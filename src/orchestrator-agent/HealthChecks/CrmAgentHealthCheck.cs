using Contoso.OrchestratorAgent.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Contoso.OrchestratorAgent.HealthChecks;

// Pings the CRM Agent's /health endpoint. The orchestrator can't route
// CRM intents without it, so failing readiness is the right behaviour.

internal sealed class CrmAgentHealthCheck(CrmAgentClient client) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await client.HttpClient.GetAsync("/health", cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("CRM Agent is reachable.")
                : HealthCheckResult.Unhealthy("CRM Agent returned an error status.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("CRM Agent is not reachable.", ex);
        }
    }
}
