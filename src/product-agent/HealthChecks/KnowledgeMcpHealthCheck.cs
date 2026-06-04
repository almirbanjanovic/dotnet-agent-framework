using Microsoft.Extensions.Diagnostics.HealthChecks;

// Pings the Knowledge MCP server — the primary source of truth for
// product recommendations. If this is down, the Product Agent has
// almost nothing useful to say, so failing the readiness probe is
// the right call.

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
