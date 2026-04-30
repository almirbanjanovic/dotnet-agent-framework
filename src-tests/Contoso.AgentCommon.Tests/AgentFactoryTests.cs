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
    public void CrmAgentFactory_UsesAIProjectClientWithDefaultAzureCredential()
    {
        var factory = new CrmAgent::CrmAgentFactory(
            CreateConfiguration(),
            NullLoggerFactory.Instance,
            new ServiceCollection().BuildServiceProvider());

        // The factory always builds an AIProjectClient (DefaultAzureCredential).
        // No API-key path exists anymore — agents are keyless end-to-end.
        GetField(factory, "_projectClient").Should().NotBeNull();
    }

    [Fact]
    public void CrmAgentFactory_TenantIdConfigured_StillBuildsProjectClient()
    {
        var factory = new CrmAgent::CrmAgentFactory(
            CreateConfiguration(tenantId: "tenant-1"),
            NullLoggerFactory.Instance,
            new ServiceCollection().BuildServiceProvider());

        GetField(factory, "_projectClient").Should().NotBeNull();
    }

    [Fact]
    public void ProductAgentFactory_UsesAIProjectClientWithDefaultAzureCredential()
    {
        var factory = new ProductAgentFactory(
            CreateConfiguration(),
            NullLoggerFactory.Instance,
            new ServiceCollection().BuildServiceProvider());

        GetField(factory, "_projectClient").Should().NotBeNull();
    }

    [Fact]
    public void ProductAgentFactory_TenantIdConfigured_StillBuildsProjectClient()
    {
        var factory = new ProductAgentFactory(
            CreateConfiguration(tenantId: "tenant-1"),
            NullLoggerFactory.Instance,
            new ServiceCollection().BuildServiceProvider());

        GetField(factory, "_projectClient").Should().NotBeNull();
    }

    [Fact]
    public void CrmAgentFactory_CreateAgent_ReturnsAgent()
    {
        var factory = new CrmAgent::CrmAgentFactory(
            CreateConfiguration(),
            NullLoggerFactory.Instance,
            new ServiceCollection().BuildServiceProvider());

        var agent = factory.CreateAgent("prompt", new List<Microsoft.Extensions.AI.AITool>());

        agent.Should().NotBeNull();
    }

    [Fact]
    public void ProductAgentFactory_CreateAgent_ReturnsAgent()
    {
        var factory = new ProductAgentFactory(
            CreateConfiguration(),
            NullLoggerFactory.Instance,
            new ServiceCollection().BuildServiceProvider());

        var agent = factory.CreateAgent("prompt", new List<Microsoft.Extensions.AI.AITool>());

        agent.Should().NotBeNull();
    }

    [Fact]
    public void CrmAgentFactory_MissingDeploymentName_Throws()
    {
        var values = new Dictionary<string, string?>
        {
            ["Foundry:Endpoint"] = "https://example.com"
        };

        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var act = () => new CrmAgent::CrmAgentFactory(
            config,
            NullLoggerFactory.Instance,
            new ServiceCollection().BuildServiceProvider());

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*Foundry:DeploymentName*");
    }

    [Fact]
    public void CrmAgentFactory_MissingEndpoint_Throws()
    {
        var values = new Dictionary<string, string?>
        {
            ["Foundry:DeploymentName"] = "deployment"
        };

        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var act = () => new CrmAgent::CrmAgentFactory(
            config,
            NullLoggerFactory.Instance,
            new ServiceCollection().BuildServiceProvider());

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*Foundry:Endpoint*");
    }

    private static IConfiguration CreateConfiguration(string? tenantId = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Foundry:DeploymentName"] = "deployment",
            ["Foundry:Endpoint"] = "https://example.com",
            ["AzureAd:TenantId"] = tenantId
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
