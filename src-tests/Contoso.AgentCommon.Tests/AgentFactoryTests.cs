extern alias CrmAgent;

using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Contoso.AgentCommon.Tests;

public sealed class AgentFactoryTests
{
    [Fact]
    public void CrmAgentFactory_ApiKeyPresent_UsesAzureOpenAIClient()
    {
        var factory = new CrmAgent::CrmAgentFactory(
            CreateConfiguration("api-key"),
            NullLoggerFactory.Instance,
            new ServiceCollection().BuildServiceProvider());

        GetField(factory, "_chatClient").Should().NotBeNull();
        GetField(factory, "_projectClient").Should().BeNull();
    }

    [Fact]
    public void CrmAgentFactory_NoApiKey_UsesAIProjectClient()
    {
        var factory = new CrmAgent::CrmAgentFactory(
            CreateConfiguration(null),
            NullLoggerFactory.Instance,
            new ServiceCollection().BuildServiceProvider());

        GetField(factory, "_chatClient").Should().BeNull();
        GetField(factory, "_projectClient").Should().NotBeNull();
    }

    [Fact]
    public void ProductAgentFactory_ApiKeyPresent_UsesAzureOpenAIClient()
    {
        var factory = new ProductAgentFactory(
            CreateConfiguration("api-key"),
            NullLoggerFactory.Instance,
            new ServiceCollection().BuildServiceProvider());

        GetField(factory, "_chatClient").Should().NotBeNull();
        GetField(factory, "_projectClient").Should().BeNull();
    }

    [Fact]
    public void ProductAgentFactory_NoApiKey_UsesAIProjectClient()
    {
        var factory = new ProductAgentFactory(
            CreateConfiguration(null),
            NullLoggerFactory.Instance,
            new ServiceCollection().BuildServiceProvider());

        GetField(factory, "_chatClient").Should().BeNull();
        GetField(factory, "_projectClient").Should().NotBeNull();
    }

    [Fact]
    public void CrmAgentFactory_CreateAgent_ReturnsAgent()
    {
        var factory = new CrmAgent::CrmAgentFactory(
            CreateConfiguration("api-key"),
            NullLoggerFactory.Instance,
            new ServiceCollection().BuildServiceProvider());

        var agent = factory.CreateAgent("prompt", new List<Microsoft.Extensions.AI.AITool>());

        agent.Should().NotBeNull();
    }

    private static IConfiguration CreateConfiguration(string? apiKey)
    {
        var values = new Dictionary<string, string?>
        {
            ["Foundry:DeploymentName"] = "deployment",
            ["Foundry:Endpoint"] = "https://example.com",
            ["Foundry:ApiKey"] = apiKey
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static object? GetField(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(instance);
    }
}
