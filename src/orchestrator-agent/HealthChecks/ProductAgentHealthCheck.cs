using Contoso.OrchestratorAgent.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Contoso.OrchestratorAgent.HealthChecks;

// Pings the Product Agent's /health endpoint. Same logic as the CRM
// agent probe — the orchestrator routes only between these two, so
// either being down is a partial outage.

internal sealed class ProductAgentHealthCheck(ProductAgentClient client) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await client.HttpClient.GetAsync("/health", cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("Product Agent is reachable.")
                : HealthCheckResult.Unhealthy("Product Agent returned an error status.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Product Agent is not reachable.", ex);
        }
    }
}
