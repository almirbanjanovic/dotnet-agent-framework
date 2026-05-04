using Microsoft.Extensions.Diagnostics.HealthChecks;

// Pings the Knowledge MCP server. The CRM Agent can technically answer
// some questions without it (order status, ticket lookup), but policy
// questions silently degrade — better to mark unready and let traffic
// drain to a healthy replica.

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
