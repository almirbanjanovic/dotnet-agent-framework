using Contoso.FraudWorkflow.Services.Mcp;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Contoso.FraudWorkflow.HealthChecks;

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
