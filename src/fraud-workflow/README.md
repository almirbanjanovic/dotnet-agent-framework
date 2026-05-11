# fraud-workflow

The **Refund Risk** workflow service. Receives `POST /api/v1/refunds`,
fans the alert out to three specialist agents in parallel, aggregates
their findings into a `RefundRiskAssessment`, and pauses on a human
gate when the assessment is anything other than `approve`.

| Endpoint                                  | Purpose                                      |
| ----------------------------------------- | -------------------------------------------- |
| `POST   /api/v1/refunds`                  | Customer/BFF submits a refund request.       |
| `GET    /api/v1/operations/pending`       | Lists pending reviews (operations dashboard).|
| `POST   /api/v1/operations/decisions`     | Submits an operator's approve/reject/redo.   |
| `GET    /api/v1/operations/{alertId}`     | Final outcome lookup (after a decision).     |
| `GET    /health` / `GET /ready`           | Liveness / readiness probes.                 |

## Workflow shape

```
RouterExecutor          (broadcast alert)
   │
   ├──► OrderHistoryAgentExecutor    (LLM + crm-mcp)
   ├──► ReturnConditionAgentExecutor (LLM + crm-mcp + knowledge-mcp)
   └──► LoyaltyContextAgentExecutor  (LLM + crm-mcp)
              │   │   │
              ▼   ▼   ▼
        AggregatorExecutor           (pure C#, no LLM)
              │
              ▼
        HumanGateExecutor            (pause on IApprovalGate)
              │
              ▼
        FinalAction (auto-approve | operator decision | timeout escalation)
```

The graph is wired with `Microsoft.Agents.AI.Workflows`'
`WorkflowBuilder.AddFanOutEdge` and `AddFanInBarrierEdge`.

The Local Track ships `InMemoryApprovalGate` — a `ConcurrentDictionary`
of `TaskCompletionSource<ApprovalDecision>` keyed by `alertId`. That gate
is **wiped on process restart**, which is the deliberate gap that the
[Full Azure Track Lab 3](../../docs/labs/full-azure/lab-3.md) closes
with Azure Durable Task Scheduler.

## Configuration keys

| Key                                  | Purpose                                       |
| ------------------------------------ | --------------------------------------------- |
| `Foundry:ProjectEndpoint`            | Azure AI Foundry project endpoint.            |
| `Foundry:DeploymentName`             | Chat model deployment name (e.g. `gpt-4.1`).  |
| `AzureAd:TenantId`                   | Tenant for `DefaultAzureCredential` (optional)|
| `CrmMcp:BaseUrl`                     | http://localhost:5002 (Local) / k8s svc URL.  |
| `KnowledgeMcp:BaseUrl`               | http://localhost:5003 (Local) / k8s svc URL.  |
| `Refund:Threshold`                   | Auto-approve threshold (default $200).        |
| `Refund:ApprovalTimeout`             | TimeSpan; default `72:00:00` (72h).           |
