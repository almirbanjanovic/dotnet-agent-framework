using Microsoft.Extensions.Diagnostics.HealthChecks;

// Pings the CRM MCP server. Used as a `ready` probe so AKS pulls the pod
// out of the ready set when its CRM tooling is unreachable — the agent
// is useless without it.

internal sealed class CrmMcpHealthCheck(CrmMcpClientProvider crmProvider) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await crmProvider.ExecuteWithClientRetryAsync(
                static (client, ct) => client.PingAsync(cancellationToken: ct),
                cancellationToken);
            return HealthCheckResult.Healthy("CRM MCP server is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("CRM MCP server is not reachable.", ex);
        }
    }
}
