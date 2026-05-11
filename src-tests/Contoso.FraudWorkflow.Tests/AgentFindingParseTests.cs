using Contoso.FraudWorkflow.Models;
using FluentAssertions;

namespace Contoso.FraudWorkflow.Tests;

// AgentFinding.Parse is the de-facto contract between the LLM and the
// rest of the workflow. The tests pin its forgiving behaviour: the
// pipeline must keep moving even when the model misbehaves.

public class AgentFindingParseTests
{
    [Fact]
    public void Parse_ValidJson_PopulatesAllFields()
    {
        var raw = """
            {
              "riskScore": 0.82,
              "findings": "Customer has returned 5 of last 6 orders.",
              "evidence": ["O-1001", "O-1003", "O-1005"]
            }
            """;

        var finding = AgentFinding.Parse("OrderHistoryAgent", raw);

        finding.AgentName.Should().Be("OrderHistoryAgent");
        finding.RiskScore.Should().Be(0.82);
        finding.Findings.Should().Contain("5 of last 6");
        finding.Evidence.Should().HaveCount(3).And.Contain("O-1003");
    }

    [Fact]
    public void Parse_StripsCodeFences()
    {
        var raw = "```json\n{\"riskScore\":0.5,\"findings\":\"middle\",\"evidence\":[]}\n```";
        var finding = AgentFinding.Parse("X", raw);
        finding.RiskScore.Should().Be(0.5);
        finding.Findings.Should().Be("middle");
    }

    [Theory]
    [InlineData(-0.5, 0.0)]
    [InlineData(2.0, 1.0)]
    [InlineData(0.5, 0.5)]
    public void Parse_ClampsRiskScoreToUnitRange(double input, double expected)
    {
        var raw = $"{{\"riskScore\":{input},\"findings\":\"x\",\"evidence\":[]}}";
        var finding = AgentFinding.Parse("X", raw);
        finding.RiskScore.Should().Be(expected);
    }

    [Fact]
    public void Parse_NonJson_FallsBackToFreeTextWith0Point5()
    {
        var finding = AgentFinding.Parse("X", "Yeah looks fine to me, no fraud");
        finding.RiskScore.Should().Be(0.5);
        finding.Findings.Should().Be("Yeah looks fine to me, no fraud");
        finding.Evidence.Should().BeEmpty();
    }

    [Fact]
    public void Parse_Empty_ReturnsNoResponseSentinel()
    {
        var finding = AgentFinding.Parse("X", "   ");
        finding.RiskScore.Should().Be(0.5);
        finding.Findings.Should().Be("(no response)");
    }
}
