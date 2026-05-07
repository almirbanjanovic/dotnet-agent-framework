using Contoso.CrmMcp.Clients;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Contoso.CrmMcp.HealthChecks;

// Pings the CRM API's /health endpoint. Older versions of this check
// piggy-backed on GET /api/v1/customers which (a) leaked the entire
// customer table on every probe and (b) became defunct when that
// endpoint was removed for security reasons.

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
