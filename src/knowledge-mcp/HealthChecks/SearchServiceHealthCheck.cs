using Contoso.KnowledgeMcp.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Contoso.KnowledgeMcp.HealthChecks;

// Issues a tiny canned query against the configured ISearchService.
// In InMemory mode this verifies docs were loaded; in Azure mode this
// verifies the search index is reachable with our managed identity.

internal sealed class SearchServiceHealthCheck(ISearchService searchService) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _ = await searchService.SearchAsync("return policy", 1, cancellationToken);
            return HealthCheckResult.Healthy("Knowledge search is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Knowledge search is not reachable.", ex);
        }
    }
}
