namespace Contoso.FraudWorkflow.Models;

// Terminal output of the workflow. Either an automatic decision (the
// aggregator said "approve" so we never paused) or an operator decision
// (the gate woke up because someone clicked) or a timeout escalation.

internal sealed record FinalAction(
    string AlertId,
    string CustomerId,
    string OrderId,
    ApprovalDecision Decision,
    string Source,        // "auto" | "operator" | "timeout"
    string Summary,
    // Carried through so the runner can call back to CRM API and update
    // the customer-facing ticket. Null when the alert was simulated from
    // the Operations dashboard (no originating ticket).
    string? TicketId = null)
{
    public static FinalAction AutoApprove(RefundRiskAssessment a) =>
        new(a.AlertId, a.CustomerId, a.OrderId, ApprovalDecision.Approve,
            "auto",
            $"Auto-approved (overall risk {a.OverallRiskScore:F2}).",
            a.TicketId);

    public static FinalAction FromOperator(RefundRiskAssessment a, ApprovalDecision decision) =>
        new(a.AlertId, a.CustomerId, a.OrderId, decision, "operator",
            $"Operator decision: {decision}.",
            a.TicketId);

    public static FinalAction Timeout(RefundRiskAssessment a) =>
        new(a.AlertId, a.CustomerId, a.OrderId, ApprovalDecision.TimedOut,
            "timeout",
            "No operator decision before configured timeout. Auto-escalated.",
            a.TicketId);
}
