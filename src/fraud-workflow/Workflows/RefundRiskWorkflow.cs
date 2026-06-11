using Contoso.FraudWorkflow.Agents;
using Contoso.FraudWorkflow.Services;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace Contoso.FraudWorkflow.Workflows;

// Wires the executors into the fan-out / fan-in graph. The shape is
// declared the same way for every refund alert; the runner calls Build()
// once per RunAsync invocation because InProcessExecution.RunStreamingAsync
// takes exclusive ownership of the returned Workflow (a single shared
// instance throws InvalidOperationException on the second concurrent
// alert). Build() is cheap — it just instantiates executors and wires
// edges.
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
