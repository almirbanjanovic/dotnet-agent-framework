# Lab 3 — Human-in-the-Loop Workflows (Local Track)

> **Track:** Local — Foundry only, everything else runs on your laptop.
> Looking for the Full Azure Track instead? See [`../full-azure/lab-3.md`](../full-azure/lab-3.md).

> **Picture this.** It's Sunday morning. Sarah Miller (test user
> `sarah`, customer `103`) wakes up to a torn rainfly on the $349.99
> Basecamp 4P Tent she ordered earlier this year (order `1003` in the
> seed data — verify with `Get-Content data/contoso-crm/orders.csv`).
> She opens Contoso Outdoors and clicks **"Refund this order"**.
> Instantly:
>
> - Three agents wake up **in parallel** and start digging:
>   *is she a serial returner?*, *does her reason match the policy?*,
>   *is she a long-tenured high-tier customer or a brand-new account?*
> - They finish, an aggregator combines their scores into a single
>   recommendation, and the workflow **stops** — waiting for a human
>   at Contoso Operations to click **Approve / Reject / Re-investigate**.
> - Minutes (or hours, or days) later, the ops user clicks **Approve**.
>   The workflow wakes up exactly where it left off and the refund is
>   recorded.
>
> That **"STOP and wait for a human"** is the whole point of Lab 3.
> Labs 1 and 2 never paused — every reply came back in seconds. Here
> you exercise a system that's perfectly happy to wait days for a
> human, then resume the moment the click arrives.

> **What this lab is NOT.** Labs 1 and 2 had you copy-paste files into
> place because the components didn't exist yet. Lab 3 is different —
> the [`fraud-workflow`](../../../src/fraud-workflow/) service is **already
> in `src/`** and wired into the AppHost. Your job is to **read the
> code, run it, watch it pause, click Approve, and then deliberately
> break it** to feel the durability gap that motivates the
> [Full Azure Track](../full-azure/lab-3.md).

> **Rusty on async workflows? Read this first.** Three things will keep
> you oriented:
>
> 1. **There's no chat box this time.** The trigger is the customer
>    asking the agent for a refund — the agent files a return ticket
>    via the CRM API, and the CRM API then fires-and-forgets a
>    `POST /api/v1/refunds` to `fraud-workflow`
>    (see [Step 4](#step-4--trigger-the-customer-side-refund-flow)).
>    The whole risk-review flow runs in the background.
> 2. **A "workflow" in Microsoft Agent Framework is just a graph of
>    typed function calls.** Each node (an *executor*) takes a typed
>    input and emits a typed output. `AddEdge(a, b)` means *"after `a`
>    finishes, send to `b`"*. `AddFanInBarrierEdge([a, b, c], d)` means
>    *"wait for all three, then run `d` once"*. That's the whole DSL.
> 3. **The pause is just an `await`.** The framework offers a built-in
>    primitive (`ctx.WaitForExternalEventAsync<T>(...)`) that blocks an
>    executor until an outside event arrives. To keep the dependency
>    surface small, the [`HumanGateExecutor`](../../../src/fraud-workflow/Workflows/HumanGateExecutor.cs)
>    in this lab uses a thin
>    [`IApprovalGate`](../../../src/fraud-workflow/Services/IApprovalGate.cs)
>    abstraction backed by a `TaskCompletionSource` — same shape,
>    simpler plumbing. (On the Local Track that pause can't survive a
>    process restart, and [Step 6](#step-6--feel-the-durability-gap)
>    makes you stare at exactly that gap.)

## What you'll learn

This lab introduces a fundamentally different agent topology than Lab 2:

- **Lab 2** is **synchronous** — a user sends a chat message, agents run, a response comes back in seconds.
- **Lab 3** is **ambient** — *no human starts the work*. Events arrive continuously (return requests, large refund claims, suspicious order patterns), agents investigate in parallel, and the workflow **pauses** waiting for a human to approve or reject the recommended action. Minutes, hours, or days may pass between the analysis and the human decision.

This is the pattern behind real-world systems like fraud detection, IT incident triage, loan underwriting, and content moderation. It exercises four primitives that simple "user asks, agent answers" loops never need:

1. **Fan-out / fan-in** — one alert dispatches to N specialist agents in parallel; an aggregator combines their findings.
2. **Human-in-the-loop with timeout** — the workflow blocks on an external event (analyst decision) racing a timer (auto-escalate after 72h).
3. **Durable replay** — when the worker process dies after step 5 of 8, a new worker reads the event log and resumes at step 6 without re-running steps 1-5. *On the Local Track this is intentionally **not** wired up — the in-memory queue is wiped on restart so you can see the durability gap; the [Full Azure Track](../full-azure/lab-3.md) puts Durable Task Scheduler in front to close it.*
4. **Stateful agent conversations across the pause** — when the analyst rejects the recommendation and asks for re-analysis, the agents pick up with full context.

The system you'll explore is the **Refund Risk Workflow** for Contoso Outdoors:

- **Trigger:** a customer submits a refund request over a configurable threshold (default $200).
- **Investigation:** three specialist agents run in parallel — [`OrderHistoryAgent`](../../../src/fraud-workflow/Agents/OrderHistoryAgent.cs) (is this customer a serial returner?), [`ReturnConditionAgent`](../../../src/fraud-workflow/Agents/ReturnConditionAgent.cs) (does the description match the product/policy?), [`LoyaltyContextAgent`](../../../src/fraud-workflow/Agents/LoyaltyContextAgent.cs) (Bronze customer with first-ever return vs. Platinum with 50 prior orders?).
- **Aggregation:** a non-LLM aggregator ([`RiskAggregator`](../../../src/fraud-workflow/Services/RiskAggregator.cs)) combines risk scores into a single `RefundRiskAssessment` (`approve` / `manual_review` / `escalate`).
- **Human gate:** if the assessment is anything other than `approve`, the workflow pauses and adds a card to the **Operations review queue**. An ops user opens the Blazor [`/operations`](../../../src/blazor-ui/Pages/Operations.razor) page, reads the agents' findings, and clicks **Approve**, **Reject**, or **Reinvestigate**.
- **Action:** the workflow records a [`FinalAction`](../../../src/fraud-workflow/Models/FinalAction.cs) carrying the operator's decision (or `Timeout` if the 72-hour SLA elapses first).

By the end you'll have *exercised* a working ambient agent — fan-out to three specialists, aggregator, paused human gate, action — and you'll see exactly which durability gap (`Ctrl+C` between investigation and approval drops the queue on the Local Track) motivates the [Full Azure Track](../full-azure/lab-3.md).

### Microsoft Agent Framework primer (Workflows)

Labs 1 and 2 used `AIAgent` and `RunAsync` — synchronous calls into a model. This lab adds the **Workflows** side of the framework, which lets you wire agents into a graph that the runtime executes for you.

| Concept | Type | What it does |
|---------|------|--------------|
| **Executor** | `Microsoft.Agents.AI.Workflows.Executor<TIn, TOut>` | A single node in the graph — runs in response to a typed input message and emits a typed output message. The repo wraps each specialist agent in one of these in [`AgentExecutors.cs`](../../../src/fraud-workflow/Workflows/AgentExecutors.cs); the aggregator and gate live in [`AggregatorExecutor.cs`](../../../src/fraud-workflow/Workflows/AggregatorExecutor.cs) and [`HumanGateExecutor.cs`](../../../src/fraud-workflow/Workflows/HumanGateExecutor.cs). |
| **WorkflowBuilder** | `Microsoft.Agents.AI.Workflows.WorkflowBuilder` | Fluent DSL for declaring edges between executors. `AddEdge(a, b)` = "after `a` emits, send to `b`". `AddFanOutEdge(src, [a, b, c])` = "broadcast `src`'s output to all three". `AddFanInBarrierEdge([a, b, c], d)` = "wait for all three, then run `d` once". The graph for this lab is built in [`RefundRiskWorkflow.cs`](../../../src/fraud-workflow/Workflows/RefundRiskWorkflow.cs). |
| **WorkflowContext** | `Microsoft.Agents.AI.Workflows.IWorkflowContext` | The runtime handle inside an executor. Lets you `SendMessageAsync(...)` to downstream executors, request external events, or read the workflow's state. Used by [`RouterExecutor`](../../../src/fraud-workflow/Workflows/RouterExecutor.cs) to broadcast and stash the original alert. |
| **External events** | `ctx.WaitForExternalEventAsync<T>(eventName)` | The framework's built-in pause primitive. The runtime persists state and stops scheduling. When the external event is raised from outside, the workflow resumes from exactly that line. (This lab uses a thinner `IApprovalGate` + `TaskCompletionSource` instead, to keep the dependency surface small — same idea, less plumbing.) |
| **Persistence** | `Microsoft.Agents.AI.Workflows.Checkpointing.ICheckpointStore` | Where workflow state is written between steps. The Local Track doesn't wire this up at all (the in-memory `TaskCompletionSource` in [`InMemoryApprovalGate`](../../../src/fraud-workflow/Services/InMemoryApprovalGate.cs) is wiped on restart) — see the [Full Azure Track](../full-azure/lab-3.md) for the production-grade durable backend. |

Three rules that will save you debugging time:

1. **Executors must be deterministic given their inputs.** The runtime *replays* executor calls when it resumes from a checkpoint. Side effects (DB writes, MCP tool calls) belong inside agent executors, but anything in the workflow plumbing should be pure.
2. **Pauses are awaits, not callbacks.** `var decision = await ctx.WaitForExternalEventAsync<ApprovalDecision>("ApprovalDecision");` reads exactly like a normal `await`. The runtime is what makes the gap between "send" and "resume" survive process restarts.
3. **One workflow instance per business event.** Each refund alert produces a fresh workflow run with its own ID. The Operations UI lists in-flight runs by that ID; durable storage keys checkpoints by it.

## Prerequisites

- [Lab 1](lab-1.md) and [Lab 2](lab-2.md) complete.
- A second browser profile or incognito window to play the **Operations** role separately from the **Customer** role.

## The architecture you're exploring

```text
   Customer asks the agent      ┌─────────────────┐
   "refund my order 1003"       │  crm-agent      │  create_support_ticket
       ─────────────────────►   │  (5004)         │  category=return, order_id=1003
                                └────────┬────────┘
                                         │ MCP
                                         ▼
                                ┌─────────────────┐
                                │  crm-mcp (5002) │
                                └────────┬────────┘
                                         │ HTTP
                                         ▼
                                ┌─────────────────┐  ticket persists synchronously
                                │  crm-api (5001) │  POST /tickets returns 200
                                │                 │  fire-and-forget background task
                                │                 │   ─► POST /api/v1/refunds
                                └────────┬────────┘
                                         │
                                         ▼
                                ┌─────────────────────┐
                                │  fraud-workflow     │  ← Microsoft Agent Framework
                                │  service (5010)     │     workflow primitives
                                └────────┬────────────┘
                                     │ RouterExecutor (fan-out)
              ┌──────────────────────┼──────────────────────┐
              ▼                      ▼                      ▼
       ┌────────────┐        ┌─────────────────┐    ┌──────────────────┐
       │ order-hist │        │ return-cond     │    │ loyalty-context  │
       │ executor   │        │ executor        │    │ executor         │
       │ (LLM+MCP)  │        │ (LLM+MCP+RAG)   │    │ (LLM+MCP)        │
       └─────┬──────┘        └────────┬────────┘    └─────────┬────────┘
             │                        │                       │
             └────────────────────────┴───────────────────────┘
                                      ▼ AddFanInBarrierEdge
                            ┌──────────────────────┐
                            │ AggregatorExecutor   │  pure C#, no LLM
                            │ → RefundRiskAssessment│  (RiskAggregator)
                            └────────┬─────────────┘
                                     │ if RecommendedAction != "approve"
                                     ▼
                            ┌──────────────────────────┐
                            │  HumanGateExecutor       │  PAUSE
                            │  WaitForDecisionAsync()  │  TaskCompletionSource
                            └────────┬─────────────────┘  (in-memory only)
                                     │
                          ┌──────────┴──────────────┐
                          ▼                         ▼
              ┌─────────────────────┐     ┌────────────────────┐
              │ Operator clicks     │     │ Timer fires (72h)  │
              │ Approve / Reject    │     │ → FinalAction.     │
              │ / Reinvestigate     │     │   Timeout          │
              └──────────┬──────────┘     └────────────────────┘
                         ▼
                ┌────────────────────┐
                │ FinalAction emitted│
                │ → workflow output  │
                └────────────────────┘
```

The [`fraud-workflow`](../../../src/fraud-workflow/) service follows the same component-independence rule as every other service in this repo: zero `<ProjectReference>` to any other project under `src/` — only NuGet package references (Microsoft Agent Framework, MCP client, Aspire workload). The fitness function in [`ComponentIndependenceTests`](../../../src-tests/Contoso.AppHost.Tests/ComponentIndependenceTests.cs) guards this for every component including this one.

> **A map of what's where.** Each piece in the architecture diagram above
> maps to one of the files below. The whole service is small enough to
> read end-to-end in 20 minutes.
>
> | Diagram piece | File(s) |
> |---|---|
> | Inbound HTTP shape | [`Endpoints/RefundEndpoint.cs`](../../../src/fraud-workflow/Endpoints/RefundEndpoint.cs) |
> | Inbound BFF proxy | [`src/bff-api/Endpoints/OperationsEndpoints.cs`](../../../src/bff-api/Endpoints/OperationsEndpoints.cs) + [`Services/FraudWorkflowClient.cs`](../../../src/bff-api/Services/FraudWorkflowClient.cs) |
> | RouterExecutor (fan-out) | [`Workflows/RouterExecutor.cs`](../../../src/fraud-workflow/Workflows/RouterExecutor.cs) |
> | 3 specialist agents | [`Agents/OrderHistoryAgent.cs`](../../../src/fraud-workflow/Agents/OrderHistoryAgent.cs), [`ReturnConditionAgent.cs`](../../../src/fraud-workflow/Agents/ReturnConditionAgent.cs), [`LoyaltyContextAgent.cs`](../../../src/fraud-workflow/Agents/LoyaltyContextAgent.cs) |
> | Executors that wrap them | [`Workflows/AgentExecutors.cs`](../../../src/fraud-workflow/Workflows/AgentExecutors.cs) |
> | Aggregator (fan-in) | [`Workflows/AggregatorExecutor.cs`](../../../src/fraud-workflow/Workflows/AggregatorExecutor.cs) + pure-C# [`Services/RiskAggregator.cs`](../../../src/fraud-workflow/Services/RiskAggregator.cs) |
> | Human gate (pause) | [`Workflows/HumanGateExecutor.cs`](../../../src/fraud-workflow/Workflows/HumanGateExecutor.cs) + [`Services/InMemoryApprovalGate.cs`](../../../src/fraud-workflow/Services/InMemoryApprovalGate.cs) |
> | Workflow assembly | [`Workflows/RefundRiskWorkflow.cs`](../../../src/fraud-workflow/Workflows/RefundRiskWorkflow.cs) |
> | Background runner | [`Workflows/FraudWorkflowRunner.cs`](../../../src/fraud-workflow/Workflows/FraudWorkflowRunner.cs) |
> | Operator UI | [`src/blazor-ui/Pages/Operations.razor`](../../../src/blazor-ui/Pages/Operations.razor) |
> | Pure-function tests | [`src-tests/Contoso.FraudWorkflow.Tests/`](../../../src-tests/Contoso.FraudWorkflow.Tests/) |

## Step 1 — Read the workflow assembly

Open [`Workflows/RefundRiskWorkflow.cs`](../../../src/fraud-workflow/Workflows/RefundRiskWorkflow.cs). Notice three things:

1. **The graph is declared in 5 lines.** `AddFanOutEdge(router, [a, b, c])` followed by `AddFanInBarrierEdge([a, b, c], aggregator)` then `AddEdge(aggregator, gate)`. The runtime takes care of scheduling, parallelism, and the barrier — you describe the *shape*, not the *driver*.
2. **`WithOutputFrom(gate)`** marks `HumanGateExecutor`'s output as the workflow's terminal value. That `FinalAction` is what `FraudWorkflowRunner` records as the run's outcome.
3. **No `ICheckpointStore` is registered.** That's the deliberate gap — see [Step 6](#step-6--feel-the-durability-gap).

Then open [`Workflows/RouterExecutor.cs`](../../../src/fraud-workflow/Workflows/RouterExecutor.cs). It does two things in `HandleAsync`:

- Stashes the original alert in shared workflow state under `("refund", "alert")` so the aggregator can rebuild the assessment from the three findings.
- Calls `context.SendMessageAsync(message, ...)` *without* a target id — the fan-out edge declared in `WorkflowBuilder` causes the runtime to broadcast the message to all three connected executors.

This is *the* idiom for fan-out in Microsoft Agent Framework: declare edges, send untargeted, let the runtime fan out.

## Step 2 — Read one specialist agent

Pick any of the three — they all follow the same shape. [`OrderHistoryAgent`](../../../src/fraud-workflow/Agents/OrderHistoryAgent.cs) is the simplest:

1. **Lazy MCP tool discovery** — the per-call `ListToolsAsync` returns the cached MCP client's current tool list. (Same pattern as [`crm-agent`](../../../src/crm-agent/) — that's intentional. We do not share a NuGet library; the code is duplicated by design — see the architectural edict in [`copilot-instructions.md`](../../../.github/copilot-instructions.md).)
2. **Per-call agent build** — `_factory.CreateAgent(...)` wraps an `AIProjectClient.AsAIAgent(...)` call with the system prompt and the tool list. This re-creates the agent every call so the tool surface stays current.
3. **Strict JSON output** — the system prompt locks the model into `{ "riskScore": 0.0-1.0, "findings": "...", "evidence": [...] }`. [`AgentFinding.Parse`](../../../src/fraud-workflow/Models/AgentFinding.cs) is forgiving: malformed JSON falls back to a `0.5` score (manual review) so a bad model response never derails the workflow.

> **Why three agents instead of one big prompt?** Two reasons. First, **parallelism** — three short prompts run faster than one giant one. Second, **observability** — when an operator looks at a paused review they see *three* separate explanations, one per dimension; that's much easier to reason about than a single 600-token blob.

## Step 3 — Read the human gate and aggregator

Open [`Services/RiskAggregator.cs`](../../../src/fraud-workflow/Services/RiskAggregator.cs). Two facts to internalize:

- **No LLM is involved.** The recommendation policy is `overall = max(scores)` plus two thresholds (`<0.34 → approve`, `<0.67 → manual_review`, `≥0.67 → escalate`). This is deliberately deterministic, testable, and cheap — see [`RiskAggregatorTests`](../../../src-tests/Contoso.FraudWorkflow.Tests/RiskAggregatorTests.cs) for the pinned thresholds.
- **The aggregator owns the policy.** If you want to change "any one risky finding escalates" to "median > 0.5 escalates", you change *one* file, with *no* prompt engineering and *no* model calls. That separation is why the aggregator isn't an LLM.

Then open [`Workflows/HumanGateExecutor.cs`](../../../src/fraud-workflow/Workflows/HumanGateExecutor.cs):

- If `RecommendedAction == "approve"` it short-circuits and emits `FinalAction.AutoApprove` immediately — no human involvement, no pause.
- Otherwise it `await`s [`IApprovalGate.WaitForDecisionAsync`](../../../src/fraud-workflow/Services/IApprovalGate.cs). On the Local Track that gate is [`InMemoryApprovalGate`](../../../src/fraud-workflow/Services/InMemoryApprovalGate.cs) — `ConcurrentDictionary<alertId, TaskCompletionSource<ApprovalDecision>>`. The dictionary entry is removed when *either* the operator decides *or* the 72-hour timeout fires (linked-token-source pattern). Notice the `RunContinuationsAsynchronously` flag — without it, the operator's HTTP thread would run the entire downstream `AggregatorExecutor → workflow output` graph inline before responding `204`.

## Step 4 — Trigger the customer-side refund flow

Run the AppHost from the repo root:

```powershell
dotnet run --project src/AppHost
```

The Aspire dashboard opens. You should see `fraud-workflow` listed with its `http` endpoint at `http://localhost:5010`. Wait until the project is **Healthy**.

There are three ways to land an alert in the queue. Option A is the **real customer flow** and the one to use first; the others are diagnostic conveniences.

### Option A — Ask the agent (the actual customer flow)

1. Open Blazor at `http://localhost:5008` and sign in as any customer (UPN + password from `local-dev-credentials.txt`).
2. From the chat assistant, say: **"I want a refund for my order 1003 — the tent rainfly arrived torn."**
3. The agent calls the [`create_support_ticket`](../../../src/crm-mcp/Tools/SupportTicketTools.cs) MCP tool with `category="return"` and `order_id="1003"`. The CRM API persists the ticket and — because category is `return` and the order exists — fires-and-forgets a `POST /api/v1/refunds` to `fraud-workflow` from a background task ([`SupportTicketEndpoints.TriggerRefundAlertAsync`](../../../src/crm-api/Endpoints/SupportTicketEndpoints.cs)). The customer immediately sees a confirmation message in chat.
4. Sign in as a second user (different browser profile) and open **Operations**. Within ~5–10 seconds (3 agents × MCP + model calls + the 5s poll) a card appears.
5. As the operator, click **Approve** (or **Reject**). `fraud-workflow` calls back to CRM API at `POST /api/v1/internal/tickets/{ticketId}/refund-decision` (see [`Services/CrmApiClient.cs`](../../../src/fraud-workflow/Services/CrmApiClient.cs)). The customer's `/tickets` page now shows the ticket as **resolved** (or still **open** with a "needs more info" comment) — the loop is closed end-to-end.

This path is what makes the workflow *load-bearing* — it isn't a button on a Ops page, it's the side-effect of every above-threshold return ticket, and the operator's click is what the customer ultimately sees on their account page.

### Option B — Use the synthetic alert button on Operations

1. Open Blazor at `http://localhost:5008` and sign in.
2. Click your avatar → **Operations**.
3. Click **Submit synthetic alert (demo)**. The page calls `POST /api/v1/refunds` with a $425.50 demo alert. Use this when you want to exercise the workflow without going through chat — useful for debugging just the aggregator/human-gate path.

### Option C — Send a real-shaped POST from the Aspire dashboard

In the Aspire dashboard, expand `fraud-workflow` → **Endpoints** and use the built-in HTTP tab to send:

```http
POST http://localhost:5010/api/v1/refunds
Content-Type: application/json

{ "customerId": "103", "orderId": "1003", "amount": 349.99, "reason": "Tent rainfly torn on arrival" }
```

You should get a `202 Accepted` with an `alertId` and a `Location: /api/v1/operations/{alertId}` header. The workflow is now running in the background.

> **Where the threshold matters.** `Refund:Threshold` (default $200) is enforced inside `fraud-workflow` itself. The CRM API always posts the alert with the order's total amount; `fraud-workflow` then either auto-approves below-threshold returns (returning `200 OK { status = "below_threshold" }`) or starts the human-gate workflow above the threshold (returning `202 Accepted { alertId }`). A customer with a $50 return won't pull anyone away from their lunch.
>
> **Closing the loop.** Below-threshold responses cause CRM API to immediately resolve the customer's ticket with an audit comment ("auto-approved (under threshold)"). Above-threshold responses do nothing yet — the customer's ticket stays **open** until `fraud-workflow` calls back with the operator's decision. Auto-approve, operator-approve, and operator-reject all flow through the same `POST /api/v1/internal/tickets/{id}/refund-decision` callback ([`SupportTicketEndpoints.cs`](../../../src/crm-api/Endpoints/SupportTicketEndpoints.cs)).
> - `approve` / `below_threshold` → ticket → **resolved** + audit comment
> - `reject` / `timeout` → ticket stays **open** + audit comment ("Need more info: …") so the customer can follow up
> The customer reads these on their `/tickets` page; the agent surfaces the latest comment when asked "what happened with my refund?".

## Step 5 — Approve from the Operations dashboard

In your second browser profile (sign in as a different test user from `local-dev-credentials.txt`), navigate to `http://localhost:5008/operations`. Within 5 seconds (the page polls `GET /api/v1/operations/pending` every 5 seconds; see [`Pages/Operations.razor`](../../../src/blazor-ui/Pages/Operations.razor)) the alert card appears. Each card shows:

- The order id, customer id, amount, and the model's `RecommendedAction` (`manual_review` or `escalate`).
- Three expandable panels — one per specialist agent — with that agent's `riskScore`, `findings`, and `evidence`.

Click **Approve refund**. The card disappears, a snackbar confirms the decision, and `FraudWorkflowRunner` records a [`FinalAction.FromOperator(...)`](../../../src/fraud-workflow/Models/FinalAction.cs) outcome.

To verify the recorded outcome from the dashboard, open the same `/operations` page in a fresh tab and watch the snackbar text — the alert id printed there is what you'd use to query the BFF (operations endpoints are operator-only and require a Bearer token, so a hand-rolled `curl` from a separate shell would also need a JWT).

> **A note on auth.** On the Local Track the `/api/v1/operations/*` routes are gated by `RequireAuthorization()` — i.e. *any* signed-in user works. The Full Azure Track tightens this to `RequireRole("Operations")` so a customer cannot approve their own refunds (see [Step 5 of the Full Azure Lab 3](../full-azure/lab-3.md#step-5--add-an-operations-entra-app-role-and-gate-the-bff-endpoints)).

> **Why polling and not SignalR?** The Blazor UI in this repo uses Server-Sent Events for chat streaming and polling for everything else. Adding SignalR would mean a new central package + a new server-side hub shape + a CORS rule + a separate auth path on the WebSocket upgrade. The whole point of the human gate is that operators are *not* clicking once a millisecond — a 5-second poll is correct.

## Step 6 — Feel the durability gap

The point of an ambient workflow is that **a process restart in the middle is non-disastrous**. Try it:

1. Submit another refund request (Step 4 again).
2. Confirm it lands in the operations queue (Step 5 — but **don't** approve yet).
3. *Before* approving, hit `Ctrl+C` on the AppHost terminal.
4. Restart with `dotnet run --project src/AppHost`.
5. Open `/operations` again.

The pending review is **gone**. `InMemoryApprovalGate` only lives in process memory; the linked token source that was racing the 72-hour timer was never persisted. The customer's refund is permanently stuck waiting for a decision that nobody will ever be asked to make.

This is by design. The lab makes the gap visible so you understand exactly why the [Full Azure Track](../full-azure/lab-3.md) exists — there, the human gate is backed by Azure Durable Task Scheduler, the workflow's state is checkpointed on every transition, and a fresh pod resumes from exactly where the dead one left off.

> **Optional Local Track exercise:** persist `_pending` to a JSON file on disk and reload on startup. This gives you durability across restarts but not across multi-worker deployments — exactly the DIY trap that the Full Azure Track avoids by using Azure Durable Task Scheduler.

## Verification checklist

- [ ] `fraud-workflow` is green in the Aspire dashboard
- [ ] `dotnet test src-tests/Contoso.FraudWorkflow.Tests` passes (20+ tests)
- [ ] Submitting a refund returns `202 Accepted` with an `alertId`
- [ ] A review card appears on `/operations` within ~10 seconds
- [ ] Each card shows three agent findings with risk scores
- [ ] Clicking **Approve refund** clears the card and `GET /api/v1/operations/{alertId}` returns a `FinalAction` with `Source = "operator"`
- [ ] After clicking **Approve**, the customer's `/tickets` page shows the originating ticket as **resolved** with an audit comment containing `operator/approve`
- [ ] Clicking **Reject** instead leaves the ticket **open** with the rejection reason in the audit comment (so the customer can follow up)
- [ ] After `Ctrl+C` + restart, the pending card is gone (the demo of Step 6)
- [ ] `ComponentIndependenceTests` is still green (`fraud-workflow` adds zero project-to-project references)

> **Glossary** (skim if anything is unfamiliar):
>
> - **Workflow** = a graph of executors that the runtime drives for you. You declare nodes and edges; the runtime decides what runs when and in what order.
> - **Executor** = one node in the workflow graph. Takes a typed input message, does work, emits a typed output message.
> - **Fan-out** = one input message routed to N executors at the same time. The router executor is the fan-out point.
> - **Fan-in barrier** = wait for all N executors to emit, then run a single combiner once with the combined results. The aggregator is the fan-in point.
> - **Human-in-the-loop (HITL)** = a workflow node that pauses until an outside actor (a human, an external service, a timer) raises a named event. Implemented as `await ctx.WaitForExternalEventAsync<T>("...")` — reads like any other `await`. (This lab uses a simpler `IApprovalGate` + `TaskCompletionSource` for the same effect.)
> - **Durable** = the workflow's state survives a process restart. The runtime checkpoints every transition; a fresh worker process can read the log and resume from exactly where the dead one left off. **Not implemented on the Local Track** — that's Step 6's lesson.
> - **Checkpoint** = a snapshot of the workflow's state written between executor invocations. Stored in `Microsoft.Agents.AI.Workflows.Checkpointing.ICheckpointStore` (e.g. `JsonCheckpointStore`, `FileSystemJsonCheckpointStore`).
> - **Aggregator** = the deterministic, no-LLM step that takes the three agents' findings and decides `approve` / `manual_review` / `escalate`. Pulled out of the LLM loop on purpose so the decision is explainable, testable, and cheap.
> - **Ambient** = the agent topology pattern this lab teaches. No human kicks off the work; events arrive from the outside and flow through the workflow on their own. Contrast with Lab 2's **interactive** pattern, where every run starts from a chat box.

## What's next

Local Track Lab 3 complete. To see what production durability looks like, switch over to the [Full Azure Track Lab 3](../full-azure/lab-3.md) — same workflow shape, same Blazor UI, but the human gate now sits behind Azure Durable Task Scheduler so it survives pod restarts.
