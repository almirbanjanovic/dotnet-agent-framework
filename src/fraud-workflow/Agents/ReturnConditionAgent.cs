using Contoso.FraudWorkflow.Models;
using Contoso.FraudWorkflow.Services;
using Contoso.FraudWorkflow.Services.Mcp;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Contoso.FraudWorkflow.Agents;

// Specialist #2 — answers: "does the customer's reason match a covered
// return scenario in policy?"
//
// Tooling:
//   crm-mcp           → get_order_detail (what was actually ordered)
//   knowledge-mcp     → search_knowledge_base (returns/refunds policy)

internal sealed class ReturnConditionAgent
{
    public const string AgentName = "ReturnConditionAgent";

    private const string Description =
        "Compares the customer's stated reason against the documented return policy and the actual order contents.";

    private const string Instructions = """
        You are a refund-risk analyst specializing in policy compliance.
        For the given refund alert:
          1. Fetch the order detail from CRM (line items, ship date).
          2. Search the knowledge base for the relevant returns/refunds policy.
          3. Decide whether the stated reason matches a covered scenario,
             considering the policy's window, condition, and category rules.

        Reply with JSON only — no commentary, no markdown fences. Schema:
          {
            "riskScore": <0.0 - 1.0>,
            "findings":  "<one short sentence>",
            "evidence":  ["<policy clause>", "<order fact>", ...]
          }

        riskScore meaning: 0.0 = reason squarely covered by policy;
        1.0 = clearly outside policy (e.g., a 90-day-old final-sale item).
        """;

    private readonly FraudAgentFactory _factory;
    private readonly CrmMcpClientProvider _crmProvider;
    private readonly KnowledgeMcpClientProvider _knowledgeProvider;

    public ReturnConditionAgent(
        FraudAgentFactory factory,
        CrmMcpClientProvider crmProvider,
        KnowledgeMcpClientProvider knowledgeProvider)
    {
        _factory = factory;
        _crmProvider = crmProvider;
        _knowledgeProvider = knowledgeProvider;
    }

    public async Task<AgentFinding> AnalyzeAsync(RefundAlert alert, CancellationToken cancellationToken)
    {
        var tools = new List<AITool>();
        tools.AddRange(await _crmProvider.ExecuteWithClientRetryAsync(
            static (client, ct) => client.ListToolsAsync(cancellationToken: ct),
            cancellationToken));
        tools.AddRange(await _knowledgeProvider.ExecuteWithClientRetryAsync(
            static (client, ct) => client.ListToolsAsync(cancellationToken: ct),
            cancellationToken));

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
