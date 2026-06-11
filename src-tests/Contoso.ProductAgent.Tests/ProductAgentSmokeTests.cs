using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Contoso.ProductAgent.Tests;

public sealed class ProductAgentSmokeTests
{
    [Fact]
    public void SystemPromptProvider_LoadsPromptFromContentRoot()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), $"product-agent-tests-{Guid.NewGuid():N}");
        var promptDir = Path.Combine(contentRoot, "Prompts");
        Directory.CreateDirectory(promptDir);

        var promptPath = Path.Combine(promptDir, "system-prompt.md");
        File.WriteAllText(promptPath, "You are the Product Agent.");

        try
        {
            var environment = new TestHostEnvironment(contentRoot);
            // Fixed clock so the date-stamp assertion is deterministic.
            var time = new FixedTimeProvider(new DateTimeOffset(2026, 6, 11, 14, 0, 0, TimeSpan.Zero));
            var provider = new SystemPromptProvider(environment, time);

            provider.Prompt.Should().StartWith("Today's date is 2026-06-11 (UTC).");
            provider.Prompt.Should().EndWith("You are the Product Agent.");
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    [Fact]
    public void SystemPromptProvider_MissingFile_Throws()
    {
        var environment = new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        var act = () => new SystemPromptProvider(environment, TimeProvider.System);

        // Either the prompts directory or the prompt file is missing.
        act.Should().Throw<IOException>(because: "the prompt file does not exist");
    }

    [Fact]
    public void CrmMcpClientProvider_UsesConfiguredBaseUrl()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CrmMcp:BaseUrl"] = "http://crm-mcp.contoso.svc.cluster.local"
            })
            .Build();

        var provider = new CrmMcpClientProvider(
            configuration,
            new StubHttpClientFactory(),
            NullLoggerFactory.Instance);

        provider.Name.Should().Be("crm-mcp");
    }

    [Fact]
    public void KnowledgeMcpClientProvider_UsesConfiguredBaseUrl()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["KnowledgeMcp:BaseUrl"] = "http://knowledge-mcp.contoso.svc.cluster.local"
            })
            .Build();

        var provider = new KnowledgeMcpClientProvider(
            configuration,
            new StubHttpClientFactory(),
            NullLoggerFactory.Instance);

        provider.Name.Should().Be("knowledge-mcp");
    }

    [Fact]
    public void McpClientProvider_DefaultsApply_WhenConfigMissing()
    {
        var configuration = new ConfigurationBuilder().Build();

        var httpClientFactory = new StubHttpClientFactory();
        var crmProvider = new CrmMcpClientProvider(configuration, httpClientFactory, NullLoggerFactory.Instance);
        var knowledgeProvider = new KnowledgeMcpClientProvider(configuration, httpClientFactory, NullLoggerFactory.Instance);

        crmProvider.Name.Should().Be("crm-mcp");
        knowledgeProvider.Name.Should().Be("knowledge-mcp");
    }

    [Fact]
    public void ChatRequest_RecordEquality_WorksByValue()
    {
        var a = new ChatRequest("101", "hello");
        var b = new ChatRequest("101", "hello");

        a.Should().Be(b);
    }

    [Fact]
    public void ChatResponse_PreservesToolCalls()
    {
        var toolCall = new ToolCallInfo("get_products", new Dictionary<string, object?>
        {
            ["category"] = "tents"
        });
        var response = new ChatResponse("Here are some tents.", new[] { toolCall });

        response.Response.Should().Be("Here are some tents.");
        response.ToolCalls.Should().HaveCount(1);
        response.ToolCalls[0].Name.Should().Be("get_products");
        response.ToolCalls[0].Arguments["category"].Should().Be("tents");
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string contentRoot)
        {
            ContentRootPath = contentRoot;
            ApplicationName = "Contoso.ProductAgent";
            EnvironmentName = "Test";
            ContentRootFileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
                Directory.Exists(contentRoot) ? contentRoot : Path.GetTempPath());
        }

        public string ApplicationName { get; set; }
        public string EnvironmentName { get; set; }
        public string ContentRootPath { get; set; }
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
    }

    // The smoke tests only construct the providers and read .Name — they
    // never let CreateClientAsync run — so the factory is never asked for
    // a real HttpClient. A throwing stub keeps test memory tiny and makes
    // misuse loud.
    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => throw new InvalidOperationException(
                $"StubHttpClientFactory.CreateClient('{name}') was called; "
                + "the smoke test should never trigger an MCP connection.");
    }
}
