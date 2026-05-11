using Contoso.FraudWorkflow.Agents;
using Contoso.FraudWorkflow.Models;
using Microsoft.Agents.AI.Workflows;

namespace Contoso.FraudWorkflow.Workflows;

// Each AgentExecutor wraps one specialist AIAgent. The executor receives
// the RefundAlert (broadcast by RouterExecutor through the fan-out edge)
// and emits a single AgentFinding. The fan-in barrier downstream waits
// until all three executors have produced an AgentFinding, then forwards
// the three findings as separate envelopes to AggregatorExecutor (which
// accumulates them in shared state and emits a single RefundRiskAssessment
// once all three have arrived).

internal sealed class OrderHistoryAgentExecutor(OrderHistoryAgent agent)
    : Executor<RefundAlert, AgentFinding>(OrderHistoryAgent.AgentName)
{
    public override async ValueTask<AgentFinding> HandleAsync(
        RefundAlert message, IWorkflowContext context, CancellationToken cancellationToken = default)
        => await agent.AnalyzeAsync(message, cancellationToken).ConfigureAwait(false);
}

internal sealed class ReturnConditionAgentExecutor(ReturnConditionAgent agent)
    : Executor<RefundAlert, AgentFinding>(ReturnConditionAgent.AgentName)
{
    public override async ValueTask<AgentFinding> HandleAsync(
        RefundAlert message, IWorkflowContext context, CancellationToken cancellationToken = default)
        => await agent.AnalyzeAsync(message, cancellationToken).ConfigureAwait(false);
}

internal sealed class LoyaltyContextAgentExecutor(LoyaltyContextAgent agent)
    : Executor<RefundAlert, AgentFinding>(LoyaltyContextAgent.AgentName)
{
    public override async ValueTask<AgentFinding> HandleAsync(
        RefundAlert message, IWorkflowContext context, CancellationToken cancellationToken = default)
        => await agent.AnalyzeAsync(message, cancellationToken).ConfigureAwait(false);
}
