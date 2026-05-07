using Azure.AI.OpenAI;
using Azure.AI.Projects;
using Azure.Core;
using Azure.Identity;
using OpenAI.Embeddings;

namespace Contoso.KnowledgeMcp.Services;

public sealed class InMemorySearchService : ISearchService
{
    private readonly EmbeddingClient _embeddingClient;
    private readonly Func<string, CancellationToken, Task<float[]>>? _embeddingGenerator;
    private readonly ILogger<InMemorySearchService> _logger;
    private readonly string _dataPath;
    private readonly List<StoredChunk> _chunks = [];
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public InMemorySearchService(IConfiguration configuration, ILogger<InMemorySearchService> logger)
    {
        _logger = logger;

        // Single Foundry endpoint exposed by infra: the project endpoint
        // (https://<account>.services.ai.azure.com/api/projects/<project>).
        // We never read a separate "account endpoint" — the project's
        // connection-discovery API is the canonical way to obtain an
        // AzureOpenAIClient under the new Foundry experience.
        var projectEndpoint = configuration["Foundry:ProjectEndpoint"]
            ?? throw new InvalidOperationException("Foundry:ProjectEndpoint configuration is required.");
        var deploymentName = configuration["Foundry:EmbeddingDeploymentName"]
            ?? throw new InvalidOperationException("Foundry:EmbeddingDeploymentName configuration is required.");

        // Always authenticate via DefaultAzureCredential — no API keys.
        // The deployer is granted "Azure AI User" on the project (which
        // includes Cognitive Services OpenAI User on the underlying account)
        // by Terraform; AKS workload identity provides the same role in prod.
        var tenantId = configuration["AzureAd:TenantId"];
        TokenCredential credential = string.IsNullOrWhiteSpace(tenantId)
            ? new DefaultAzureCredential()
            : new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId });

        // Microsoft's recommended pattern for the new Foundry experience:
        //   1. Build an AIProjectClient against the project endpoint.
        //   2. Ask it for the AzureOpenAIClient connection.
        //   3. Reduce the connection's locator to its host so we hit the
        //      account's data plane (https://<account>.openai.azure.com).
        //   4. Construct an AzureOpenAIClient against that URI and pull
        //      the EmbeddingClient for our deployment.
        var projectClient = new AIProjectClient(new Uri(projectEndpoint), credential);
        var connection = projectClient.GetConnection(typeof(AzureOpenAIClient).FullName!);
        if (!connection.TryGetLocatorAsUri(out Uri? openAiUri) || openAiUri is null)
        {
            throw new InvalidOperationException(
                "Could not resolve the Azure OpenAI connection URI from the Foundry project. " +
                "Ensure the project has a default Azure OpenAI connection.");
        }
        openAiUri = new Uri($"https://{openAiUri.Host}");

        var azureOpenAIClient = new AzureOpenAIClient(openAiUri, credential);
        _embeddingClient = azureOpenAIClient.GetEmbeddingClient(deploymentName);
        _dataPath = ResolveDataPath();
    }

    internal InMemorySearchService(
        Func<string, CancellationToken, Task<float[]>> embeddingGenerator,
        ILogger<InMemorySearchService> logger,
        string dataPath)
    {
        _embeddingGenerator = embeddingGenerator ?? throw new ArgumentNullException(nameof(embeddingGenerator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataPath = dataPath ?? throw new ArgumentNullException(nameof(dataPath));
        _embeddingClient = null!;
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

    internal static IEnumerable<string> ChunkDocument(string content)
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
        if (_embeddingGenerator is not null)
        {
            return await _embeddingGenerator(text, ct);
        }

        var response = await _embeddingClient.GenerateEmbeddingAsync(text, cancellationToken: ct);
        return response.Value.ToFloats().Span.ToArray();
    }

    internal static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        // Embeddings from a single model are always the same dimension. If
        // that contract is broken (mixed models, partial vector), refuse to
        // compare — silently scoring on Min(a, b) and dividing by
        // partial-length norms is mathematically wrong and produces
        // misleadingly high scores.
        if (a.Length != b.Length)
        {
            return 0;
        }

        double dot = 0;
        double normA = 0;
        double normB = 0;

        for (var i = 0; i < a.Length; i++)
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

    internal static string ResolveDataPath()
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
