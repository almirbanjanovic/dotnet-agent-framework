using Contoso.FraudWorkflow.Models;

namespace Contoso.FraudWorkflow.Services;

// Deterministic, no-LLM aggregator. Pulled out of the agent loop on
// purpose so the recommended action is explainable, testable, and cheap.
//
// Rules:
//   overall = max(history, condition, loyalty)        — pessimistic merge
//   < 0.34   → "approve"          (auto-approve, no human gate)
//   < 0.67   → "manual_review"    (operator decides)
//   ≥ 0.67   → "escalate"         (operator decides; high-priority ticket)

internal sealed class RiskAggregator
{
    public RefundRiskAssessment Aggregate(
        RefundAlert alert,
        AgentFinding history,
        AgentFinding condition,
        AgentFinding loyalty)
    {
        var overall = Math.Max(history.RiskScore, Math.Max(condition.RiskScore, loyalty.RiskScore));
        var action = overall switch
        {
            < 0.34 => "approve",
            < 0.67 => "manual_review",
            _      => "escalate"
        };

        return new RefundRiskAssessment(
            AlertId: alert.AlertId,
            CustomerId: alert.CustomerId,
            OrderId: alert.OrderId,
            Amount: alert.Amount,
            Reason: alert.Reason,
            OverallRiskScore: overall,
            RecommendedAction: action,
            HistoryFinding: history,
            ConditionFinding: condition,
            LoyaltyFinding: loyalty,
            TicketId: alert.TicketId);
    }
}
