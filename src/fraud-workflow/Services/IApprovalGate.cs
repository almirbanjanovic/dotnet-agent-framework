using Contoso.FraudWorkflow.Models;

namespace Contoso.FraudWorkflow.Services;

// Abstraction over the human-in-the-loop pause. The Local Track ships
// `InMemoryApprovalGate` (TaskCompletionSource per pending alert);
// the Full Azure Track replaces it with a Durable-Task-Scheduler-backed
// implementation that survives pod restarts.

internal interface IApprovalGate
{
    /// <summary>
    /// Register a pending review and wait for the operator's decision.
    /// Returns when an operator submits a decision via
    /// <c>SubmitDecision</c>, when the cancellation token fires, or
    /// when the configured timeout elapses (in which case the result
    /// is <see cref="ApprovalDecision.TimedOut"/>).
    /// </summary>
    Task<ApprovalDecision> WaitForDecisionAsync(
        RefundRiskAssessment assessment,
        CancellationToken cancellationToken);

    /// <summary>
    /// All assessments currently awaiting an operator decision.
    /// Snapshot — safe to enumerate without holding any lock.
    /// </summary>
    IReadOnlyCollection<RefundRiskAssessment> ListPending();

    /// <summary>
    /// Wake up the workflow waiting on the given alert. Returns false
    /// if no workflow is currently paused on that alert.
    /// </summary>
    bool SubmitDecision(string alertId, ApprovalDecision decision);
}
