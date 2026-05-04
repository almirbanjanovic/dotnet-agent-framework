using Contoso.BffApi.Services;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Contoso.BffApi.HealthChecks;

// Pings the agents Cosmos account (where conversation history lives).
// Only registered when DataMode is not InMemory.

internal sealed class CosmosHealthCheck(CosmosClient client) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _ = await client.ReadAccountAsync();
            return HealthCheckResult.Healthy("Cosmos DB agents account is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Cosmos DB agents account is not reachable.", ex);
        }
    }
}
