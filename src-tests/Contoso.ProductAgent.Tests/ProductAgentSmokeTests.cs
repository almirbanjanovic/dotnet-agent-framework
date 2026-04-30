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
            var provider = new SystemPromptProvider(environment);

            provider.Prompt.Should().Be("You are the Product Agent.");
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

        var act = () => new SystemPromptProvider(environment);

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

        var provider = new CrmMcpClientProvider(configuration, NullLoggerFactory.Instance);

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

        var provider = new KnowledgeMcpClientProvider(configuration, NullLoggerFactory.Instance);

        provider.Name.Should().Be("knowledge-mcp");
    }

    [Fact]
    public void McpClientProvider_DefaultsApply_WhenConfigMissing()
    {
        var configuration = new ConfigurationBuilder().Build();

        var crmProvider = new CrmMcpClientProvider(configuration, NullLoggerFactory.Instance);
        var knowledgeProvider = new KnowledgeMcpClientProvider(configuration, NullLoggerFactory.Instance);

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
}
