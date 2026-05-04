using Contoso.BffApi.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Contoso.BffApi.HealthChecks;

// Pings the CRM API's /health endpoint via the typed CrmApiClient.

internal sealed class CrmApiHealthCheck(CrmApiClient crmApiClient) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await crmApiClient.GetHealthAsync(cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("CRM API is reachable.")
                : HealthCheckResult.Unhealthy("CRM API returned an error status.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("CRM API is not reachable.", ex);
        }
    }
}
