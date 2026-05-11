namespace Contoso.FraudWorkflow.Models;

// What an operator can do at the human gate.
internal enum ApprovalDecision
{
    Approve,        // Issue the refund.
    Reject,         // No refund, send rejection notice.
    Reinvestigate,  // Re-run the workflow with operator feedback (out of scope for the lab).
    TimedOut        // Auto-escalation: nobody responded within Refund:ApprovalTimeout.
}

// Wire payload posted by the Operations dashboard.
internal sealed record DecisionRequest(string AlertId, ApprovalDecision Decision);
