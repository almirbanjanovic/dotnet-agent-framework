namespace Contoso.BlazorUi.Models;

// Wire shape mirrors the FraudWorkflow service's RefundRiskAssessment.
// JsonSerializerOptions.PropertyNameCaseInsensitive=true on the BFF
// client lets PascalCase JSON deserialise into PascalCase records here.

public sealed record AgentFinding(
    string AgentName,
    double RiskScore,
    string Findings,
    IReadOnlyList<string> Evidence);

public sealed record RefundRiskAssessment(
    string AlertId,
    string CustomerId,
    string OrderId,
    decimal Amount,
    string Reason,
    double OverallRiskScore,
    string RecommendedAction,
    AgentFinding HistoryFinding,
    AgentFinding ConditionFinding,
    AgentFinding LoyaltyFinding);

// Operator submits one of these via /api/v1/operations/decisions.
// Approve → refund the customer. Reject → deny. Reinvestigate → keeps
// pending (the workflow re-queues the alert with stricter thresholds in
// a real implementation; in this lab it just records the decision).
public sealed record DecisionRequest(string AlertId, string Decision);

// Final outcome returned by GET /api/v1/operations/{alertId}.
public sealed record FinalAction(
    string AlertId,
    string CustomerId,
    string OrderId,
    string Decision,
    string Source,
    string Summary);
