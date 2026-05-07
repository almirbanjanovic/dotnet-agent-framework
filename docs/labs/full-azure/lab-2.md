# Lab 2 — Single & Multi-Agent Workflows (Full Azure Track)

> **Track:** Full Azure — production-shaped: AKS pods, Cosmos DB, AI Search, GitHub OIDC.
> Looking for the Local Track instead? See [`../local/lab-2.md`](../local/lab-2.md).

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
3. **Specialists are processes.** No agent imports another agent's code. The orchestrator doesn't `using Contoso.CrmAgent;` — it speaks JSON to the specialist's HTTP endpoint.

You will:

- Drive the single-agent pattern directly, observing tool discovery and tool-call traces.
- Drive the orchestrator and watch it pick the right specialist for different prompts.
- Add a third specialist (a **Returns Agent**) and wire it into the orchestrator's routing — without touching the existing CRM or Product agents.

On the Full Azure Track, the agent code is identical to the Local Track. The only differences are:

- Endpoints are reachable through the Application Gateway for Containers (AGC), not `localhost`.
- Chat requests carry a Microsoft Entra ID Bearer token (issued by MSAL in the Blazor UI). Call from a browser session signed in as one of the seeded test users — see [docs/security.md](../../security.md#test-users).
- Specialist agents authenticate to Azure AI Foundry as their own Entra **Agent Identity** (`id-crm-agent`, `id-prod-agent`, `id-orch-agent`) — every tool call is auditable to a specific agent.
- `crm-mcp` and `knowledge-mcp` enforce per-caller authorization: the MCP servers reject calls from agents whose workload identities don't have the required Entra Agent ID app role.

## Prerequisites

- [Lab 1](lab-1.md) completed (infrastructure deployed, services running on AKS).
- All 8 services healthy (AKS readiness probes green).
- A modern browser (Edge, Chrome, Firefox) — you'll drive every scenario through the **Blazor UI** the same way a real customer would.

## The architecture you'll exercise

```text
                        ┌──────────────┐
   "Where is order      │ orchestrator │  classify intent
    1003?"  ─────────►  │   agent      │  (one LLM call,
                        │  (k8s svc)   │   ToolMode = None,
                        └──────┬───────┘   max 16 tokens)
                               │
              ┌────────────────┴──────────────────┐
              ▼                                   ▼
        ┌──────────┐                       ┌──────────────┐
        │ crm-agent│                       │ product-agent│
        │  (k8s)   │                       │   (k8s)      │
        └─────┬────┘                       └─────┬────────┘
              │                                  │
       ┌──────┴───────┐                   ┌──────┴───────┐
       ▼              ▼                   ▼              ▼
  ┌──────────┐  ┌──────────────┐    ┌──────────┐  ┌──────────────┐
  │ crm-mcp  │  │ knowledge-mcp│    │ crm-mcp  │  │ knowledge-mcp│
  │  (k8s)   │  │   (k8s)      │    │  (k8s)   │  │   (k8s)      │
  │ 11 tools │  │   1 tool     │    │ 11 tools │  │   1 tool     │
  └──────────┘  └──────────────┘    └──────────┘  └──────────────┘
```

Each box is a separate pod with its own workload identity. The orchestrator does **not** import any agent's code — it calls them over HTTP through Kubernetes service DNS.

## Step 1 — Sign in via the Blazor UI

Open `https://{agc-frontend-fqdn}/` (the URL is in the `terraform output` from Lab 1). Sign in as a seeded test user (e.g., `emma.wilson@{your-domain}`). The chat panel uses **Server-Sent Events** at `POST /api/v1/chat/stream` so tokens render as they arrive.

> **Tracing on AKS today.** Distributed tracing (App Insights / Application Map) is **not** wired up in this Track yet — `infra/terraform/diagnostics.tf` only ships Cosmos and Key Vault diagnostics to Log Analytics, and the AKS cluster ships pod stdout via Container Insights. To follow a chat turn end-to-end on Azure, use one of:
>
> - **Browser DevTools → Network** — same as the Local Track. The request appears as a long-pending `fetch` to `POST /api/v1/chat/stream`; the **Response** tab shows the SSE event frames (`event: conversation`, `event: stage`, `event: tool`, `event: token`, `event: done`).
> - **Log Analytics → Container Insights** (workspace provisioned by `module.aks`) — query `ContainerLogV2 | where ContainerName in ("bff-api","orchestrator-agent","crm-agent","product-agent","crm-mcp","knowledge-mcp")` to follow `ILogger` output across pods. To stitch hops together, filter on the W3C `traceparent` header that each service logs.
>
> If your goal is the same one-click span tree the Local Track gets from the Aspire dashboard, that requires adding an Application Insights resource + the `Azure.Monitor.OpenTelemetry.AspNetCore` exporter to each `ServiceDefaults.cs`. Tracked separately from this lab.

Type into the chat:

> Where is my last order?

The BFF maps the signed-in UPN to a customer automatically via
`AzureAd:CustomerMap` (written by `infra/init.ps1`) — no customer picker is
shown in MSAL mode, so whichever seeded user you logged in as is who the
agents will look up. While the response renders, watch the chat panel render
token-by-token, then open browser DevTools → Network → click
`POST /api/v1/chat/stream` → **Response** tab to confirm the SSE event sequence.
To stitch the cross-pod call chain, open **Azure portal → your AKS cluster →
Logs** and run the KQL example in the call-out above. (One-click distributed
traces require the App Insights wiring noted as a follow-up.)

The SSE wire frames `bff-api` emits to the browser are the same as the Local
Track:

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
…more token frames, a second tool call, more tokens…

event: done
data: {"conversationId":"...","toolCalls":[{"name":"get_customer_orders","arguments":{"customerId":"101"}},{"name":"get_order_detail","arguments":{"orderId":"1001"}}]}
```

### What just happened (under the hood)

The per-request flow lives in `src/crm-agent/Endpoints/ChatEndpoint.cs`
(the agent itself is built in `src/crm-agent/Services/CrmAgentFactory.cs`,
the MCP clients in `src/crm-agent/Services/Mcp/`). On every request:

1. The agent fetches tool catalogs from both MCP servers via `client.ListToolsAsync()` — this is the **runtime tool discovery** that makes the agent decoupled from the MCP server's implementation.
2. The combined tool list is handed to the `AIAgent`.
3. The agent runs an LLM turn, sees the user wants order data, calls `get_customer_orders`, then `get_order_detail`, then composes a reply.
4. `ToolCallExtractor` walks the response messages and emits the tool-call trace you see in the JSON.

The per-request loop in code:

```csharp
// 1. Fetch tools dynamically from each MCP server (no compile-time coupling).
var tools = new List<AITool>();
tools.AddRange(await crmClient.ListToolsAsync(cancellationToken: ct));
tools.AddRange(await knowledgeClient.ListToolsAsync(cancellationToken: ct));

// 2. Build an agent for this request, with the freshly-discovered tools.
var agent = agentFactory.CreateAgent(promptProvider.Prompt, tools);

// 3. Run one or more LLM turns until the model stops asking for tools.
var response = await agent.RunAsync(messages, cancellationToken: ct);

// 4. Extract the tool calls the model made (for the response trace).
var toolCalls = ToolCallExtractor.Extract(response);
```

> **File map.** Every agent component follows the same layout:
>
> | Folder | Holds |
> |--------|-------|
> | `Program.cs` | composition root only — DI registrations, health-check wiring, endpoint mapping |
> | `Models/` | wire DTOs (`ChatRequest`, `ChatResponse`, etc.) |
> | `Services/` | the agent factory, MCP client cache, prompt loader, helpers |
> | `Services/Mcp/` | one file per MCP backend (`CrmMcpClientProvider.cs`, `KnowledgeMcpClientProvider.cs`) |
> | `HealthChecks/` | one `IHealthCheck` per file |
> | `Endpoints/` | the `Map*Endpoint` extension methods called from `Program.cs` |

## Step 2 — Drive the orchestrator from a script (optional)

If you want to script the same scenarios without going through the UI, acquire
a token first. The BFF's App ID URI is `api://{bff-client-id}` (the Terraform
`entra` module sets `identifier_uri = "api://${azuread_application.bff.client_id}"`),
so resolve the client ID from `terraform output` first:

```powershell
$bffClientId = (terraform -chdir=infra/terraform output -raw bff_client_id)
$token = (az account get-access-token --resource "api://$bffClientId" --query accessToken -o tsv)
$body = @{
    customerId = "103"
    message    = "Where is order 1003?"
    history    = @()
} | ConvertTo-Json

Invoke-RestMethod `
    -Uri https://{agc-frontend-fqdn}/api/v1/chat `
    -Method Post `
    -ContentType "application/json" `
    -Headers @{ Authorization = "Bearer $token" } `
    -Body $body
```

The BFF validates the JWT, attaches the customer's Entra object ID as `X-Customer-Entra-Id`, and forwards to the orchestrator. The orchestrator and specialists never see the user's token directly — they only see the customer ID.

Send these two prompts back-to-back and inspect Container Insights logs (or DevTools → Network for the SSE response) between each:

> Are there any sales on hiking boots?

The trace should branch into a `product-agent` call (no `crm-agent` involvement).

> Where is order 1003?

The trace should branch into a `crm-agent` call instead.

That's intent-based handoff: the orchestrator made one tiny LLM call to classify (`PRODUCT` vs `CRM`), then forwarded the original message to the right specialist.

### Read the orchestrator's source

Open IntentClassifier.cs and AgentRouter.cs. Two short files — together they are the entire orchestration layer:

- `IntentClassifier` — one LLM call, `ToolMode = ChatToolMode.None`, `MaxOutputTokens = 16`. The prompt asks for `CRM` or `PRODUCT` and a regex pulls the answer out (resilient to the model adding punctuation or markdown noise).
- `AgentRouter` — a `switch` over the label, an `HttpClient.PostAsJsonAsync` to the chosen specialist. That's it. No middleware, no broker, no shared DTO library.

The `ChatClientAgentRunOptions` block guarantees a deterministic single-token response:

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

## Step 3 — Add a third specialist (Returns Agent)

This is the exercise that proves the pattern. You'll add a `returns-agent` that handles refund-status questions, then wire the orchestrator to route to it. **The crm-agent and product-agent must not change** — that's the whole point of independent components.

### 3a — Scaffold the new component

The fastest path is to clone `src/crm-agent/`:

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

4. Add `returns-agent` to the solution: `dotnet sln add src/returns-agent/Contoso.ReturnsAgent.csproj`.

### 3b — Teach the orchestrator about the new domain

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

Three small edits, in three files:

1. **`Services/AgentClients.cs`** — add a `ReturnsAgentClient` next to `ProductAgentClient`:

   ```csharp
   internal sealed class ReturnsAgentClient
   {
       public ReturnsAgentClient(HttpClient httpClient) => HttpClient = httpClient;
       public HttpClient HttpClient { get; }
   }
   ```

2. **`Services/AgentRouter.cs`** — accept the new client in the constructor, then add a third branch in **both** `RouteAsync` (used by `/api/v1/chat`) **and** `RouteStreamAsync` (used by the SSE chat panel — the streaming method is what the browser actually exercises, and skipping it leaves RETURNS traffic falling back to crm-agent):

   ```csharp
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
           var baseUrl = builder.Configuration["ReturnsAgent:BaseUrl"] ?? "http://returns-agent:8080";
           client.BaseAddress = new Uri(baseUrl);
       })
       .AddHttpMessageHandler<CustomerHeaderForwarder>();
   ```

4. **`Endpoints/ChatEndpoint.cs`** — the SSE `stage` event tells the
   browser which agent the request was routed to. Update the label
   computation so RETURNS traffic surfaces as `"returns"` in the trace
   instead of being mis-labelled `"crm"`:

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

### 3c — Deploy the new component to AKS

1. **Identity** — add a `returns_agent` entry to the existing `agent_identity`
   module's `agents` map in `infra/terraform/main.tf` (mirror the existing
   `crm_agent` entry, including the new `sa-returns-agent` service-account
   name). The map shape is:

   ```hcl
   agents = {
     crm_agent     = { blueprint_display_name = "Contoso CRM Agent",     namespace = var.k8s_namespace, service_account = "sa-crm-agent" }
     prod_agent    = { blueprint_display_name = "Contoso Product Agent", namespace = var.k8s_namespace, service_account = "sa-prod-agent" }
     orch_agent    = { blueprint_display_name = "Contoso Orchestrator Agent", namespace = var.k8s_namespace, service_account = "sa-orch-agent" }
     returns_agent = { blueprint_display_name = "Contoso Returns Agent", namespace = var.k8s_namespace, service_account = "sa-returns-agent" }
   }
   ```

   Re-run `./infra/deploy.ps1` so Terraform creates the new agent-identity
   blueprint, service principal, and federated-identity credential.
2. **Container image** — clone src/crm-agent/Dockerfile into the new component (no changes needed; the multi-stage build picks up the new csproj).
3. **Helm chart** — clone src/crm-agent/chart/ into src/returns-agent/chart/, update Chart.yaml, values.yaml (image repository, service account name `sa-returns-agent`), and the ConfigMap keys. Helm template files are reusable.
4. **NetworkPolicy** — add a new manifest under infra/k8s/manifests/network-policies/ allowing ingress from `orchestrator-agent` and egress to `crm-mcp` + `knowledge-mcp` (mirror crm-agent.yaml).
5. **CI/CD** — copy `.github/workflows/deploy-crm-agent.yml` to `deploy-returns-agent.yml`, replace `crm-agent` with `returns-agent` in the path filter, `SERVICE_NAME`, csproj path, test project, Dockerfile path, and chart path. Optionally add `returns-agent` to the matrix in `.github/workflows/deploy-all-services.yml` (under `tier-3`, alongside `crm-agent` and `product-agent`) so future full-fleet deploys pick it up too.
6. **Orchestrator config** — bump the `orchestrator-agent` ConfigMap to add `ReturnsAgent:BaseUrl: http://returns-agent:8080`.

The component-independence fitness test (`ComponentIndependenceTests`) and the template-hygiene fitness test (`LocalDevTemplateTests`) both run on every PR — they will catch most omissions.

## Verification checklist

- [ ] Signing in as a seeded user lets you chat through the Blazor UI
- [ ] Container Insights / pod stdout shows the orchestrator and the chosen specialist agent both received the request (filter `ContainerLogV2` by `ContainerName` and the W3C `traceparent` header)
- [ ] Routing decisions are visible in `orchestrator-agent` pod logs
- [ ] After Step 3, `kubectl get pods -n contoso` shows a new `returns-agent` pod with the `sa-returns-agent` service account
- [ ] Asking the orchestrator a refunds question returns a response from `returns-agent` (visible in its pod logs)
- [ ] `crm-agent` source has not changed (`git diff src/crm-agent` is empty)
- [ ] `ComponentIndependenceTests` is still green

## What's next

Continue to [Lab 3](lab-3.md) for the human-in-the-loop fraud detection workflow.
