namespace Contoso.KnowledgeMcp.Services;

public interface ISearchService
{
    Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int topK = 3,
        CancellationToken ct = default);

    // Triggered by `SearchServiceWarmupHostedService` during application
    // start so the very first user-facing `search_knowledge_base` call
    // doesn't block on cold-start embedding work (181 chunks × ~150 ms
    // = up to a minute of latency, which trips the agent framework's
    // tool-call timeout and causes the MCP backend to be retried in a
    // loop). Implementations with no warm-up cost return Task.CompletedTask.
    Task WarmupAsync(CancellationToken ct = default);
}

public record SearchResult(string Text, string Source, double Score);
