using Contoso.FraudWorkflow.Services.Mcp;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Contoso.FraudWorkflow.HealthChecks;

internal sealed class KnowledgeMcpHealthCheck(KnowledgeMcpClientProvider knowledgeProvider) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = await knowledgeProvider.GetClientAsync(cancellationToken);
            _ = await client.PingAsync(cancellationToken: cancellationToken);
            return HealthCheckResult.Healthy("Knowledge MCP server is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Knowledge MCP server is not reachable.", ex);
        }
    }
}
