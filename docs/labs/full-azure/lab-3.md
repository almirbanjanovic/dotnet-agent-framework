# Lab 3 — Human-in-the-Loop Workflows (Full Azure Track)

> **Track:** Full Azure — production-shaped: AKS pods, Azure Durable Task Scheduler (DTS), GitHub OIDC, Entra workload identity.
> Looking for the Local Track instead? See [`../local/lab-3.md`](../local/lab-3.md).

> **Picture this.** The Local Track had you simulate a refund, watch
> three agents fan out, click **Approve** in the operations dashboard,
> and then deliberately `Ctrl+C` the AppHost — at which point the
> pending review evaporated. That gap (in-process `TaskCompletionSource`
> dies with the worker) is **the** thing the Full Azure Track closes.
> Same workflow shape, same Blazor UI, same three specialist agents —
> but the human gate now sits behind Azure Durable Task Scheduler so it
> survives pod restarts, multi-replica deployments, AKS node rebuilds,
> and the 72-hour timer that auto-escalates abandoned reviews.

> **What this lab is NOT.** It is *not* "rebuild fraud-workflow on Azure
> from scratch". The [`fraud-workflow`](../../../src/fraud-workflow/)
> service already exists and is wired into the AppHost — exactly the
> code you toured in the Local Track. This lab adds **only the durable
> + production-gating layer**: provision DTS, swap the in-memory
> approval gate for a DTS-backed one, gate the operator endpoints on an
> Entra app role, ship the pod to AKS, then prove it survives a
> deliberate pod kill mid-pause.

## What you'll learn

This lab introduces a fundamentally different agent topology than Lab 2:

- **Lab 2** is **synchronous** — a user sends a chat message, agents run, a response comes back in seconds.
- **Lab 3 (Full Azure)** is **ambient and durable** — *no human starts the work*. Events arrive continuously (return requests, large refund claims, suspicious order patterns), agents investigate in parallel, and the workflow **pauses indefinitely** waiting for a human to approve or reject the recommended action. Hours, days, or weeks may pass between the analysis and the human decision. Pods will restart in the meantime.

This is the pattern behind real-world systems like fraud detection, IT incident triage, loan underwriting, and content moderation. Across the Local Track and the Full Azure Track you exercise four primitives that simple "user asks, agent answers" loops never need:

1. **Fan-out / fan-in** — one alert dispatches to N specialist agents in parallel; an aggregator combines their findings. *(Already implemented — shared with the Local Track.)*
2. **Human-in-the-loop with timeout** — the workflow blocks on an external event (analyst decision) racing a timer (auto-escalate after 72h). *(In-memory on Local Track; durable on this track.)*
3. **Durable replay** — when the worker pod is killed mid-step, a new pod reads the event log from Azure Durable Task Scheduler and resumes deterministically. *(This track only.)*
4. **Stateful agent conversations across the pause** — when the analyst rejects the recommendation and asks for re-analysis, the agents pick up with full context. *(Hardened on this track because state outlives the pod.)*

The system you're hardening is the **Refund Risk Workflow** for Contoso Outdoors — same flow described in the [Local Track lab](../local/lab-3.md#step-1--read-the-workflow-assembly): a customer requests a refund, three specialists fan out to analyze it, the aggregator picks `approve`/`manual_review`/`escalate`, and (when not auto-approved) a human at Contoso Operations decides.

By the end of this lab the same workflow runs on AKS with durable state — kill the pod mid-pause and the new pod resumes from exactly the right line, and a 72-hour timer auto-escalates abandoned reviews even if every operator is on PTO.

### Microsoft Agent Framework primer (Workflows)

If you skipped the Local Track, the Workflows primer there walks the
core types — read [the table in `../local/lab-3.md`](../local/lab-3.md#microsoft-agent-framework-primer-workflows)
before continuing. The summary is:

- **Executor** — one node, typed input → typed output. The repo has 5: [router](../../../src/fraud-workflow/Workflows/RouterExecutor.cs), 3 [agent executors](../../../src/fraud-workflow/Workflows/AgentExecutors.cs), [aggregator](../../../src/fraud-workflow/Workflows/AggregatorExecutor.cs), [human gate](../../../src/fraud-workflow/Workflows/HumanGateExecutor.cs).
- **WorkflowBuilder** — fluent DSL: `AddFanOutEdge`, `AddFanInBarrierEdge`, `AddEdge`. The graph is assembled in [`RefundRiskWorkflow.cs`](../../../src/fraud-workflow/Workflows/RefundRiskWorkflow.cs).
- **External events** — the framework's pause primitive (`ctx.WaitForExternalEventAsync<T>(...)`). For dependency hygiene the repo wraps the same pattern behind [`IApprovalGate`](../../../src/fraud-workflow/Services/IApprovalGate.cs).
- **Persistence** — `Microsoft.Agents.AI.Workflows.Checkpointing.ICheckpointStore` (e.g. `JsonCheckpointStore`, `FileSystemJsonCheckpointStore`). The Local Track wires no checkpoint store at all; this lab adds DTS as the checkpoint backend so a pod restart resumes from the last saved transition.

The three rules that save you debugging time stay identical:

1. **Executors must be deterministic given their inputs.** The runtime *replays* executor calls when it resumes from a checkpoint. Side effects (DB writes, MCP tool calls) belong inside agent executors, but anything in the workflow plumbing should be pure. This becomes load-bearing on the Full Azure Track because DTS replays are what give you durability.
2. **Pauses are awaits, not callbacks.** `var decision = await gate.WaitForDecisionAsync(...);` reads exactly like a normal `await`. The runtime is what makes the gap between "send" and "resume" survive process restarts.
3. **One workflow instance per business event.** Each refund alert produces a fresh workflow run with its own `AlertId`. The Operations UI lists in-flight runs by that ID; DTS keys checkpoints by it.

## Prerequisites

- [Local Track Lab 3](../local/lab-3.md) complete — you've toured the code, simulated a refund, approved it, and seen the durability gap when you `Ctrl+C` the AppHost.
- [Full Azure Track Labs 1 and 2](lab-1.md) complete — Foundry + AKS + Terraform state are already in place.
- A second browser profile or incognito window to play the **Operations** role separately from the **Customer** role.

## The architecture you're hardening

```text
   Customer submits         ┌─────────────────┐
   refund > $200            │  bff-api        │  POST /api/v1/refunds
       ─────────────────►   │  /refunds       │  proxies to fraud-workflow
                            └────────┬────────┘
                                     │
                                     ▼
                            ┌─────────────────────┐
                            │  fraud-workflow     │  ← AKS deployment (HPA)
                            │  pod                │     workflow primitives
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
                            │ → RefundRiskAssessment│
                            └────────┬─────────────┘
                                     │ if RecommendedAction != "approve"
                                     ▼
                            ┌──────────────────────────────────┐
                            │  HumanGateExecutor               │  PAUSE
                            │  await gate.WaitForDecisionAsync │
                            └────────┬─────────────────────────┘
                                     │  ┌───────────────────────────────┐
                                     │  │ DurableTaskApprovalGate       │
                                     ├──┤ • external-event subscription │
                                     │  │   in DTS                       │
                                     │  │ • timer registered in DTS      │
                                     │  │ • CHECKPOINTED                 │
                                     │  └───────────────────────────────┘
                          ┌──────────┴──────────────┐
                          ▼                         ▼
              ┌─────────────────────┐     ┌────────────────────┐
              │ Operator clicks     │     │ DTS timer (72h)    │
              │ Approve / Reject    │     │ → FinalAction.     │
              │ / Reinvestigate     │     │   Timeout (P1 esc) │
              └──────────┬──────────┘     └────────────────────┘
                         ▼
                ┌────────────────────┐
                │ FinalAction emitted│
                │ → workflow output  │
                └────────────────────┘
```

The single difference from the [Local Track architecture](../local/lab-3.md#the-architecture-youre-exploring) is the dashed box: `IApprovalGate` is no longer backed by a process-local `ConcurrentDictionary<string, TaskCompletionSource>` but by **Azure Durable Task Scheduler**. The contract on `HumanGateExecutor` is unchanged — that's the whole point of having put the gate behind an interface.

> **A map of what's where.** The first eight rows already exist in
> `src/`. The last four are net-new artifacts you produce in this lab.
>
> | Concern | Location | Status |
> |---|---|---|
> | RouterExecutor | [`src/fraud-workflow/Workflows/RouterExecutor.cs`](../../../src/fraud-workflow/Workflows/RouterExecutor.cs) | Existing |
> | 3 specialist agents | [`src/fraud-workflow/Agents/`](../../../src/fraud-workflow/Agents/) | Existing |
> | AggregatorExecutor + RiskAggregator | [`src/fraud-workflow/Workflows/AggregatorExecutor.cs`](../../../src/fraud-workflow/Workflows/AggregatorExecutor.cs), [`Services/RiskAggregator.cs`](../../../src/fraud-workflow/Services/RiskAggregator.cs) | Existing |
> | HumanGateExecutor + IApprovalGate | [`src/fraud-workflow/Workflows/HumanGateExecutor.cs`](../../../src/fraud-workflow/Workflows/HumanGateExecutor.cs), [`Services/IApprovalGate.cs`](../../../src/fraud-workflow/Services/IApprovalGate.cs) | Existing |
> | InMemoryApprovalGate (Local-only) | [`src/fraud-workflow/Services/InMemoryApprovalGate.cs`](../../../src/fraud-workflow/Services/InMemoryApprovalGate.cs) | Existing — *your starting point for the swap-in* |
> | Workflow assembly | [`src/fraud-workflow/Workflows/RefundRiskWorkflow.cs`](../../../src/fraud-workflow/Workflows/RefundRiskWorkflow.cs) | Existing |
> | BFF proxy + operator endpoints | [`src/bff-api/Endpoints/OperationsEndpoints.cs`](../../../src/bff-api/Endpoints/OperationsEndpoints.cs), [`Services/FraudWorkflowClient.cs`](../../../src/bff-api/Services/FraudWorkflowClient.cs) | Existing |
> | Operator UI | [`src/blazor-ui/Pages/Operations.razor`](../../../src/blazor-ui/Pages/Operations.razor) | Existing |
> | DTS task hub (Terraform) | `infra/terraform/modules/durable-task-scheduler/` | **You build** |
> | `id-fraud-workflow` workload identity | `infra/terraform/modules/agent-identity` reuse | **You build** |
> | `DurableTaskApprovalGate` impl | `src/fraud-workflow/Services/Durable/` | **You build** |
> | `Operations` Entra app role + auth gate | `infra/terraform/modules/entra-app-roles/`, `src/bff-api/Program.cs` policy | **You build** |
> | AKS deployment (Helm) | `src/fraud-workflow/chart/`, `infra/k8s/manifests/network-policies/` | **You build** |

> **Background reading.** For a deep dive on **why** durable workflows matter — the wait-for-human problem, replay semantics, exactly-once side effects — read the [.NET Durable Task SDK docs](https://learn.microsoft.com/en-us/azure/durable-task-scheduler/durable-task-sdks/durable-task-sdks). Every primitive on this track has a direct equivalent in that SDK.

## Step 1 — Re-read the existing workflow code with production eyes

Before you provision a single Azure resource, do a focused re-read of the existing `fraud-workflow` code with the *durability* lens on. Look for the seams the Full Azure Track is going to cross.

1. Open [`Workflows/RefundRiskWorkflow.cs`](../../../src/fraud-workflow/Workflows/RefundRiskWorkflow.cs). Notice that **no `ICheckpointStore` is registered** in the builder. That's the gap — Step 5 below registers DTS as the checkpoint store, no other code change needed.
2. Open [`Workflows/HumanGateExecutor.cs`](../../../src/fraud-workflow/Workflows/HumanGateExecutor.cs). The whole executor is `await _gate.WaitForDecisionAsync(...)`. Because that `await` is the only mutation in the executor, the runtime can checkpoint state immediately *before* it and replay deterministically.
3. Open [`Services/IApprovalGate.cs`](../../../src/fraud-workflow/Services/IApprovalGate.cs). Two methods: `WaitForDecisionAsync(alertId, assessment, ct)` and `SubmitDecision(alertId, decision)`. The contract makes no assumption about *where* the wait is held — that's the seam your DTS implementation slots into.
4. Open [`Services/InMemoryApprovalGate.cs`](../../../src/fraud-workflow/Services/InMemoryApprovalGate.cs). Note the comment block at the top calling out the durability gap, and the linked `CancellationTokenSource` racing the 72-hour timeout against the operator's click. That's the same race you'll express against DTS — but DTS will hold the timer in its event log, not in your process.

That's the whole shape of the swap-in: register a `DurableTaskApprovalGate` in `Program.cs` and register a DTS-backed `ICheckpointStore` on the workflow. *Nothing else in the workflow code changes.*

## Step 2 — Provision the DTS task hub

Add a Terraform module under [`infra/terraform/modules/`](../../../infra/terraform/modules/) named `durable-task-scheduler/`. The DTS resource type is `azurerm_durable_task_scheduler` (preview as of writing — pin the API version in [`providers.tf`](../../../infra/terraform/providers.tf) the same way the other preview services are pinned).

The module needs:

- A scheduler resource (`name`, `location`, `sku = "Dedicated"`).
- A task hub child resource (`name = "refund-risk-hub"`).
- An RBAC assignment of the `Durable Task Data Contributor` role to the `id-fraud-workflow` workload identity (created in Step 3) on the task hub scope.

Wire the module into [`infra/terraform/main.tf`](../../../infra/terraform/main.tf) alongside the other module calls. Surface the scheduler endpoint and task hub name as Terraform outputs and route them into the [`k8s-secrets.tf`](../../../infra/terraform/k8s-secrets.tf) ConfigMap so the pod resolves them at startup as `DurableTask:Endpoint` and `DurableTask:TaskHub` (matching the [config naming standard](../../config-naming-standard.md)). The `Refund:ApprovalTimeout` config key (TimeSpan string, e.g. `"72:00:00"` for the 72h SLA, `"00:05:00"` for a demo) is the same in every environment — only the *backend* of the gate changes.

> **Why a separate DTS task hub per environment, not per service?** DTS hubs are inexpensive — running one per environment per workload class makes blast-radius reasoning trivial. Sharing a hub across teams means a runaway orchestration in one product can throttle every other team's workflows.

## Step 3 — Provision the agent identity

Reuse the existing [`agent-identity` module](../../../infra/terraform/modules/) (the same one used by `crm-agent`, `product-agent`, etc.) with a fresh call:

- `agent_name = "fraud-workflow"`
- `service_account_name = "sa-fraud-workflow"`
- `namespace = "contoso"`

This creates an Entra Agent ID with its own object ID, a federated credential bound to the AKS OIDC issuer for `system:serviceaccount:contoso:sa-fraud-workflow`, and the same `User Access Administrator`-scoped role assignments on Foundry that the other agents already have. See [docs/security.md § Agent authentication](../../security.md#agent-authentication) for the full identity story.

The DTS RBAC assignment from Step 2 binds this identity to the task hub. End-state: the pod authenticates to DTS using `DefaultAzureCredential` (federated workload identity → Entra → DTS data plane). No connection strings, no keys.

## Step 4 — Build the `DurableTaskApprovalGate`

Create `src/fraud-workflow/Services/Durable/DurableTaskApprovalGate.cs` implementing [`IApprovalGate`](../../../src/fraud-workflow/Services/IApprovalGate.cs). The structure mirrors [`InMemoryApprovalGate`](../../../src/fraud-workflow/Services/InMemoryApprovalGate.cs) but the wait is held in DTS instead of in process memory. Two methods:

- `WaitForDecisionAsync(alertId, assessment, ct)` — when called from inside the workflow, this is the durable equivalent of the in-memory race: register an external event subscription on DTS for event name `"ApprovalDecision"` and a DTS-backed timer with the configured `Refund:ApprovalTimeout` (default `72:00:00`, TimeSpan format); `await Task.WhenAny(...)` on the two; return the winner. Both the subscription and the timer are part of the orchestration's event log — they survive a pod restart.
- `SubmitDecision(alertId, decision)` — called by `OperationsEndpoint` from outside the orchestration when the operator clicks Approve/Reject/Reinvestigate. It calls the DTS client's `RaiseEventAsync(orchestrationInstanceId, "ApprovalDecision", decision)`. *This* is the call that resumes the paused workflow.

In [`Program.cs`](../../../src/fraud-workflow/Program.cs), bind the gate registration to environment:

- `ASPNETCORE_ENVIRONMENT=Local` → `InMemoryApprovalGate` (existing default).
- All other environments → `DurableTaskApprovalGate`.

Also in `Program.cs`, register a DTS-backed checkpoint store with the workflow runtime so the pre-pause state is persisted, and gate `FraudWorkflowRunner` startup behind a DTS connectivity health check so the pod doesn't go `Ready` until it can reach the task hub.

> **Why an `if (env == Local)` switch in `Program.cs` and not separate
> binaries?** Two reasons. First, the surface area of the swap is one
> DI registration — splitting binaries to save one `if` is over-engineering.
> Second, the `Local` branch is *exercised by the entire test suite*; if
> we forked binaries, the Local code would silently bit-rot.

## Step 5 — Add an `Operations` Entra app role and gate the BFF endpoints

Customer-facing routes are unchanged. The operator routes need to be **authorized**, **audited**, and **attributable** — every approval click should resolve to a specific Entra identity in App Insights and Cosmos DB.

1. Add a Terraform module `entra-app-roles/` (or extend the existing app-registration module) declaring an `Operations` app role on the BFF's app registration.
2. Assign the role to a small group of test users (`ContosoOperations` or similar). Verify in the Entra portal.
3. In [`src/bff-api/Program.cs`](../../../src/bff-api/Program.cs), tighten every `RequireAuthorization()` call on the four operations endpoints (`submitRefundEndpoint`, `listPendingEndpoint`, `submitDecisionEndpoint`, `getOutcomeEndpoint`) to `RequireAuthorization(p => p.RequireRole("Operations"))`. A customer-only user must get `403`, not `200`. (The endpoint mapping itself stays in [`OperationsEndpoints.cs`](../../../src/bff-api/Endpoints/OperationsEndpoints.cs); the auth policy lives in `Program.cs` because that's where the `useEntraAuth` switch is.)
4. In [`src/blazor-ui/Pages/Operations.razor`](../../../src/blazor-ui/Pages/Operations.razor), tighten `@attribute [Authorize]` to `@attribute [Authorize(Roles = "Operations")]` so non-operators don't even see the menu item resolve.

This is the difference between *demo HITL* and *production HITL*: every approval is attributed to a specific Entra identity in App Insights — and *only* members of `ContosoOperations` can see the queue, much less click a button on it.

## Step 6 — Containerize and deploy to AKS

For each piece, mirror the existing pattern of one of the other agents (`crm-agent` is closest in shape).

1. Add `src/fraud-workflow/Dockerfile` — clone from [`src/crm-agent/Dockerfile`](../../../src/crm-agent/Dockerfile), update the project name. Same multi-stage build, same `dotnet publish` flags, same non-root user.
2. Add `src/fraud-workflow/chart/` — clone from [`src/crm-agent/chart/`](../../../src/crm-agent/chart/), update `Chart.yaml`, image repository, the service account name (`sa-fraud-workflow`), and the ConfigMap keys for `DurableTask:Endpoint`, `DurableTask:TaskHub`, and `Refund:ApprovalTimeoutHours`.
3. Add a `NetworkPolicy` under [`infra/k8s/manifests/network-policies/`](../../../infra/k8s/manifests/) allowing egress to the DTS service endpoint (DTS uses gRPC over HTTPS) plus the existing `crm-mcp` and `knowledge-mcp` egress that the other agents already have.
4. Add the new component to the deploy CI workflow ([`.github/workflows/`](../../../.github/workflows/)) — same `kubectl apply` / `helm upgrade` shape as the existing services.

Roll forward with [`infra/deploy.ps1`](../../../infra/deploy.ps1) (or `deploy.sh` on Linux/macOS). Verify:

```powershell
kubectl get pods -n contoso -l app=fraud-workflow
kubectl logs -n contoso -l app=fraud-workflow --tail=50
```

The pod should log `Connected to DTS task hub: refund-risk-hub` and stay `Ready`.

## Step 7 — Test durability for real

This is the demonstration the Local Track couldn't do.

1. Submit a refund request via the Blazor UI (signed in as `emma.wilson` from `local-dev-credentials.txt`).
2. Confirm the review card appears on `/operations` (the polling loop in [`Operations.razor`](../../../src/blazor-ui/Pages/Operations.razor) picks it up within ~5 seconds).
3. Drain the `fraud-workflow` deployment:

   ```powershell
   kubectl scale deployment/fraud-workflow -n contoso --replicas=0
   kubectl scale deployment/fraud-workflow -n contoso --replicas=1
   ```

4. Reload `/operations` — the review card is **still there** (DTS held the event subscription and the timer; the new pod re-attached on startup).
5. Click **Approve refund** — the new pod receives the `RaiseEventAsync` from DTS, resumes the orchestration past the gate, and emits the `FinalAction` outcome.

Open App Insights → **Application map**. The orchestration shows up as a multi-segment operation that spans the restart, with a gap during the worker downtime. That gap is the proof that the workflow was *waiting in DTS*, not running in any worker.

## Step 8 — Run the auto-escalate path

Submit another refund and **don't approve it**. After 72 hours (or override the timer with `Refund:ApprovalTimeout = "00:05:00"` for demo purposes — set it via Helm value or ConfigMap and roll the deployment), the timer wins the race against the external event and the workflow emits `FinalAction.Timeout(...)`. A real production handler would, at this point, page on-call or page a supervisor with a P1 escalation; this lab leaves that follow-on action as a stretch exercise (see [What's next](#whats-next)).

This — **a timer racing an external event, both held in durable storage** — is the part that's almost impossible to build correctly without a durable orchestrator.

## Verification checklist

- [ ] `fraud-workflow` pod is `Ready` in the `contoso` namespace
- [ ] DTS task hub `refund-risk-hub` exists in the resource group
- [ ] Workload identity `id-fraud-workflow` has `Durable Task Data Contributor` on the task hub
- [ ] Submitting a refund through the Blazor UI returns `202` and creates an orchestration instance visible in DTS metrics
- [ ] `/operations` is gated by the `Operations` role (a customer-only user gets `403`)
- [ ] Killing the `fraud-workflow` pod mid-pause does **not** lose the pending review (Step 7)
- [ ] Approving from `/operations` after a pod restart resumes the workflow and the outcome is recorded
- [ ] App Insights shows the orchestration as a single distributed trace spanning the restart
- [ ] The 72h timeout fires and produces `FinalAction.Timeout` when no decision is given (Step 8)
- [ ] `ComponentIndependenceTests` is still green — `fraud-workflow` adds zero project-to-project references even with the DTS code

## What's next

You've built every primitive needed for ambient, durable, human-gated agent workflows. The same pattern transposes to:

- **IT incident triage** — page on-call, race against escalation timer, multi-step remediation.
- **Loan underwriting** — fan out to credit + employment + compliance checks, wait for underwriter approval, execute disbursement.
- **Content moderation** — multi-model classification, human reviewer for borderline cases, scheduled re-review for new context.

Ideas for follow-on labs:

- Wire `FinalAction.Timeout` into a real escalation channel (Teams + PagerDuty) — exercises post-pause side effects with replay safety.
- Add a **second human gate** (e.g., supervisor approval for refunds > $1000) — exercises nested external events.
- Add a **stateful conversation entity** so the analyst can ask the agents follow-up questions during review without restarting the whole workflow.
- Replace the rule-based [`RiskAggregator`](../../../src/fraud-workflow/Services/RiskAggregator.cs) with a fourth LLM-backed `RiskJudgeAgent` and compare quality vs. cost.
- Add a real **customer-facing "Refund this order" button** on the Blazor order detail page (today the customer-side flow is exercised via the dashboard's "Simulate refund alert" button or a direct `POST /api/v1/refunds`; the BFF + workflow plumbing already exists end-to-end).
