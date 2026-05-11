using Contoso.FraudWorkflow.Models;
using Contoso.FraudWorkflow.Services;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace Contoso.FraudWorkflow.Workflows;

// The human-in-the-loop pause. If the aggregator already said "approve",
// we short-circuit and emit FinalAction.AutoApprove without involving an
// operator. Otherwise the executor blocks on IApprovalGate and only
// emits FinalAction once an operator decides (or the timeout fires).
//
// On the Local Track the gate is in-memory — restart loses the wait.
// On the Full Azure Track the gate is durable; the same code resumes
// against a fresh process after a pod restart.

internal sealed class HumanGateExecutor : Executor<RefundRiskAssessment, FinalAction>
{
    private readonly IApprovalGate _gate;
    private readonly ILogger<HumanGateExecutor> _logger;

    public HumanGateExecutor(IApprovalGate gate, ILogger<HumanGateExecutor> logger)
        : base(nameof(HumanGateExecutor))
    {
        _gate = gate;
        _logger = logger;
    }

    public override async ValueTask<FinalAction> HandleAsync(
        RefundRiskAssessment assessment, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        if (string.Equals(assessment.RecommendedAction, "approve", StringComparison.Ordinal))
        {
            _logger.LogInformation(
                "Refund {AlertId} auto-approved (overall risk {Risk:F2}); skipping human gate.",
                assessment.AlertId, assessment.OverallRiskScore);
            return FinalAction.AutoApprove(assessment);
        }

        _logger.LogInformation(
            "Refund {AlertId} requires operator decision ({Action}, risk {Risk:F2}). Pausing.",
            assessment.AlertId, assessment.RecommendedAction, assessment.OverallRiskScore);

        var decision = await _gate.WaitForDecisionAsync(assessment, cancellationToken).ConfigureAwait(false);
        return decision == ApprovalDecision.TimedOut
            ? FinalAction.Timeout(assessment)
            : FinalAction.FromOperator(assessment, decision);
    }
}
