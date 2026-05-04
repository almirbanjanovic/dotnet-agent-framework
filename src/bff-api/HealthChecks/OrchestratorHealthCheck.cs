using Contoso.BffApi.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Contoso.BffApi.HealthChecks;

// Pings the orchestrator's /health endpoint. The BFF can't service /chat
// without it, so failing the readiness probe is the right behaviour.

internal sealed class OrchestratorHealthCheck(OrchestratorClient orchestratorClient) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await orchestratorClient.GetHealthAsync(cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("Orchestrator Agent is reachable.")
                : HealthCheckResult.Unhealthy("Orchestrator Agent returned an error status.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Orchestrator Agent is not reachable.", ex);
        }
    }
}
