using Contoso.FraudWorkflow.Agents;
using Contoso.FraudWorkflow.Services;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace Contoso.FraudWorkflow.Workflows;

// Wires the executors into the fan-out / fan-in graph. The shape is
// declared once at startup; every refund alert is a fresh `Run` against
// the same `Workflow` instance.
//
//   RouterExecutor
//        │ (fan-out: broadcast RefundAlert)
//        ├──► OrderHistoryAgentExecutor     ┐
//        ├──► ReturnConditionAgentExecutor  ├── (fan-in barrier: 3 × AgentFinding)
//        └──► LoyaltyContextAgentExecutor   ┘
//                                          │
//                                          ▼
//                              AggregatorExecutor (RefundRiskAssessment)
//                                          │
//                                          ▼
//                              HumanGateExecutor (FinalAction)

internal static class RefundRiskWorkflow
{
    public static Workflow Build(
        OrderHistoryAgent historyAgent,
        ReturnConditionAgent conditionAgent,
        LoyaltyContextAgent loyaltyAgent,
        RiskAggregator aggregator,
        IApprovalGate approvalGate,
        ILoggerFactory loggerFactory)
    {
        var router    = new RouterExecutor();
        var historyEx = new OrderHistoryAgentExecutor(historyAgent);
        var condEx    = new ReturnConditionAgentExecutor(conditionAgent);
        var loyaltyEx = new LoyaltyContextAgentExecutor(loyaltyAgent);
        var aggEx     = new AggregatorExecutor(aggregator);
        var gateEx    = new HumanGateExecutor(approvalGate, loggerFactory.CreateLogger<HumanGateExecutor>());

        return new WorkflowBuilder(router)
            .AddFanOutEdge(router, [historyEx, condEx, loyaltyEx])
            .AddFanInBarrierEdge([historyEx, condEx, loyaltyEx], aggEx)
            .AddEdge(aggEx, gateEx)
            .WithOutputFrom(gateEx)
            .Build();
    }
}
