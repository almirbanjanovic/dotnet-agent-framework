using Contoso.KnowledgeMcp.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Contoso.KnowledgeMcp.Tests;

public sealed class InMemorySearchServiceTests
{
    [Fact]
    public async Task SearchAsync_NullQuery_ReturnsEmpty()
    {
        var dataPath = CreateDataPathWithChunks(1);

        try
        {
            var service = CreateService(dataPath);
            var result = await service.SearchAsync(null!);
            result.Should().BeEmpty();
        }
        finally
        {
            CleanupDirectory(dataPath);
        }
    }

    [Fact]
    public async Task SearchAsync_WhitespaceQuery_ReturnsEmpty()
    {
        var dataPath = CreateDataPathWithChunks(1);

        try
        {
            var service = CreateService(dataPath);
            var result = await service.SearchAsync("   ");
            result.Should().BeEmpty();
        }
        finally
        {
            CleanupDirectory(dataPath);
        }
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsEmpty()
    {
        var dataPath = CreateDataPathWithChunks(1);

        try
        {
            var service = CreateService(dataPath);
            var result = await service.SearchAsync(string.Empty);
            result.Should().BeEmpty();
        }
        finally
        {
            CleanupDirectory(dataPath);
        }
    }

    [Fact]
    public async Task SearchAsync_TopKZero_ClampsToOne()
    {
        var dataPath = CreateDataPathWithChunks(11);

        try
        {
            var service = CreateService(dataPath);
            var results = await service.SearchAsync("query", topK: 0);
            results.Should().HaveCount(1);
        }
        finally
        {
            CleanupDirectory(dataPath);
        }
    }

    [Fact]
    public async Task SearchAsync_TopKHundred_ClampsToTen()
    {
        var dataPath = CreateDataPathWithChunks(11);

        try
        {
            var service = CreateService(dataPath);
            var results = await service.SearchAsync("query", topK: 100);
            results.Should().HaveCount(10);
        }
        finally
        {
            CleanupDirectory(dataPath);
        }
    }

    [Fact]
    public async Task SearchAsync_DefaultTopK_ReturnsThree()
    {
        var dataPath = CreateDataPathWithChunks(11);

        try
        {
            var service = CreateService(dataPath);
            var results = await service.SearchAsync("query");
            results.Should().HaveCount(3);
        }
        finally
        {
            CleanupDirectory(dataPath);
        }
    }

    [Fact]
    public void CosineSimilarity_IdenticalVectors_ReturnsOne()
    {
        var score = InMemorySearchService.CosineSimilarity(new float[] { 1, 0 }, new float[] { 1, 0 });
        score.Should().BeApproximately(1, 0.0001);
    }

    [Fact]
    public void CosineSimilarity_OrthogonalVectors_ReturnsZero()
    {
        var score = InMemorySearchService.CosineSimilarity(new float[] { 1, 0 }, new float[] { 0, 1 });
        score.Should().BeApproximately(0, 0.0001);
    }

    [Fact]
    public void CosineSimilarity_ZeroVector_ReturnsZero()
    {
        var score = InMemorySearchService.CosineSimilarity(new float[] { 0, 0 }, new float[] { 1, 0 });
        score.Should().BeApproximately(0, 0.0001);
    }

    [Fact]
    public void ChunkDocument_SplitsOnDoubleNewline()
    {
        var first = new string('a', 25);
        var second = new string('b', 25);
        var content = $"{first}\n\n{second}";

        var chunks = InMemorySearchService.ChunkDocument(content).ToList();

        chunks.Should().HaveCount(2);
        chunks[0].Should().Be(first);
        chunks[1].Should().Be(second);
    }

    [Fact]
    public void ChunkDocument_FiltersShortChunks()
    {
        var shortChunk = "short";
        var longChunk = new string('c', 25);
        var content = $"{shortChunk}\n\n{longChunk}";

        var chunks = InMemorySearchService.ChunkDocument(content).ToList();

        chunks.Should().ContainSingle().Which.Should().Be(longChunk);
    }

    [Fact]
    public void ResolveDataPath_WalksUpDirectories()
    {
        var original = Directory.GetCurrentDirectory();
        var root = Path.Combine(original, "resolve-data-path", Guid.NewGuid().ToString("N"));
        var parent = Path.Combine(root, "parent");
        var child = Path.Combine(parent, "child");
        var dataPath = Path.Combine(parent, "data", "contoso-sharepoint");

        Directory.CreateDirectory(child);
        Directory.CreateDirectory(dataPath);

        try
        {
            Directory.SetCurrentDirectory(child);
            var resolved = InMemorySearchService.ResolveDataPath();

            resolved.Should().Be(Path.GetFullPath(dataPath));
        }
        finally
        {
            Directory.SetCurrentDirectory(original);
            CleanupDirectory(root);
        }
    }

    [Fact]
    public async Task WarmupAsync_EmbedsAllChunksOnce()
    {
        var dataPath = CreateDataPathWithChunks(50);
        var embedCount = 0;

        try
        {
            var service = new InMemorySearchService(
                (text, _) =>
                {
                    Interlocked.Increment(ref embedCount);
                    return Task.FromResult(new float[] { text.Length });
                },
                NullLogger<InMemorySearchService>.Instance,
                dataPath);

            await service.WarmupAsync();
            var embedsAfterWarmup = embedCount;

            // First search after warm-up only embeds the QUERY (single
            // call); the 50 chunk embeddings must already be cached.
            await service.SearchAsync("anything");

            embedsAfterWarmup.Should().Be(50, "warm-up should embed every chunk exactly once");
            (embedCount - embedsAfterWarmup).Should().Be(1, "subsequent searches should only embed the query");
        }
        finally
        {
            CleanupDirectory(dataPath);
        }
    }

    [Fact]
    public async Task SearchAsync_DoesNotRestartInitWhenRequestCancelled()
    {
        // Reproduces the "knowledge-mcp keeps getting called over and over"
        // loop. Before the fix the request CT was passed straight into
        // LoadDocumentsAsync, so a request-scoped cancel would abort the
        // partially-built embedding cache and force every retry to restart
        // from chunk 0. Now: a cancelled request must not corrupt the
        // singleton cache for the next caller.
        //
        // The fake embedding generator OBSERVES its CancellationToken so
        // the test would have failed against the pre-fix code (where the
        // request CT was forwarded into LoadDocumentsAsync — every chunk
        // call would have thrown OCE, _initialized stays false, the
        // second caller restarts the embedding storm).
        var dataPath = CreateDataPathWithChunks(20);
        var embedCount = 0;
        var firstCallStarted = new TaskCompletionSource();

        try
        {
            var service = new InMemorySearchService(
                async (text, ct) =>
                {
                    if (Interlocked.Increment(ref embedCount) == 1)
                    {
                        firstCallStarted.SetResult();
                    }
                    await Task.Yield();
                    ct.ThrowIfCancellationRequested();
                    return new float[] { text.Length };
                },
                NullLogger<InMemorySearchService>.Instance,
                dataPath);

            using var cts = new CancellationTokenSource();
            var firstCall = service.SearchAsync("query A", ct: cts.Token);
            await firstCallStarted.Task;
            cts.Cancel();

            try { await firstCall; } catch (OperationCanceledException) { /* tolerated */ }

            // Second call (with a fresh CT) must find the cache fully
            // populated by the in-progress init that completed in the
            // background — no re-embedding of the 20 chunks. Embed budget
            // for the second call is exactly 1 (the query).
            var snapshot = embedCount;
            var results = await service.SearchAsync("query B");

            results.Should().NotBeEmpty();
            (embedCount - snapshot).Should().Be(1,
                "the second call should only embed its query, not re-embed the 20 chunks");
        }
        finally
        {
            CleanupDirectory(dataPath);
        }
    }

    [Fact]
    public async Task LoadDocuments_PartialFailureDoesNotDuplicateChunks()
    {
        // Adversarial-review regression test: if LoadDocumentsAsync fails
        // mid-embedding (e.g. a transient Azure OpenAI 429), the next
        // caller must NOT see double-counted chunks in the cache. Before
        // the fix the loader appended directly to `_chunks`, so a retry
        // after a partial success doubled-up entries (skewing scores and
        // returning identical passages multiple times in topK).
        var dataPath = CreateDataPathWithChunks(20);
        var embedCount = 0;
        var failOnce = true;

        try
        {
            var service = new InMemorySearchService(
                (text, _) =>
                {
                    var n = Interlocked.Increment(ref embedCount);
                    // Throw on the 5th chunk during the FIRST attempt only.
                    if (failOnce && n == 5)
                    {
                        failOnce = false;
                        throw new InvalidOperationException("simulated transient embedding failure");
                    }
                    return Task.FromResult(new float[] { text.Length });
                },
                NullLogger<InMemorySearchService>.Instance,
                dataPath);

            try { await service.WarmupAsync(); } catch (InvalidOperationException) { /* expected */ }

            // Second attempt should succeed and end up with EXACTLY 20
            // chunks in the cache — not 24 (4 from first attempt + 20).
            await service.WarmupAsync();
            var results = await service.SearchAsync("anything", topK: 10);

            // Top 10 results must all reference distinct chunks (no
            // duplicates from the failed first attempt).
            results.Should().HaveCount(10);
            results.Select(r => r.Text).Distinct().Should().HaveCount(10);
        }
        finally
        {
            CleanupDirectory(dataPath);
        }
    }

    private static InMemorySearchService CreateService(string dataPath)
    {
        return new InMemorySearchService(
            (text, _) => Task.FromResult(new float[] { text.Length }),
            NullLogger<InMemorySearchService>.Instance,
            dataPath);
    }

    private static string CreateDataPathWithChunks(int chunkCount)
    {
        var root = Path.Combine(Directory.GetCurrentDirectory(), "test-data", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var filePath = Path.Combine(root, "doc.txt");
        var chunks = Enumerable.Range(1, chunkCount)
            .Select(i => $"Chunk {i} content for testing data {i}.");

        File.WriteAllText(filePath, string.Join("\n\n", chunks));
        return root;
    }

    private static void CleanupDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }
}
