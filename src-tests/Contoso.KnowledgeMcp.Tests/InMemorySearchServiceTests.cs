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
