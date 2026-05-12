using Microsoft.Extensions.Hosting;

namespace Contoso.KnowledgeMcp.Services;

// Drives `ISearchService.WarmupAsync` once during application start.
//
// For `InMemorySearchService` this means embedding all knowledge-base
// chunks before the first user-facing `search_knowledge_base` call.
// Without this step the very first tool call has to wait ~30-60 s for
// 181 sequential embedding round-trips, the agent framework's tool-call
// timeout fires, the MCP request retries, and the partially-built cache
// is abandoned — producing the "knowledge-mcp keeps getting called over
// and over" loop visible in the Aspire dashboard.
//
// We deliberately do NOT propagate warm-up failures: if the embedding
// endpoint is briefly unavailable at start, we still want the service
// to come up and recover via the lazy fallback path on the next request.
internal sealed class SearchServiceWarmupHostedService : BackgroundService
{
    private readonly ISearchService _searchService;
    private readonly ILogger<SearchServiceWarmupHostedService> _logger;

    public SearchServiceWarmupHostedService(
        ISearchService searchService,
        ILogger<SearchServiceWarmupHostedService> logger)
    {
        _searchService = searchService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("Warming up search service ({Type})…", _searchService.GetType().Name);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await _searchService.WarmupAsync(stoppingToken);
            sw.Stop();
            _logger.LogInformation("Search service warm-up completed in {Elapsed} ms.", sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown during warm-up — nothing to do.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Search service warm-up failed; first user request will retry.");
        }
    }
}
