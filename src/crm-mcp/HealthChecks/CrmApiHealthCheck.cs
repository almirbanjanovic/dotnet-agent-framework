using Contoso.CrmMcp.Clients;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Contoso.CrmMcp.HealthChecks;

// Pings the CRM API with the cheapest call we have (a customer-list).
// If this fails, every CRM tool will fail too, so the MCP server should
// drop out of the ready set.

internal sealed class CrmApiHealthCheck(CrmApiClient crmApiClient) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _ = await crmApiClient.GetAllCustomersAsync(cancellationToken);
            return HealthCheckResult.Healthy("CRM API is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("CRM API is not reachable.", ex);
        }
    }
}
