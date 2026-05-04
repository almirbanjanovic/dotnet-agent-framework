using Microsoft.Extensions.Diagnostics.HealthChecks;

// Pings the CRM MCP server. The Product Agent uses CRM tools for
// loyalty-tier promotions; this probe lets AKS pull the pod from the
// ready set when CRM is unreachable.

internal sealed class CrmMcpHealthCheck(CrmMcpClientProvider crmProvider) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = await crmProvider.GetClientAsync(cancellationToken);
            _ = await client.PingAsync(cancellationToken: cancellationToken);
            return HealthCheckResult.Healthy("CRM MCP server is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("CRM MCP server is not reachable.", ex);
        }
    }
}
