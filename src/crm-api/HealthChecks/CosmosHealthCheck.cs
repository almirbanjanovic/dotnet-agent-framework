using Contoso.CrmApi.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Contoso.CrmApi.HealthChecks;

// Probes the configured ICosmosService. CosmosService.CheckConnectivityAsync
// reads a single document; InMemoryCrmDataService returns true cheaply.
// Either way this is the readiness signal for the API pod.

internal sealed class CosmosHealthCheck(ICosmosService cosmosService) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var isHealthy = await cosmosService.CheckConnectivityAsync(cancellationToken);
        return isHealthy
            ? HealthCheckResult.Healthy("Cosmos DB is reachable.")
            : HealthCheckResult.Unhealthy("Cosmos DB is not reachable.");
    }
}
