using Contoso.FraudWorkflow.Models;
using Contoso.FraudWorkflow.Services;
using FluentAssertions;

namespace Contoso.FraudWorkflow.Tests;

// Pure-function tests for the policy that turns three agent findings into
// a refund risk assessment. No I/O, no LLM — RiskAggregator is the unit
// of code we WANT students to be able to reason about deterministically.

public class RiskAggregatorTests
{
    private static readonly RefundAlert SampleAlert =
        new RefundAlert("C001", "O-1234", 425.50m, "Damaged on arrival") { AlertId = "A1" };

    private static AgentFinding Finding(string name, double risk) =>
        new(name, risk, "ok", []);

    [Theory]
    [InlineData(0.0, 0.0, 0.0, "approve")]
    [InlineData(0.1, 0.2, 0.3, "approve")]            // max 0.30 < 0.34
    [InlineData(0.33, 0.0, 0.0, "approve")]           // max 0.33 < 0.34
    [InlineData(0.34, 0.0, 0.0, "manual_review")]    // exactly 0.34 → review
    [InlineData(0.5, 0.5, 0.5, "manual_review")]
    [InlineData(0.66, 0.0, 0.0, "manual_review")]    // max 0.66 < 0.67
    [InlineData(0.67, 0.0, 0.0, "escalate")]         // exactly 0.67 → escalate
    [InlineData(1.0, 0.0, 0.0, "escalate")]
    public void Aggregate_PicksRecommendedAction_BasedOnMaxRisk(
        double history, double condition, double loyalty, string expected)
    {
        var aggregator = new RiskAggregator();

        var result = aggregator.Aggregate(
            SampleAlert,
            Finding(Agents.OrderHistoryAgent.AgentName, history),
            Finding(Agents.ReturnConditionAgent.AgentName, condition),
            Finding(Agents.LoyaltyContextAgent.AgentName, loyalty));

        result.RecommendedAction.Should().Be(expected);
        result.OverallRiskScore.Should().Be(Math.Max(history, Math.Max(condition, loyalty)));
        result.AlertId.Should().Be("A1");
        result.CustomerId.Should().Be("C001");
    }
}
