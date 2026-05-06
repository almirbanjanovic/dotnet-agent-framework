# Lab 3 — Human-in-the-Loop Workflows (Local Track)

> **Track:** Local — Foundry only, everything else runs on your laptop.
> Looking for the Full Azure Track instead? See [`../full-azure/lab-3.md`](../full-azure/lab-3.md).

## What you'll learn

This lab introduces a fundamentally different agent topology than Lab 2:

- **Lab 2** is **synchronous** — a user sends a chat message, agents run, a response comes back in seconds.
- **Lab 3** is **ambient and durable** — *no human starts the work*. Events arrive continuously (return requests, large refund claims, suspicious order patterns), agents investigate in parallel, and the workflow **pauses indefinitely** waiting for a human to approve or reject the recommended action. Hours, days, or weeks may pass between the analysis and the human decision. Processes will restart in the meantime.

This is the pattern behind real-world systems like fraud detection, IT incident triage, loan underwriting, and content moderation. It exercises four primitives that simple "user asks, agent answers" loops never need:

1. **Fan-out / fan-in** — one alert dispatches to N specialist agents in parallel; an aggregator combines their findings.
2. **Human-in-the-loop with timeout** — the workflow blocks on an external event (analyst decision) racing a timer (auto-escalate after 72h).
3. **Durable replay** — when the worker process dies after step 5 of 8, a new worker reads the event log and resumes at step 6 without re-running steps 1-5.
4. **Stateful agent conversations across the pause** — when the analyst rejects the recommendation and asks for re-analysis, the agents pick up with full context.

You'll build a **Refund Risk Workflow** for Contoso Outdoors:

- **Trigger:** a customer submits a refund request over a configurable threshold (default $200).
- **Investigation:** three specialist agents run in parallel — `OrderHistoryAgent` (is this customer a serial returner?), `ReturnConditionAgent` (does the description match the product/policy?), `LoyaltyContextAgent` (Bronze customer with first-ever return vs. Platinum with 50 prior orders?).
- **Aggregation:** a non-LLM aggregator combines risk scores into a single `RefundRiskAssessment` (`approve` / `manual_review` / `escalate`).
- **Human gate:** if the assessment is anything other than `approve`, the workflow pauses and adds a card to the **Operations review queue**. An ops user opens the Blazor `/operations` page, reads the agents' findings, and clicks **Approve**, **Reject**, or **Re-investigate with feedback**.
- **Action:** the workflow either calls `crm-mcp.create_support_ticket` (for refund processing) or sends the rejection back to the customer.

By the end you'll have a working ambient agent that survives `Ctrl+C` mid-investigation and resumes correctly — and you'll see exactly which durability gap motivates the [Full Azure Track](../full-azure/lab-3.md).

### Microsoft Agent Framework primer (Workflows)

Labs 1 and 2 used `AIAgent` and `RunAsync` — synchronous calls into a model. This lab adds the **Workflows** side of the framework, which lets you wire agents into a graph that the runtime executes for you.

| Concept | Type | What it does |
|---------|------|--------------|
| **Executor** | `Microsoft.Agents.Workflows.Executor<TIn, TOut>` | A single node in the graph — runs in response to a typed input message and emits a typed output message. Wrap each specialist agent in one of these (`AgentExecutor`, `AggregatorExecutor`, `HumanGateExecutor` below). |
| **WorkflowBuilder** | `Microsoft.Agents.Workflows.WorkflowBuilder` | Fluent DSL for declaring edges between executors. `AddEdge(a, b)` = "after `a` emits, send to `b`". `AddFanInEdges([a, b, c], d)` = "wait for all three, then run `d` once with the combined inputs". |
| **WorkflowContext** | `Microsoft.Agents.Workflows.WorkflowContext` | The runtime handle inside an executor. Lets you `SendMessageAsync(...)`, `RequestExternalEventAsync(...)`, or read the workflow's state. |
| **External events** | `ctx.WaitForExternalEventAsync<T>(eventName)` | The point where a workflow **pauses**. The runtime persists state and stops scheduling. When `RaiseEventAsync(eventName, payload)` is called from outside (e.g. an HTTP controller handling an approval click), the workflow resumes from exactly that line. |
| **Persistence** | `IWorkflowCheckpointStore` | Where workflow state is written between steps. The Local Track uses an in-memory store (`Ctrl+C` ends the workflow) — see the [Full Azure Track](../full-azure/lab-3.md) for the production-grade durable backend. |

Three rules that will save you debugging time:

1. **Executors must be deterministic given their inputs.** The runtime *replays* executor calls when it resumes from a checkpoint. Side effects (DB writes, MCP tool calls) belong inside agent executors, but anything in the workflow plumbing should be pure.
2. **Pauses are awaits, not callbacks.** `var decision = await ctx.WaitForExternalEventAsync<ApprovalDecision>("ApprovalDecision");` reads exactly like a normal `await`. The runtime is what makes the gap between "send" and "resume" survive process restarts.
3. **One workflow instance per business event.** Each refund alert produces a fresh workflow run with its own ID. The Operations UI lists in-flight runs by that ID; durable storage keys checkpoints by it.

## Prerequisites

- [Lab 1](lab-1.md) and [Lab 2](lab-2.md) complete.
- A second browser profile or incognito window to play the **Operations** role separately from the **Customer** role.

## The architecture you'll build

```text
   Customer submits         ┌─────────────────┐
   refund > $200            │  bff-api        │  POST /api/v1/refunds
       ─────────────────►   │  /refunds       │  enqueues alert
                            └────────┬────────┘
                                     │
                                     ▼
                            ┌─────────────────────┐
                            │  fraud-workflow     │  ← Microsoft Agent Framework
                            │  service (5010)     │     workflow primitives
                            └────────┬────────────┘
                                     │ fan-out
              ┌──────────────────────┼──────────────────────┐
              ▼                      ▼                      ▼
       ┌────────────┐        ┌─────────────────┐    ┌──────────────────┐
       │ order-hist │        │ return-cond     │    │ loyalty-context  │
       │ executor   │        │ executor        │    │ executor         │
       │ (LLM+MCP)  │        │ (LLM+MCP+RAG)   │    │ (LLM+MCP)        │
       └─────┬──────┘        └────────┬────────┘    └─────────┬────────┘
             │                        │                       │
             └────────────────────────┴───────────────────────┘
                                      ▼
                            ┌──────────────────────┐
                            │ risk aggregator      │  pure C#, no LLM
                            │ → RefundRiskAssessment│
                            └────────┬─────────────┘
                                     │ if risk != "approve"
                                     ▼
                            ┌──────────────────────────┐
                            │  PAUSE                   │  TaskCompletionSource
                            │  WaitForApprovalAsync()  │  (in-memory only)
                            └────────┬─────────────────┘
                                     │
                          ┌──────────┴──────────────┐
                          ▼                         ▼
              ┌─────────────────────┐     ┌────────────────────┐
              │ Operator clicks     │     │ Timer fires (72h)  │
              │ Approve / Reject    │     │ → auto-escalate    │
              │ / Re-investigate    │     └────────────────────┘
              └──────────┬──────────┘
                         ▼
                ┌────────────────────┐
                │ Action: ticket OR  │
                │ rejection notice   │
                └────────────────────┘
```

The `fraud-workflow` service is a **new component** you'll add — it does not exist in `src/` yet. It follows the same component-independence rule as every other service: no `<ProjectReference>` to any other project under `src/` — only NuGet package references (Microsoft Agent Framework, MCP client, Aspire workload). The same fitness function ([`ComponentIndependenceTests`](../../../src-tests/Contoso.AppHost.Tests/ComponentIndependenceTests.cs)) that guards every other service will guard this one too.

## Step 1 — Scaffold the workflow service

```powershell
mkdir src/fraud-workflow
Push-Location src/fraud-workflow
dotnet new web -n Contoso.FraudWorkflow --no-restore
Move-Item Contoso.FraudWorkflow/* .
Remove-Item Contoso.FraudWorkflow
Pop-Location
```

Edit Contoso.FraudWorkflow.csproj — add the same package versions used elsewhere in the repo (check Directory.Packages.props for pinned versions):

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Contoso.FraudWorkflow</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Agents.AI" />
    <PackageReference Include="Microsoft.Agents.AI.OpenAI" />
    <PackageReference Include="Azure.AI.OpenAI" />
    <PackageReference Include="Azure.Identity" />
    <PackageReference Include="ModelContextProtocol" />
    <PackageReference Include="Microsoft.AspNetCore.SignalR" />
  </ItemGroup>
</Project>
```

Add it to the solution and to AppHost:

```powershell
dotnet sln add src/fraud-workflow/Contoso.FraudWorkflow.csproj
```

In Program.cs:

```csharp
var fraudWorkflow = AsLocal(builder.AddProject<Projects.Contoso_FraudWorkflow>("fraud-workflow"))
    .WithHttpEndpoint(port: 5010, name: "http")
    .WithReference(crmMcp)
    .WithReference(knowledgeMcp);
```

## Step 2 — Create the three specialist agents

In src/fraud-workflow/Agents/ create three files. Each is a thin wrapper around `Microsoft.Agents.AI.AIAgent` that loads MCP tools at startup, runs one analysis turn, and returns a structured result.

**OrderHistoryAgent.cs** — looks up the customer's prior orders via `crm-mcp.get_customer_orders` and reports patterns:

```csharp
public sealed class OrderHistoryAgent
{
    private readonly AIAgent _agent;

    public OrderHistoryAgent(AIAgent agent) => _agent = agent;

    public async Task<AgentFinding> AnalyzeAsync(RefundAlert alert, CancellationToken ct)
    {
        var prompt = $"""
            Customer {alert.CustomerId} requests refund for order {alert.OrderId} (amount: ${alert.Amount}).
            Reason given: "{alert.Reason}"

            Investigate this customer's order and return history. Report:
            - Total orders in last 12 months
            - Total returns in last 12 months
            - Return rate as percentage
            - Any flagged patterns (e.g., serial returner, recent burst of returns)

            Output JSON: { "riskScore": 0.0-1.0, "findings": "...", "evidence": ["...", "..."] }
            """;

        var response = await _agent.RunAsync(prompt, cancellationToken: ct);
        return AgentFinding.Parse(response.Text);
    }
}
```

The other two follow the same shape with different prompts and tool subsets:

- **ReturnConditionAgent** — uses `knowledge-mcp.search_knowledge_base` to fetch the relevant policy section and `crm-mcp.get_order_detail` to fetch the items; reports whether the customer's reason matches a covered scenario.
- **LoyaltyContextAgent** — uses `crm-mcp.get_customer_detail` for tier and account age; reports whether the customer's profile carries weight (long-tenure Platinum vs. brand-new account).

Wire each agent in `Program.cs` the same way `crm-agent` does — the
`AIProjectClient.AsAIAgent(...)` call lives in
[`src/crm-agent/Services/CrmAgentFactory.cs`](../../../src/crm-agent/Services/CrmAgentFactory.cs)
and the per-request MCP-tool-loading pattern lives in
[`src/crm-agent/Endpoints/ChatEndpoint.cs`](../../../src/crm-agent/Endpoints/ChatEndpoint.cs).

## Step 3 — Build the workflow with Microsoft Agent Framework `WorkflowBuilder`

Create Workflows/RefundRiskWorkflow.cs. The Microsoft Agent Framework provides `WorkflowBuilder`, `Executor`, and `WorkflowContext` types for fan-out/fan-in topologies. Use them to wire the executors:

```csharp
public static class RefundRiskWorkflow
{
    public static IWorkflow Build(
        OrderHistoryAgent history,
        ReturnConditionAgent condition,
        LoyaltyContextAgent loyalty,
        RiskAggregator aggregator,
        IApprovalGate approvalGate)
    {
        var router    = new AlertRouterExecutor();                // fans out alert → 3 executors
        var historyEx = new AgentExecutor("history",   history);
        var condEx    = new AgentExecutor("condition", condition);
        var loyaltyEx = new AgentExecutor("loyalty",   loyalty);
        var agg       = new AggregatorExecutor("agg",  aggregator);
        var gate      = new HumanGateExecutor("gate",  approvalGate);

        return new WorkflowBuilder(router)
            .AddEdge(router, historyEx)
            .AddEdge(router, condEx)
            .AddEdge(router, loyaltyEx)
            .AddFanInEdges(new[] { historyEx, condEx, loyaltyEx }, agg)
            .AddEdge(agg, gate)
            .Build();
    }
}
```

The `HumanGateExecutor` is where the workflow pauses. On the Local Track it asks an in-memory `IApprovalGate` service for the decision — see [Step 4](#step-4--implement-the-in-memory-approval-gate).

## Step 4 — Implement the in-memory approval gate

Create Services/InMemoryApprovalGate.cs:

```csharp
public sealed class InMemoryApprovalGate : IApprovalGate
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ApprovalDecision>> _pending = new();
    private readonly IHubContext<OperationsHub> _hub;

    public InMemoryApprovalGate(IHubContext<OperationsHub> hub) => _hub = hub;

    public Task<ApprovalDecision> WaitForDecisionAsync(
        string alertId, RefundRiskAssessment assessment, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<ApprovalDecision>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[alertId] = tcs;

        // Push the card to all connected operations dashboards.
        _hub.Clients.All.SendAsync("PendingReviewAdded", assessment, ct);

        ct.Register(() => tcs.TrySetCanceled(ct));
        return tcs.Task;
    }

    public bool TrySubmitDecision(string alertId, ApprovalDecision decision)
    {
        if (!_pending.TryRemove(alertId, out var tcs)) return false;
        tcs.TrySetResult(decision);
        return true;
    }
}
```

Expose two HTTP endpoints in Program.cs:

```csharp
app.MapPost("/api/v1/refunds", async (RefundAlert alert, IWorkflowRunner runner) =>
{
    var alertId = await runner.StartAsync(alert);
    return Results.Accepted($"/api/v1/refunds/{alertId}", new { alertId });
});

app.MapPost("/api/v1/operations/decisions", (DecisionRequest req, InMemoryApprovalGate gate) =>
    gate.TrySubmitDecision(req.AlertId, req.Decision)
        ? Results.NoContent()
        : Results.NotFound());
```

`IWorkflowRunner` is a thin host that runs the workflow on a background `Task` (`Task.Run`) so the HTTP request returns immediately while the workflow runs to its pause point.

## Step 5 — Add a SignalR hub for the operations dashboard

Create Hubs/OperationsHub.cs:

```csharp
public sealed class OperationsHub : Hub
{
    public Task JoinOperations() => Groups.AddToGroupAsync(Context.ConnectionId, "operations");
}
```

Map it in Program.cs:

```csharp
builder.Services.AddSignalR();
app.MapHub<OperationsHub>("/hubs/operations");
```

## Step 6 — Add the Blazor operations page

In src/blazor-ui/Pages/ create Operations.razor:

```razor
@page "/operations"
@inject HttpClient Http
@inject NavigationManager Nav
@implements IAsyncDisposable

<h2>Refund risk reviews</h2>

@if (_pending.Count == 0)
{
    <p><em>No reviews pending.</em></p>
}
else
{
    foreach (var card in _pending)
    {
        <div class="review-card">
            <h3>Alert @card.AlertId — Customer @card.CustomerId — $@card.Amount</h3>
            <p><strong>Recommendation:</strong> @card.RecommendedAction (risk: @card.OverallRiskScore)</p>
            <details><summary>Order history finding</summary><p>@card.HistoryFindings</p></details>
            <details><summary>Return condition finding</summary><p>@card.ConditionFindings</p></details>
            <details><summary>Loyalty context finding</summary><p>@card.LoyaltyFindings</p></details>
            <button @onclick="() => Decide(card.AlertId, ApprovalDecision.Approve)">Approve</button>
            <button @onclick="() => Decide(card.AlertId, ApprovalDecision.Reject)">Reject</button>
            <button @onclick="() => Decide(card.AlertId, ApprovalDecision.Reinvestigate)">Re-investigate</button>
        </div>
    }
}

@code {
    private readonly List<RefundRiskAssessment> _pending = new();
    private HubConnection? _hub;

    protected override async Task OnInitializedAsync()
    {
        _hub = new HubConnectionBuilder()
            .WithUrl(Nav.ToAbsoluteUri("/hubs/operations"))   // BFF proxies to fraud-workflow
            .WithAutomaticReconnect()
            .Build();

        _hub.On<RefundRiskAssessment>("PendingReviewAdded", a =>
        {
            _pending.Add(a);
            InvokeAsync(StateHasChanged);
        });

        await _hub.StartAsync();
        await _hub.SendAsync("JoinOperations");
    }

    private async Task Decide(string alertId, ApprovalDecision decision)
    {
        await Http.PostAsJsonAsync("/api/v1/operations/decisions",
            new { AlertId = alertId, Decision = decision });
        _pending.RemoveAll(p => p.AlertId == alertId);
    }

    public async ValueTask DisposeAsync()
    {
        if (_hub is not null) await _hub.DisposeAsync();
    }
}
```

Add a navigation entry to NavMenu.razor pointing at `/operations`.

## Step 7 — End-to-end test

Restart `dotnet run --project src/AppHost`. Confirm `fraud-workflow` is green in the Aspire dashboard.

Open two browser windows:

- **Window A** (incognito) — sign in to the Blazor UI at `http://localhost:5008` as a customer (e.g., `emma.wilson-local@<your-tenant-domain>`). All 8 UPNs + their current passwords live in **`local-dev-credentials.txt` at the repo root** — the file `setup-local` wrote in Lab 1. `cat local-dev-credentials.txt` (or open it in your editor) to grab the password for whichever user you want to sign in as. Pick **Emma Wilson** in the customer picker, open the chat, and ask:

  > I'd like a refund for order 1003 — the boots arrived damaged. The total was $285.

  In production a real customer-experience surface would have a **Refund this order** button on the order detail page that POSTs `{ customerId, orderId, amount, reason }` to `bff-api/api/v1/refunds`, and the BFF would forward to `fraud-workflow`. We haven't built that button in this lab — for now, the chat prompt above is the customer trigger and the `returns-agent` (built in Lab 2) is responsible for collecting the structured fields and calling `fraud-workflow` over HTTP.

  > **Lab gap (acknowledged):** wiring the BFF refund endpoint and the order-page button is left as an exercise. Until that's done you can simulate the customer-side POST from the **Aspire dashboard**: open `fraud-workflow` → **Endpoints** → use the built-in HTTP tab to send `POST /api/v1/refunds` with the body below. **Do not** make a habit of running production-shaped traffic through a CLI — it bypasses the very BFF/auth path the rest of the labs prove out.

  ```json
  { "customerId": "101", "orderId": "1003", "amount": 285.00, "reason": "Boots damaged on arrival" }
  ```

- **Window B** (separate browser profile) — sign in as a different test user (UPN + password again from **`local-dev-credentials.txt`** at the repo root — e.g., the `lisa` row, which is `lisa.torres-local@<your-tenant-domain>`) and navigate to `/operations`. In production this would be an account with the `Operations` app role; for the Local Track lab, any signed-in user can see the queue.

Within ~5 seconds, a review card should appear in Window B with the three agents' findings. Click **Approve** — the call returns `204` and the workflow runs the action (creates a support ticket via `crm-mcp.create_support_ticket`).

## Step 8 — Test durability (the demo that matters)

The point of an ambient workflow is that **a process restart in the middle is non-disastrous**. Try it:

1. Submit another refund request.
2. *Before* approving in Window B, hit `Ctrl+C` on the AppHost terminal.
3. Restart with `dotnet run --project src/AppHost`.
4. Open `/operations` again.

On the **Local Track**, the pending review is **lost** — `InMemoryApprovalGate` only lives in memory. This is by design. The lab makes the gap visible so you understand exactly why the [Full Azure Track](../full-azure/lab-3.md) exists.

> **Optional Local Track exercise:** persist `_pending` to a JSON file on disk and reload on startup. This gives you durability across restarts but not across multi-worker deployments — exactly the DIY trap that the Full Azure Track avoids by using Azure Durable Task Scheduler.

## Verification checklist

- [ ] `fraud-workflow` is green in the Aspire dashboard
- [ ] Submitting a refund returns `202 Accepted` with an `alertId`
- [ ] A review card appears on `/operations` within ~5 seconds
- [ ] Each card shows three agent findings with risk scores
- [ ] Approving creates a support ticket (verify by GET `http://localhost:5001/api/v1/customers/101/tickets`)
- [ ] Rejecting closes the alert with no ticket created
- [ ] `ComponentIndependenceTests` is still green after adding the new component

## What's next

Local Track Lab 3 complete. To see what production durability looks like, switch over to the [Full Azure Track Lab 3](../full-azure/lab-3.md) — same workflow shape, same Blazor UI, but the human gate now sits behind Azure Durable Task Scheduler so it survives pod restarts.
