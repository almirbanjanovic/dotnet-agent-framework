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
            await knowledgeProvider.ExecuteWithClientRetryAsync(
                static (client, ct) => client.PingAsync(cancellationToken: ct),
                cancellationToken);
            return HealthCheckResult.Healthy("Knowledge MCP server is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Knowledge MCP server is not reachable.", ex);
        }
    }
}
