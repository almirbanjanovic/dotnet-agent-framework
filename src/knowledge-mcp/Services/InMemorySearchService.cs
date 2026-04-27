using Azure;
using Azure.AI.OpenAI;
using OpenAI.Embeddings;

namespace Contoso.KnowledgeMcp.Services;

public sealed class InMemorySearchService : ISearchService
{
    private readonly EmbeddingClient _embeddingClient;
    private readonly ILogger<InMemorySearchService> _logger;
    private readonly string _dataPath;
    private readonly List<StoredChunk> _chunks = [];
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public InMemorySearchService(IConfiguration configuration, ILogger<InMemorySearchService> logger)
    {
        _logger = logger;

        var endpoint = configuration["Foundry:Endpoint"]
            ?? throw new InvalidOperationException("Foundry:Endpoint configuration is required.");
        var deploymentName = configuration["Foundry:EmbeddingDeploymentName"]
            ?? throw new InvalidOperationException("Foundry:EmbeddingDeploymentName configuration is required.");
        var apiKey = configuration["Foundry:ApiKey"]
            ?? throw new InvalidOperationException("Foundry:ApiKey configuration is required for InMemory mode.");

        var client = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
        _embeddingClient = client.GetEmbeddingClient(deploymentName);
        _dataPath = ResolveDataPath();
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

        await EnsureInitializedAsync(ct);

        var queryVector = await GenerateEmbeddingAsync(query, ct);
        var normalizedTopK = Math.Clamp(topK, 1, 10);

        var results = _chunks
            .Select(chunk => new
            {
                Chunk = chunk,
                Score = CosineSimilarity(queryVector, chunk.Vector)
            })
            .OrderByDescending(item => item.Score)
            .Take(normalizedTopK)
            .Select(item => new SearchResult(item.Chunk.Text, item.Chunk.Source, item.Score))
            .ToList();

        return results;
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized)
        {
            return;
        }

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized)
            {
                return;
            }

            await LoadDocumentsAsync(ct);
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task LoadDocumentsAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_dataPath))
        {
            throw new DirectoryNotFoundException($"Knowledge base folder not found at '{_dataPath}'.");
        }

        var files = Directory.EnumerateFiles(_dataPath, "*.txt", SearchOption.AllDirectories).ToList();
        _logger.LogInformation("Loading knowledge base from {Path} ({Count} files).", _dataPath, files.Count);

        foreach (var file in files)
        {
            var source = Path.GetRelativePath(_dataPath, file);
            var content = await File.ReadAllTextAsync(file, ct);
            foreach (var chunk in ChunkDocument(content))
            {
                var vector = await GenerateEmbeddingAsync(chunk, ct);
                _chunks.Add(new StoredChunk(chunk, vector, source));
            }
        }

        _logger.LogInformation("Embedded {Count} knowledge chunks.", _chunks.Count);
    }

    private IEnumerable<string> ChunkDocument(string content)
    {
        var normalized = content.Replace("\r\n", "\n");
        var chunks = normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var chunk in chunks)
        {
            if (chunk.Length < 20)
            {
                continue;
            }

            yield return chunk;
        }
    }

    private async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct)
    {
        var response = await _embeddingClient.GenerateEmbeddingAsync(text, cancellationToken: ct);
        return response.Value.ToFloats().Span.ToArray();
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        var length = Math.Min(a.Length, b.Length);
        double dot = 0;
        double normA = 0;
        double normB = 0;

        for (var i = 0; i < length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA == 0 || normB == 0)
        {
            return 0;
        }

        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    private static string ResolveDataPath()
    {
        var contentRoot = Directory.GetCurrentDirectory();
        var candidates = new[]
        {
            Path.Combine(contentRoot, "data", "contoso-sharepoint"),
            Path.Combine(contentRoot, "..", "data", "contoso-sharepoint"),
            Path.Combine(contentRoot, "..", "..", "data", "contoso-sharepoint"),
            Path.Combine(contentRoot, "..", "..", "..", "data", "contoso-sharepoint")
        };

        foreach (var candidate in candidates)
        {
            var fullPath = Path.GetFullPath(candidate);
            if (Directory.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return Path.GetFullPath(candidates.Last());
    }

    private sealed record StoredChunk(string Text, float[] Vector, string Source);
}
