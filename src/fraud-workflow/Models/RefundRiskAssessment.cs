namespace Contoso.FraudWorkflow.Models;

// What the operations dashboard sees and what the workflow's gate
// receives. Combines the alert with all three findings + a single
// recommended action chosen by the deterministic aggregator.

internal sealed record RefundRiskAssessment(
    string AlertId,
    string CustomerId,
    string OrderId,
    decimal Amount,
    string Reason,
    double OverallRiskScore,
    string RecommendedAction,            // "approve" | "manual_review" | "escalate"
    AgentFinding HistoryFinding,
    AgentFinding ConditionFinding,
    AgentFinding LoyaltyFinding,
    // Threaded through from RefundAlert so the gate/aggregator can drive
    // the eventual ticket callback. Null when the alert was simulated
    // from the Operations dashboard (no originating ticket).
    string? TicketId = null);
