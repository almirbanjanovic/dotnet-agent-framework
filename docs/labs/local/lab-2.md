# Lab 2 — Single & Multi-Agent Workflows (Local Track)

> **Track:** Local — Foundry only, everything else runs on your laptop.
> Looking for the Full Azure Track instead? See [`../full-azure/lab-2.md`](../full-azure/lab-2.md).

> **Rusty on .NET / web apps? Read this first.** This lab is meant to be
> followed even if you haven't written code in a year. Three things will help:
>
> 1. Treat each of the 8 services in the Aspire dashboard as a **separate
>    program running on its own port**. They communicate by sending HTTP
>    requests to each other — exactly like calling a public web API.
> 2. Whenever you see a port number (5001–5008), match it to a folder under
>    `src/` (e.g. port 5004 = `src/crm-agent/`). The "How a single chat
>    message travels" table further down maps every port hop to a specific
>    file and method.
> 3. Don't memorize the framework. Every agent in this repo is built
>    around the same **three core lines** you saw in
>    [Lab 1's `simple-agent`](lab-1.md#how-simple-agent-works-your-first-microsoft-agent-framework-call):
>    `DefaultAzureCredential` → `AsAIAgent(...)` → `RunAsync(...)`. The
>    web-hosted agents below add three more things on top — a system
>    prompt loaded from a markdown file, an HTTP endpoint that calls
>    `RunStreamingAsync` instead of `RunAsync`, and a list of MCP tools
>    discovered at request time. That's it.

> **Picture this.** Emma is logged in. She types *"where is my order
> #1001?"* in the chat panel and hits Enter. Six processes on your laptop
> wake up and pass that question down a chain of HTTP calls. The model
> (`gpt-4.1`) is asked **about four times**: once to classify the intent,
> then twice or three more times in the CRM specialist's tool loop
> (call a tool, see the result, decide what to do next). Two MCP tool
> servers each get hit at least once, and the answer streams back to
> the browser token-by-token — typically in a few seconds.
>
> **Lab 2 is one long answer to "what just happened when I hit Enter?"**
> You'll see every box on the architecture diagram light up in the
> Aspire **Traces** tab, watch SSE frames in the browser DevTools, then
> add a brand-new **Returns Agent** in Step 5 without touching a line of
> the existing services.

## What you'll learn

This lab walks you through two patterns of the [Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/overview/?pivots=programming-language-csharp) using the agents already shipped in this repo:

1. **Single-agent + MCP tools** — a single `AIAgent` discovers tools at runtime from one or more MCP servers and decides when to call them. The `crm-agent` is the canonical example: it's a one-shot specialist that talks to `crm-mcp` (11 CRM tools) and `knowledge-mcp` (1 RAG tool) and produces a natural-language answer.
2. **Multi-agent orchestration with intent-based handoff** — a thin `orchestrator-agent` classifies the user's intent, then routes the request to a specialist agent (`crm-agent` or `product-agent`). Each specialist owns its own tools, prompts, identity, and process. There is **no shared library** between them — communication is HTTP/JSON only.

By the end you'll understand the contract that makes specialists pluggable and why this scales to dozens of agents owned by different teams.

### Microsoft Agent Framework primer

If [Lab 1](lab-1.md#how-simple-agent-works-your-first-microsoft-agent-framework-call) was your first taste, the agents in this lab add three more concepts. Every one of them surfaces as a few lines of C# in the existing components — there are no hidden frameworks to learn.

| Concept | Type | Where it lives | What it does |
|---------|------|----------------|--------------|
| **AIAgent** | `Microsoft.Agents.AI.AIAgent` | every agent | The runnable thing. Built once via `AsAIAgent(...)`, called via `RunAsync(prompt or messages)`. |
| **AITool (MCP-discovered)** | `Microsoft.Extensions.AI.AITool` | [`crm-agent/Program.cs`](../../../src/crm-agent/Program.cs) | Each MCP server publishes a tool catalog over HTTP. `client.ListToolsAsync()` fetches it at request time and hands the list to the agent. The agent decides when (and whether) to call each tool. |
| **ChatClientAgentRunOptions** | `Microsoft.Agents.AI.ChatClientAgentRunOptions` | [`orchestrator-agent/Services/IntentClassifier.cs`](../../../src/orchestrator-agent/Services/IntentClassifier.cs) | Per-call knobs (max tokens, temperature, `ToolMode`). The orchestrator uses `ToolMode = ChatToolMode.None` to keep the classifier from accidentally calling tools. |
| **Multi-agent handoff** | plain `HttpClient.PostAsJsonAsync` | [`orchestrator-agent/Services/AgentRouter.cs`](../../../src/orchestrator-agent/Services/AgentRouter.cs) | After classifying intent, the orchestrator forwards the *original* request to the chosen specialist over HTTP. There's deliberately no shared C# DTO library — the wire contract is JSON. |

Three rules to keep in your head:

1. **One LLM call per `RunAsync`** unless you pass tools — then the framework loops "model → tool call → model" until the model stops asking for tools or hits a token cap.
2. **Tools are discovered, not coded.** Replacing `crm-mcp` with a different process that exposes the same tool names requires zero changes to `crm-agent`.
3. **Specialists are processes.** No agent imports another agent's code. The orchestrator doesn't `using Contoso.CrmAgent;` — it speaks JSON to `http://localhost:5004/api/v1/chat`.

You will:

- Drive the single-agent pattern directly, observing tool discovery and tool-call traces.
- Drive the orchestrator and watch it pick the right specialist for different prompts.
- Add a third specialist (a **Returns Agent**) and wire it into the orchestrator's routing — without touching the existing CRM or Product agents.

## Prerequisites

- [Lab 1](lab-1.md) completed.
- All 8 services healthy in the Aspire dashboard.
- A modern browser (Edge, Chrome, Firefox) — you'll drive every scenario through the **Blazor UI** the same way a real customer would, and inspect what's happening underneath via the **Aspire dashboard** (`https://localhost:15888`) and the browser's **Network** dev tool.

> **Why the UI, not curl?** This repo is the customer experience for Contoso Outdoors. Every prompt below should be **typed into the chat panel** by a signed-in test user — that's what exercises the full token → BFF → orchestrator → specialist → MCP path.

### How to trace a chat message

The chat panel uses **Server-Sent Events (SSE)** so the assistant's reply renders token-by-token. That changes what you see in DevTools, so use the right tool for the question you're asking:

| Question | Surface | What you see |
|----------|---------|--------------|
| **What was called, in what order, how long did each hop take?** | Aspire dashboard → **Traces** tab (`https://localhost:15888`) | One trace per chat turn, span tree across `bff-api → orchestrator-agent → crm-agent / product-agent → crm-mcp / knowledge-mcp → azure_openai`. This is the canonical surface — the next sections all use it. |
| **What did the BFF receive / send on the wire?** | Browser DevTools → **Network** tab, filter `stream` | The request appears as a **`fetch`** call to `POST /api/v1/chat/stream` that stays in **Pending** state until the stream finishes, then turns 200. There is **no** old-style `chat` XHR — the buffered endpoint is gone from the UI. |
| **What were the individual SSE events the browser received?** | Same Network entry → **Response** tab (recent Chromium versions also surface an **EventStream** tab for `text/event-stream` responses) | Sequence of frames in this order: `event: conversation` (from BFF) → `event: stage` ×2 (from orchestrator: `classifying`, then `routed`) → interleaved `event: token` / `event: tool` (from the specialist) → `event: done` (from BFF, after persistence). Failures emit `event: error`. |
| **What were the per-service logs while it ran?** | Aspire dashboard → **Console Logs** or **Structured Logs** tab, pick a service | Per-service stdout + structured `ILogger` output — useful when a span is red. |

Keep the dashboard's Traces tab open in one browser window and the chat in another so you can see both at once.

## How a single chat message travels (file-by-file)

> **Read this once, even if you skim everything else.** It maps every hop in
> the architecture diagram below to a specific **file** and **method** so you
> can pause the lab at any point and read the code that just ran.

Sign in as **emma** (the test user from `local-dev-credentials.txt`),
type **`Where is my order #1001?`** in the chat panel, and hit Enter. Order
1001 belongs to Emma (customer `101`) in the seed data, so the example
below uses real IDs you can verify yourself. Here is what runs, in order,
on a single laptop. Each step lists the file you'd open in VS Code if you
wanted to read along — most of these methods are short, and a few (like
the BFF and Blazor handlers) are longer because they own the SSE plumbing.

| # | Where it runs | File and method | What it does |
|---|---------------|-----------------|--------------|
| 1 | Browser (Blazor WASM) | [`src/blazor-ui/Shared/ChatBubble.razor`](../../../src/blazor-ui/Shared/ChatBubble.razor) → `SendAsync()` | Captures the textarea text (when you click Send or press Enter), appends it to the on-page `history` list, and calls the BFF client. |
| 2 | Browser (Blazor WASM) | [`src/blazor-ui/Services/BffApiClient.cs`](../../../src/blazor-ui/Services/BffApiClient.cs) → `SendChatStreamAsync()` | Opens an HTTP POST to **`/api/v1/chat/stream`** on the BFF and asks the browser to expose the response body as a live stream (so tokens render as they arrive). The MSAL bearer token is attached **automatically** by a registered handler — see [`BffAuthorizationMessageHandler.cs`](../../../src/blazor-ui/Services/BffAuthorizationMessageHandler.cs), wired up in `blazor-ui/Program.cs` via `AddHttpMessageHandler<BffAuthorizationMessageHandler>()`. |
| 3 | BFF (process on port 5007) | [`src/bff-api/Endpoints/ChatEndpoint.cs`](../../../src/bff-api/Endpoints/ChatEndpoint.cs) → `HandleStreamAsync()` | Validates the JWT, maps the signed-in UPN to a customer ID via `AzureAd:CustomerMap`, loads or creates the conversation, persists the new user message, sets `Content-Type: text/event-stream`, and forwards to the orchestrator. |
| 4 | Orchestrator (port 5006) | [`src/orchestrator-agent/Endpoints/ChatEndpoint.cs`](../../../src/orchestrator-agent/Endpoints/ChatEndpoint.cs) → `HandleStreamAsync()` | Sends `event: stage data:{"stage":"classifying"}` to the browser, then invokes the intent classifier. |
| 5 | Orchestrator | [`src/orchestrator-agent/Services/IntentClassifier.cs`](../../../src/orchestrator-agent/Services/IntentClassifier.cs) → `ClassifyAsync()` | One short LLM call to Foundry: *"is this CRM or PRODUCT?"*. The knobs are set inside a `ChatClientAgentRunOptions` (`MaxOutputTokens = 16`, `Temperature = 0`, `ToolMode = ChatToolMode.None`) so the model can only emit a label. (You'll add a third category in Step 5 of this lab.) |
| 6 | Orchestrator | [`src/orchestrator-agent/Endpoints/ChatEndpoint.cs`](../../../src/orchestrator-agent/Endpoints/ChatEndpoint.cs) → `HandleStreamAsync()` | Sends `event: stage data:{"stage":"routed","agent":"crm"}` to the browser, then asks the router to open a stream against the chosen specialist. |
| 7 | Orchestrator | [`src/orchestrator-agent/Services/AgentRouter.cs`](../../../src/orchestrator-agent/Services/AgentRouter.cs) → `RouteStreamAsync()` | A `switch` on the intent picks `_crmClient.HttpClient` (port 5004), then opens an HTTP POST to **`/api/v1/chat/stream`** on `crm-agent`. |
| 8 | crm-agent (port 5004) | [`src/crm-agent/Endpoints/ChatEndpoint.cs`](../../../src/crm-agent/Endpoints/ChatEndpoint.cs) → `HandleStreamAsync()` | Calls `crmProvider.GetClientAsync()` and `knowledgeProvider.GetClientAsync()` to get MCP clients, then `client.ListToolsAsync()` on each to fetch the **tool catalogs at runtime**. The agent now has 12 tools it can choose from. |
| 9 | crm-agent | [`src/crm-agent/Services/CrmAgentFactory.cs`](../../../src/crm-agent/Services/CrmAgentFactory.cs) → `CreateAgent()` | Builds a fresh `AIAgent` for this request with the system prompt + the 12 tools. |
| 10 | crm-agent | [`src/crm-agent/Endpoints/ChatEndpoint.cs`](../../../src/crm-agent/Endpoints/ChatEndpoint.cs) → `agent.RunStreamingAsync()` | The model + tool loop runs here. **First** model call: it sees the prompt + history + tool list and decides to call `get_customer_orders(customerId: "101")`. The framework runs the tool and sends the JSON result back. **Second** model call: it decides to call `get_order_detail(orderId: "1001")`. **Third** model call: it has enough info and finally writes the natural-language reply. Each model call is its own `azure_openai chat.completions` span in the trace tree. |
| 11 | crm-mcp (port 5002) | [`src/crm-mcp/Tools/CustomerTools.cs`](../../../src/crm-mcp/Tools/CustomerTools.cs), [`OrderTools.cs`](../../../src/crm-mcp/Tools/OrderTools.cs), [`ProductTools.cs`](../../../src/crm-mcp/Tools/ProductTools.cs), [`SupportTicketTools.cs`](../../../src/crm-mcp/Tools/SupportTicketTools.cs) | Each tool call from step 10 is one HTTP round-trip. crm-mcp reads from in-memory CSV-backed data and returns JSON. |
| 12 | crm-agent | [`src/crm-agent/Endpoints/ChatEndpoint.cs`](../../../src/crm-agent/Endpoints/ChatEndpoint.cs) → `HandleStreamAsync()` | As the model writes its reply, each token is wrapped in `event: token data:{"text":"..."}` and flushed back. Tool calls become `event: tool` frames. End-of-reply is `event: done`. |
| 13 | Orchestrator | [`src/orchestrator-agent/Endpoints/ChatEndpoint.cs`](../../../src/orchestrator-agent/Endpoints/ChatEndpoint.cs) → `HandleStreamAsync()` | Receives the open `HttpResponseMessage` from `RouteStreamAsync()` and pipes the bytes straight into its own response stream with `upstreamStream.CopyToAsync(...)`. **No parsing, no buffering** — the orchestrator never sees a whole reply, just bytes flowing through. That's why the orchestrator span in the trace tree matches the specialist span's duration almost exactly. |
| 14 | BFF | [`src/bff-api/Endpoints/ChatEndpoint.cs`](../../../src/bff-api/Endpoints/ChatEndpoint.cs) → `HandleStreamAsync()` | Pipes orchestrator bytes back to the browser, but also **assembles** the tokens into a single string in memory so it can persist the assistant's full reply via `PersistAssistantAsync(...)` after the stream ends. Then it emits its own `event: done`. |
| 15 | Browser | [`src/blazor-ui/Services/BffApiClient.cs`](../../../src/blazor-ui/Services/BffApiClient.cs) → `SendChatStreamAsync()` SSE state machine | Reads the bytes line-by-line, parses SSE event blocks, and yields a `ChatStreamEvent` to the chat bubble for each one. |
| 16 | Browser | [`src/blazor-ui/Shared/ChatBubble.razor`](../../../src/blazor-ui/Shared/ChatBubble.razor) | Each event triggers a re-render: `token` events append to the assistant bubble, `tool` events show a "🔧 calling tool…" chip, `done` closes the request. |

That's it. **No message broker, no shared C# types, no service mesh.** The
BFF, orchestrator, and crm-agent all use ordinary ASP.NET middleware
(authentication, CORS, exception handling) — what's missing is any
cross-service plumbing. Every box in the architecture diagram below is one
of these files. When you open the Aspire **Traces** tab in the next step,
the spans you see line up with steps **3, 4, 7, 8, 10, and 11**. Step 10
shows up as **multiple** `azure_openai chat.completions` spans (one per
turn in the model+tool loop) plus an `mcp` span for each tool call — you
can use this table to translate any span back into the file you want to
open.

> **Glossary** (skim if anything is unfamiliar):
>
> - **BFF** = "Backend for Frontend". The only server the browser talks to. Holds JWT validation, conversation persistence, and the customer-id lookup so the front-end stays simple.
> - **MCP** = [Model Context Protocol](https://modelcontextprotocol.io/). A small spec for "here is a server that exposes tools an LLM can call". Each MCP server is just an HTTP endpoint that lists its tools and executes them.
> - **SSE** = Server-Sent Events. A long-lived HTTP response where the server keeps writing `event: …\ndata: …\n\n` blocks. The browser's `fetch` API exposes the response body as a live stream — that's what `httpRequest.SetBrowserResponseStreamingEnabled(true)` (called inside `BffApiClient.SendChatStreamAsync`) opts into.
> - **JWT** = JSON Web Token. The thing MSAL gives the browser after sign-in; the BFF validates its signature on every request to know which tenant + user is calling.
> - **AppHost** = The .NET Aspire orchestrator project. Reads `src/AppHost/Program.cs`, starts every `Projects.Contoso_*` referenced there as a child process, wires up service discovery and OpenTelemetry, and exposes the dashboard.
> - **Span / Trace** = OpenTelemetry concepts. A *trace* is one logical request; each *span* is one piece of work in that trace (one HTTP call, one tool execution, one LLM call). They get stitched together by the W3C `traceparent` header that every HTTP hop forwards.
> - **Classifier** = a one-shot LLM call whose only job is to label the input. Cheap (16 tokens), no tools allowed.

> **Try it before you keep reading.** With `dotnet run --project src/AppHost`
> running, sign in to the Blazor UI as **emma** (her UPN + password are in
> `local-dev-credentials.txt` at the repo root), open the Aspire dashboard's
> **Traces** tab in a second browser window, and type **`where is my order
> #1001?`** in the chat. You should see:
>
> 1. Tokens appearing one at a time in the chat panel — that's rows 12–16
>    of the table above happening live.
> 2. A new trace whose span tree covers rows 3, 4, 5, 7, 8, 10, and 11.
>    Step 10 expands into multiple `azure_openai chat.completions` spans
>    (one per turn in the model + tool loop) plus an `mcp` span per tool
>    call — click any `mcp` span to see arguments like
>    `get_customer_orders(customerId: "101")`.
>
> That's the table above made tangible. Everything else in Lab 2 just
> zooms in on a specific piece of this same flow.

## The architecture you'll exercise

```text
                        ┌──────────────┐
   "Where is order      │ orchestrator │  classify intent
    1003?"  ─────────►  │   agent      │  (one LLM call,
                        │  (5006)      │   ToolMode = None,
                        └──────┬───────┘   max 16 tokens)
                               │
              ┌────────────────┴──────────────────┐
              ▼                                   ▼
        ┌──────────┐                       ┌──────────────┐
        │ crm-agent│                       │ product-agent│
        │  (5004)  │                       │   (5005)     │
        └─────┬────┘                       └─────┬────────┘
              │                                  │
       ┌──────┴───────┐                   ┌──────┴───────┐
       ▼              ▼                   ▼              ▼
  ┌──────────┐  ┌──────────────┐    ┌──────────┐  ┌──────────────┐
  │ crm-mcp  │  │ knowledge-mcp│    │ crm-mcp  │  │ knowledge-mcp│
  │  (5002)  │  │   (5003)     │    │  (5002)  │  │   (5003)     │
  │ 11 tools │  │   1 tool     │    │ 11 tools │  │   1 tool     │
  └──────────┘  └──────────────┘    └──────────┘  └──────────────┘
```

Each box is a separate process with its own identity. The orchestrator does **not** import any agent's code — it calls them over HTTP.

## Step 1 — Confirm the system is running

```powershell
dotnet run --project src/AppHost
```

Open the Aspire dashboard at `https://localhost:15888`. Confirm 8 services are green: `crm-api`, `crm-mcp`, `knowledge-mcp`, `crm-agent`, `product-agent`, `orchestrator-agent`, `bff-api`, `blazor-ui`.

## Step 2 — Single-agent pattern: drive `crm-agent` through the chat UI

Open `http://localhost:5008` and sign in as **emma.wilson-local@<your-tenant-domain>** (UPN + password come from **`local-dev-credentials.txt` at the repo root** — the file `setup-local` wrote in Lab 1; `cat local-dev-credentials.txt` to see all 8). The BFF maps the signed-in UPN to **Emma Wilson (customer 101)** automatically via the `AzureAd:CustomerMap` it received from `setup-local` — there is no customer picker.

Open the chat panel by clicking **Ask the experts** in the green hero on the home page (or the floating chat icon in the bottom-right corner of any page), and send:

> Where is my last order?

While the response renders, open the **Aspire dashboard** (`https://localhost:15888`) and click **Traces**. Find the trace started by `bff-api` for `POST /api/v1/chat/stream`. Expand it — the span tree is the entire flow:

```text
bff-api  POST /api/v1/chat/stream
 └─ orchestrator-agent  POST /api/v1/chat/stream     (intent classifier picks CRM)
     └─ crm-agent  POST /api/v1/chat/stream          (the specialist this lab focuses on)
          ├─ crm-mcp  list_tools                      (runtime tool discovery)
          ├─ knowledge-mcp  list_tools                (runtime tool discovery)
          ├─ azure_openai  chat.completions           (turn 1 — model decides to call get_customer_orders)
          ├─ crm-mcp  call_tool: get_customer_orders
          ├─ azure_openai  chat.completions           (turn 2 — model decides to call get_order_detail)
          ├─ crm-mcp  call_tool: get_order_detail
          └─ azure_openai  chat.completions           (turn 3 — final natural-language reply)
```

The trace span propagates via the W3C `traceparent` header, so every hop ends up under the same trace ID. The BFF, orchestrator, and specialist spans are all long-lived — their durations equal the streaming time, because every hop is SSE end-to-end (the orchestrator simply pipes the specialist's bytes through to the BFF, and the BFF pipes them through to the browser).

Now open browser **DevTools → Network**, filter on `stream`, and click the entry for `POST /api/v1/chat/stream`. The request is a **`fetch`** that stays Pending until the assistant is done. Two tabs to look at:

- **Headers** — confirms `content-type: text/event-stream`
- **Response** — the SSE wire frames the BFF emits, in order:

  ```text
  event: conversation
  data: {"conversationId":"..."}

  event: stage
  data: {"stage":"classifying"}

  event: stage
  data: {"stage":"routed","agent":"crm"}

  event: tool
  data: {"name":"get_customer_orders","arguments":{"customerId":"101"}}

  event: token
  data: {"text":"Your"}
  event: token
  data: {"text":" most"}
  …more token frames, then a second tool call, then more tokens…

  event: done
  data: {"conversationId":"...","toolCalls":[{"name":"get_customer_orders","arguments":{"customerId":"101"}},{"name":"get_order_detail","arguments":{"orderId":"1001"}}]}
  ```

  > A failure anywhere in the chain surfaces as `event: error` with `data: {"message":"..."}` instead of `done`.

  These are the same `toolCalls` the trace tree shows you, just framed for the browser. The Aspire trace is faster for understanding the call order; the SSE frames are useful when you need to see exactly what the browser consumed.

### What just happened

The per-request flow lives in `src/crm-agent/Endpoints/ChatEndpoint.cs`
(the agent itself is built in `src/crm-agent/Services/CrmAgentFactory.cs`,
the MCP clients in `src/crm-agent/Services/Mcp/`). Open `ChatEndpoint.cs` and
trace the path. On every request:

1. The agent fetches tool catalogs from both MCP servers via `client.ListToolsAsync()` — this is the **runtime tool discovery** that makes the agent decoupled from the MCP server's implementation. The agent doesn't know what tools exist until it asks.
2. The combined tool list is handed to the `AIAgent`.
3. The agent runs an LLM turn, sees the user wants order data, calls `get_customer_orders`, then `get_order_detail`, then composes a reply.
4. `ToolCallExtractor` walks the response messages and emits the tool-call trace you see in the JSON.

In code, the per-request loop is just this:

```csharp
// 1. Fetch tools dynamically from each MCP server (no compile-time coupling).
var tools = new List<AITool>();
tools.AddRange(await crmClient.ListToolsAsync(cancellationToken: ct));
tools.AddRange(await knowledgeClient.ListToolsAsync(cancellationToken: ct));

// 2. Build an agent for this request, with the freshly-discovered tools.
var agent = agentFactory.CreateAgent(promptProvider.Prompt, tools);

// 3. Run one or more LLM turns until the model stops asking for tools.
//    `messages` is the user prompt + any prior turns from the conversation.
var response = await agent.RunAsync(messages, cancellationToken: ct);

// 4. Extract the tool calls the model made (for the response trace).
var toolCalls = ToolCallExtractor.Extract(response);
```

Note what is **not** there: no hardcoded tool list, no shared C# DTOs with the MCP servers, no compile-time dependency on any tool's implementation. The agent and the tool servers can be deployed independently and in different repos.

> **File map for the rest of the lab.** Every agent component follows the same layout:
>
> | Folder | Holds |
> |--------|-------|
> | `Program.cs` | composition root only — DI registrations, health-check wiring, endpoint mapping |
> | `Models/` | wire DTOs (`ChatRequest`, `ChatResponse`, etc.) |
> | `Services/` | the agent factory, MCP client cache, prompt loader, helpers |
> | `Services/Mcp/` | one file per MCP backend (`CrmMcpClientProvider.cs`, `KnowledgeMcpClientProvider.cs`) |
> | `HealthChecks/` | one `IHealthCheck` per file |
> | `Endpoints/` | the `Map*Endpoint` extension methods called from `Program.cs` |
>
> When the lab says "open `IntentClassifier.cs`" or "`AgentRouter.cs`", look in `src/orchestrator-agent/Services/`.
> The typed HTTP clients (`CrmAgentClient`, `ProductAgentClient`) live in `src/orchestrator-agent/Services/AgentClients.cs`.

## Step 3 — Single-agent pattern: try it with conversation history

In the **same** chat session (do **not** start a new one), send a follow-up:

> What's the return window for that?

The Blazor UI is keeping a `history` array on the client and sending the prior turns back with each request — that's what lets the model resolve "that" to order #1001 (the boots) without re-asking. Open the browser **Network** tab, click the new `chat/stream` request, and look at **Payload** (or **Request** body): you'll see your previous user/assistant turns inside the `history` array. (The request body is plain JSON; only the *response* is streamed.)

Now open the Aspire dashboard's **Traces** tab and find the new trace. The model's tool choice should be different this turn — instead of `get_customer_orders`, you should see a span for `knowledge-mcp` `call_tool: search_knowledge_base` (the one tool that MCP server exposes). Same agent, same code, different tool — picked by the model based on intent. That's the whole point of runtime tool discovery: the agent doesn't pre-select tools, the model does.

## Step 4 — Multi-agent pattern: watch the orchestrator route

Every prompt you've sent so far has actually gone through `orchestrator-agent` first — the BFF doesn't talk to specialists directly. To **see the routing happen**, start a fresh chat (refresh the browser) and send these two prompts back-to-back, watching the Aspire **Traces** tab between each:

> Are there any sales on hiking boots?

Expand the new trace. You should see the orchestrator span branch into a `product-agent` `POST /api/v1/chat/stream` span (no `crm-agent` span at all).

> Where is order 1003?

Expand this trace. The orchestrator should branch into a `crm-agent` `POST /api/v1/chat/stream` span instead.

That's intent-based handoff: the orchestrator made one tiny LLM call to classify (`PRODUCT` vs `CRM`), then forwarded the original message to the right specialist. The orchestrator does **not** call any tool itself — it doesn't even import another agent's code.

### Read the orchestrator's source

Open IntentClassifier.cs and AgentRouter.cs. Two short files — together they are the entire orchestration layer:

- `IntentClassifier` — one LLM call, `ToolMode = ChatToolMode.None`, `MaxOutputTokens = 16`. The prompt asks for `CRM` or `PRODUCT` and a regex pulls the answer out (resilient to the model adding punctuation or markdown noise).
- `AgentRouter` — a `switch` over the label, an `HttpClient.PostAsJsonAsync` to the chosen specialist. That's it. No middleware, no broker, no shared DTO library.

The key snippet is the `ChatClientAgentRunOptions` block. By telling the model `MaxOutputTokens = 16` and `ToolMode = None`, you guarantee the classifier emits exactly the label you asked for and never wanders off to call a tool:

```csharp
// IntentClassifier.cs — the entire "classification" call.
_runOptions = new ChatClientAgentRunOptions(new ChatOptions
{
    MaxOutputTokens = 16,
    Temperature     = 0,
    ToolMode        = ChatToolMode.None
});

var response = await _agent.RunAsync(prompt, options: _runOptions, ct);
// response.ToString() == "CRM" or "PRODUCT"
```

This is the pattern the [Microsoft Agent Framework handoff design](https://github.com/microsoft/agent-framework) calls **direct-line with lazy classification** — classify only when you must, then let the specialist run the conversation.

## Step 5 — Add a third specialist (Returns Agent) without touching the others

This is the exercise that proves the pattern. You'll add a `returns-agent` that handles refund-status questions, then wire the orchestrator to route to it. **The crm-agent and product-agent must not change** — that's the whole point of independent components.

### 5a — Scaffold the new component

The fastest path is to clone `src/crm-agent/`. The `-Exclude` list keeps your
personal `appsettings.Local.json` (with your Foundry endpoint and tenant ID),
`bin/`, and `obj/` from leaking into the new component:

```powershell
Copy-Item -Recurse src/crm-agent src/returns-agent -Exclude bin,obj,appsettings.Local.json,appsettings.Development.json
Push-Location src/returns-agent
Rename-Item Contoso.CrmAgent.csproj Contoso.ReturnsAgent.csproj
Pop-Location
```

Then in the new directory:

1. Open Contoso.ReturnsAgent.csproj and update the `<RootNamespace>` and `<AssemblyName>` to `Contoso.ReturnsAgent`.
2. Open Program.cs and replace `using Contoso.CrmAgent` with `using Contoso.ReturnsAgent` (and update the namespace declaration).
3. Replace Prompts/system-prompt.md with a returns-focused prompt:

   ```markdown
   You are the Returns Specialist for Contoso Outdoors.

   Your expertise:
   - Refund status, refund processing timelines
   - Return label issuance
   - Returns policy (window, condition, exceptions)

   Rules:
   - The customer's ID is provided in each request. Use it to look up their data.
   - Always use tools to retrieve order and ticket data — never fabricate.
   - For policy questions, search the knowledge base first.
   - If the user asks about anything other than returns or refunds, respond exactly:
     "This is outside my area. Let me connect you with the right specialist."
   ```

4. Update appsettings.Local.json.template to mirror the existing
   `src/crm-agent/appsettings.Local.json.template` (the port comes from
   AppHost's `WithHttpEndpoint(port: 5009, ...)`, not from the template):

   ```json
   {
     "Foundry": {
       "ProjectEndpoint": "{{FOUNDRY_PROJECT_ENDPOINT}}",
       "DeploymentName": "{{CHAT_DEPLOYMENT_NAME}}"
     },
     "AzureAd":      { "TenantId": "{{TENANT_ID}}" },
     "CrmMcp":       { "BaseUrl": "http://localhost:5002" },
     "KnowledgeMcp": { "BaseUrl": "http://localhost:5003" },
     "Logging":      { "LogLevel": { "Default": "Information" } }
   }
   ```

5. Teach `setup-local` about the new component. The script's component lists
   are **hardcoded** (no auto-discovery from `src/`), so you must add
   `returns-agent` in two places near the top of `infra/setup-local.ps1`
   (and the equivalent `infra/setup-local.sh` if you're on macOS / Linux):

   ```powershell
   # $PortMap — add the new port:
   @{ Port = 5009; Component = "returns-agent" }

   # $TemplateComponents — add the new template:
   "returns-agent"
   ```

   Re-run setup so the template renders:

   ```powershell
   ./infra/setup-local.ps1
   ```

### 5b — Register with AppHost

The AppHost references each child project by a generated
`Projects.Contoso_<Name>` symbol. That symbol is emitted by Aspire's source
generator, which only sees projects via explicit `<ProjectReference>`
entries in the AppHost's csproj. So the order is:

1. Add the project to the solution:

   ```powershell
   dotnet sln add src/returns-agent/Contoso.ReturnsAgent.csproj
   ```

2. Add a `<ProjectReference>` to `src/AppHost/Contoso.AppHost.csproj`
   alongside the other 8 (order doesn't matter):

   ```xml
   <ProjectReference Include="..\returns-agent\Contoso.ReturnsAgent.csproj" />
   ```

3. Restore + build once so the source generator emits the
   `Projects.Contoso_ReturnsAgent` symbol:

   ```powershell
   dotnet build src/AppHost/Contoso.AppHost.csproj
   ```

4. In `src/AppHost/Program.cs`, add the new project after the `productAgent`
   block:

   ```csharp
   var returnsAgent = AsLocal(builder.AddProject<Projects.Contoso_ReturnsAgent>("returns-agent"))
       .WithHttpEndpoint(port: 5009, name: "http")
       .WithReference(crmMcp)
       .WithReference(knowledgeMcp);
   ```

### 5c — Teach the orchestrator about the new domain

Edit IntentClassifier.cs to add a third label:

```csharp
private const string ClassificationTemplate = """
    Classify the following customer message into one of these categories:
    - CRM: order status, account info, support tickets, complaints
    - PRODUCT: product recommendations, catalog browsing, pricing, promotions, sizing, gear advice
    - RETURNS: refund status, return labels, return policy questions

    Respond with ONLY the category name (CRM, PRODUCT, or RETURNS).

    Customer message: {0}
    """;
```

Update the regex match and return:

```csharp
var token = System.Text.RegularExpressions.Regex.Match(
    raw,
    @"\b(PRODUCT|CRM|RETURNS)\b",
    System.Text.RegularExpressions.RegexOptions.IgnoreCase).Value;

return token.ToUpperInvariant() switch
{
    "PRODUCT" => "PRODUCT",
    "RETURNS" => "RETURNS",
    _         => "CRM"
};
```

Three small edits, in three files (this is where the per-component
reorganization shows its value — each concern lives where you'd expect):

1. **`Services/AgentClients.cs`** — add a `ReturnsAgentClient` next to `ProductAgentClient`:

   ```csharp
   internal sealed class ReturnsAgentClient
   {
       public ReturnsAgentClient(HttpClient httpClient) => HttpClient = httpClient;
       public HttpClient HttpClient { get; }
   }
   ```

2. **`Services/AgentRouter.cs`** — accept the new client in the constructor, then add a third branch in **both** `RouteAsync` (used by `/api/v1/chat`) **and** `RouteStreamAsync` (used by the SSE chat panel — this is the one your browser actually exercises):

   ```csharp
   // Replaces the existing `intent.Equals("PRODUCT", ...) ? ... : ...`
   // ternary in BOTH RouteAsync AND RouteStreamAsync. Without the change
   // in RouteStreamAsync the SSE chat panel will never reach returns-agent
   // — it'll fall back to crm-agent.
   var client = intent.ToUpperInvariant() switch
   {
       "PRODUCT" => _productClient.HttpClient,
       "RETURNS" => _returnsClient.HttpClient,
       _         => _crmClient.HttpClient,
   };
   ```

3. **`Program.cs`** — register the typed `HttpClient` next to the other two.
   **Do not** add `.AddStandardResilienceHandler()` here — the orchestrator
   already adds one in `ServiceDefaults.cs` via
   `ConfigureHttpClientDefaults`, and chaining a second pipeline stacks two
   sets of timeouts:

   ```csharp
   builder.Services.AddHttpClient<ReturnsAgentClient>(client =>
       {
           var baseUrl = builder.Configuration["ReturnsAgent:BaseUrl"] ?? "http://localhost:5009";
           client.BaseAddress = new Uri(baseUrl);
       })
       .AddHttpMessageHandler<CustomerHeaderForwarder>();
   ```

4. **`Endpoints/ChatEndpoint.cs`** — the SSE `stage` event sends the
   `agent` field to the browser so it can render "routed to returns-agent".
   Update the label computation to include the new branch:

   ```csharp
   // Replaces the existing
   //   var agentLabel = intent.Equals("PRODUCT", ...) ? "product" : "crm";
   var agentLabel = intent.ToUpperInvariant() switch
   {
       "PRODUCT" => "product",
       "RETURNS" => "returns",
       _         => "crm",
   };
   ```

### 5d — Verify

Restart AppHost and confirm `returns-agent` is green in the dashboard. Then go back to the Blazor UI, sign in as Emma, and send:

> When will my refund be processed?

Open the Aspire **Traces** tab — the orchestrator span should now branch into a `returns-agent` `POST /api/v1/chat/stream` span (not `crm-agent`, not `product-agent`). The `crm-agent` and `product-agent` are unchanged — only `returns-agent` (new) and `orchestrator-agent` (one classifier line + one route line) were touched. The fitness test still passes:

```powershell
dotnet test src-tests/Contoso.AppHost.Tests/Contoso.AppHost.Tests.csproj
```

## Verification checklist

- [ ] Signed in as Emma in the Blazor UI, asking "Where is my last order?" returns Emma's order data
- [ ] The Aspire **Traces** tab shows the trace branching `bff-api → orchestrator-agent → crm-agent` with two `crm-mcp` tool spans (`get_customer_orders`, `get_order_detail`)
- [ ] The browser **Network** tab shows a single `POST /api/v1/chat/stream` `fetch` request whose **Response** tab contains `event: tool` frames for both tool calls and a final `event: done`
- [ ] A follow-up turn ("What's the return window for that?") shows a `search_knowledge_base` span instead of CRM tool spans
- [ ] "Are there any sales on hiking boots?" routes to `product-agent` (visible in the trace tree); "Where is order 1003?" routes to `crm-agent`
- [ ] After Step 5, "When will my refund be processed?" routes to `returns-agent`
- [ ] `crm-agent` source has not changed (`git diff src/crm-agent` is empty)
- [ ] `ComponentIndependenceTests` is still green

## What's next

Lab 2 complete. Continue to [Lab 3](lab-3.md) for human-in-the-loop fraud workflows.
