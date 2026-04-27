namespace Contoso.KnowledgeMcp.Services;

public interface ISearchService
{
    Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int topK = 3,
        CancellationToken ct = default);
}

public record SearchResult(string Text, string Source, double Score);
