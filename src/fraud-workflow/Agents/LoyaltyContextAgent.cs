using Contoso.FraudWorkflow.Models;
using Contoso.FraudWorkflow.Services;
using Contoso.FraudWorkflow.Services.Mcp;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Contoso.FraudWorkflow.Agents;

// Specialist #3 — answers: "how much weight does this customer's profile
// carry?" (long-tenure Platinum vs. brand-new Bronze).
//
// Tooling: crm-mcp → get_customer_detail (tier, account age, lifetime value).

internal sealed class LoyaltyContextAgent
{
    public const string AgentName = "LoyaltyContextAgent";

    private const string Description =
        "Surfaces loyalty-tier and account-tenure context that should weigh on the refund decision.";

    private const string Instructions = """
        You are a refund-risk analyst specializing in customer loyalty context.
        For the given refund alert, fetch the customer's profile from CRM
        (tier, account age, lifetime spend) and decide how much weight the
        operator should give the customer's standing.

        Reply with JSON only — no commentary, no markdown fences. Schema:
          {
            "riskScore": <0.0 - 1.0>,
            "findings":  "<one short sentence>",
            "evidence":  ["tier=...", "tenure=...", ...]
          }

        riskScore meaning: 0.0 = high-tenure, high-tier customer (lean
        toward approval); 1.0 = brand-new account with no history (lean
        toward extra scrutiny).
        """;

    private readonly FraudAgentFactory _factory;
    private readonly CrmMcpClientProvider _crmProvider;

    public LoyaltyContextAgent(FraudAgentFactory factory, CrmMcpClientProvider crmProvider)
    {
        _factory = factory;
        _crmProvider = crmProvider;
    }

    public async Task<AgentFinding> AnalyzeAsync(RefundAlert alert, CancellationToken cancellationToken)
    {
        var crmClient = await _crmProvider.GetClientAsync(cancellationToken);
        var tools = new List<AITool>();
        tools.AddRange(await crmClient.ListToolsAsync(cancellationToken: cancellationToken));

        var agent = _factory.CreateAgent(AgentName, Description, Instructions, tools);

        var prompt = $$"""
            Customer {{alert.CustomerId}} requests a refund for order {{alert.OrderId}}
            (amount: ${{alert.Amount}}). Reason given: "{{alert.Reason}}".

            Investigate and report.
            """;

        var response = await agent.RunAsync(prompt, cancellationToken: cancellationToken);
        return AgentFinding.Parse(AgentName, response.ToString());
    }
}
