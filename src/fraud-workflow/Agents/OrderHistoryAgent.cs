using Contoso.FraudWorkflow.Models;
using Contoso.FraudWorkflow.Services;
using Contoso.FraudWorkflow.Services.Mcp;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Contoso.FraudWorkflow.Agents;

// Specialist #1 — answers: "is this customer a serial returner?"
//
// Tooling: crm-mcp (get_customer_orders + related). The system prompt
// constrains the model to a strict JSON shape; AgentFinding.Parse falls
// back to a 0.5 score if the model misbehaves.

internal sealed class OrderHistoryAgent
{
    public const string AgentName = "OrderHistoryAgent";

    private const string Description =
        "Inspects a customer's order and return history to spot serial-returner patterns.";

    private const string Instructions = """
        You are a refund-risk analyst specializing in customer order history.
        For the given refund alert, use the available CRM tools to look up:
          - the customer's last 12 months of orders,
          - the customer's prior returns and refunds.
        Decide whether this customer is a "serial returner" or whether the
        return is consistent with their normal pattern.

        Reply with JSON only — no commentary, no markdown fences. Schema:
          {
            "riskScore": <0.0 - 1.0>,
            "findings":  "<one short sentence>",
            "evidence":  ["<fact 1>", "<fact 2>", ...]
          }

        riskScore meaning: 0.0 = clean, expected behaviour; 1.0 = highly
        suspicious (e.g., five returns in 30 days against a brand-new
        account).
        """;

    private readonly FraudAgentFactory _factory;
    private readonly CrmMcpClientProvider _crmProvider;

    public OrderHistoryAgent(FraudAgentFactory factory, CrmMcpClientProvider crmProvider)
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

        // Doubled-brace string interpolation: `$$"""..."""` only treats `{{ }}`
        // as holes, so the literal JSON braces in the schema above don't need
        // escaping when we extend the prompt later.
        var prompt = $$"""
            Customer {{alert.CustomerId}} requests a refund for order {{alert.OrderId}}
            (amount: ${{alert.Amount}}). Reason given: "{{alert.Reason}}".

            Investigate and report.
            """;

        var response = await agent.RunAsync(prompt, cancellationToken: cancellationToken);
        return AgentFinding.Parse(AgentName, response.ToString());
    }
}
