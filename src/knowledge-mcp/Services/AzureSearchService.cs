using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;

namespace Contoso.KnowledgeMcp.Services;

public sealed class AzureSearchService : ISearchService
{
    private readonly SearchClient _searchClient;
    private readonly ILogger<AzureSearchService> _logger;

    public AzureSearchService(IConfiguration configuration, ILogger<AzureSearchService> logger)
    {
        _logger = logger;

        var endpoint = configuration["Search:Endpoint"]
            ?? throw new InvalidOperationException("Search:Endpoint configuration is required.");
        var indexName = configuration["Search:IndexName"]
            ?? throw new InvalidOperationException("Search:IndexName configuration is required.");

        var tenantId = configuration["AzureAd:TenantId"];
        var credential = string.IsNullOrWhiteSpace(tenantId)
            ? new DefaultAzureCredential()
            : new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId });

        _searchClient = new SearchClient(new Uri(endpoint), indexName, credential);
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int topK = 3,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<SearchResult>();
        }

        var options = new SearchOptions
        {
            Size = Math.Clamp(topK, 1, 10),
            QueryType = SearchQueryType.Semantic,
            SemanticSearch = new SemanticSearchOptions
            {
                SemanticConfigurationName = "default"
            }
        };

        var response = await _searchClient.SearchAsync<SearchDocument>(query, options, ct);
        var results = new List<SearchResult>();

        await foreach (var result in response.Value.GetResultsAsync())
        {
            var document = result.Document;
            var text = GetFieldValue(document, "content", "chunk", "text") ?? string.Empty;
            var source = GetFieldValue(document, "source", "metadata_storage_name", "metadata_storage_path") ?? "unknown";
            var score = result.Score ?? 0d;

            results.Add(new SearchResult(text, source, score));
        }

        _logger.LogInformation("Azure search returned {Count} results for query '{Query}'.", results.Count, query);

        return results;
    }

    private static string? GetFieldValue(SearchDocument document, params string[] fieldNames)
    {
        foreach (var field in fieldNames)
        {
            if (!document.TryGetValue(field, out var value) || value is null)
            {
                continue;
            }

            if (value is string text)
            {
                return text;
            }

            if (value is BinaryData data)
            {
                return data.ToString();
            }
        }

        return null;
    }
}
