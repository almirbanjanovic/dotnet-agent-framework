using Contoso.FraudWorkflow.Models;
using Contoso.FraudWorkflow.Services;
using Microsoft.Agents.AI.Workflows;

namespace Contoso.FraudWorkflow.Workflows;

// Fan-in target. Each upstream agent emits a single AgentFinding and the
// fan-in barrier forwards them as three separate envelopes (NOT a List<T>).
// The runtime filters envelopes by what the target executor declares it
// can handle, so the aggregator must accept `AgentFinding` directly —
// declaring `Executor<List<AgentFinding>>` would cause every envelope to
// be dropped as a type mismatch and the workflow stalls without ever
// invoking the aggregator.
//
// We accumulate findings in per-run shared state (`AlertScope`), then
// build the RefundRiskAssessment once all three have arrived. The work
// happens in `OnMessageDeliveryFinishedAsync` (called once per superstep
// after all messages are delivered) so we only emit one assessment per
// barrier release.
//
// Because we send `RefundRiskAssessment` from a hook rather than from a
// HandleAsync return value, the protocol does not auto-declare it as a
// sendable type — we declare it explicitly with `[SendsMessage]`. This
// is the same reason input-only `Executor<TIn>` cannot send messages
// without explicit declarations.

[SendsMessage(typeof(RefundRiskAssessment))]
internal sealed class AggregatorExecutor : Executor<AgentFinding>
{
    internal const string AlertScope = "refund";
    internal const string AlertKey = "alert";
    private const string FindingsKey = "findings";

    private readonly RiskAggregator _aggregator;

    public AggregatorExecutor(RiskAggregator aggregator) : base(nameof(AggregatorExecutor))
    {
        _aggregator = aggregator;
    }

    public override async ValueTask HandleAsync(
        AgentFinding finding, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var findings = await context
            .ReadStateAsync<List<AgentFinding>>(FindingsKey, AlertScope, cancellationToken)
            .ConfigureAwait(false)
            ?? new List<AgentFinding>();

        findings.Add(finding);

        await context
            .QueueStateUpdateAsync(FindingsKey, findings, AlertScope, cancellationToken)
            .ConfigureAwait(false);
    }

    protected override async ValueTask OnMessageDeliveryFinishedAsync(
        IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var findings = await context
            .ReadStateAsync<List<AgentFinding>>(FindingsKey, AlertScope, cancellationToken)
            .ConfigureAwait(false);

        // Wait until the barrier has released ALL three findings before
        // building the assessment. We expect exactly 3 (one per fan-in
        // source) so equality is more defensive than `>= 3` against future
        // graph changes that might re-deliver findings.
        if (findings is null || findings.Count != 3)
        {
            return;
        }

        var alert = await context
            .ReadStateAsync<RefundAlert>(AlertKey, AlertScope, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "Aggregator could not find the originating RefundAlert in workflow state.");

        var history   = FindByAgent(findings, Agents.OrderHistoryAgent.AgentName);
        var condition = FindByAgent(findings, Agents.ReturnConditionAgent.AgentName);
        var loyalty   = FindByAgent(findings, Agents.LoyaltyContextAgent.AgentName);

        var assessment = _aggregator.Aggregate(alert, history, condition, loyalty);

        // Forward to HumanGateExecutor along the direct edge.
        await context.SendMessageAsync(assessment, targetId: null, cancellationToken)
            .ConfigureAwait(false);

        // Clear accumulated state so a (hypothetical) re-run on this same
        // executor instance starts clean.
        await context
            .QueueStateUpdateAsync<List<AgentFinding>?>(FindingsKey, null, AlertScope, cancellationToken)
            .ConfigureAwait(false);
    }

    private static AgentFinding FindByAgent(List<AgentFinding> findings, string agentName)
    {
        foreach (var f in findings)
        {
            if (string.Equals(f.AgentName, agentName, StringComparison.Ordinal))
            {
                return f;
            }
        }
        // Defensive fallback — shouldn't happen if the graph is intact.
        return new AgentFinding(agentName, 0.5, "(missing)", []);
    }
}
