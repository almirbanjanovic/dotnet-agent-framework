using FluentAssertions;
using Contoso.ProductAgent.Endpoints;
using Microsoft.Extensions.AI;

namespace Contoso.ProductAgent.Tests;

public sealed class ProductAgentFactoryTests
{
    [Theory]
    [InlineData("guest-smoke", false)]
    [InlineData("guest-", false)]
    [InlineData("101", true)]
    [InlineData("cust-1", true)]
    [InlineData(null, true)]
    public void ChatEndpoint_ShouldIncludeToolbox_SuppressesGuestTraffic(string? customerId, bool expected)
    {
        ChatEndpoint.ShouldIncludeToolbox(customerId).Should().Be(expected);
    }

    [Fact]
    public void MergeTools_AddsToolbox_WhenEnabledAndConfigured()
    {
        var mcpTool = CreateTool("mcp_tool");
        var toolboxTool = CreateTool("toolbox_tool");

        var merged = ProductAgentFactory.MergeTools([mcpTool], toolboxTool, includeToolbox: true);

        merged.Should().ContainInOrder(mcpTool, toolboxTool);
    }

    [Fact]
    public void MergeTools_SuppressesToolbox_WhenDisabledForGuest()
    {
        var mcpTool = CreateTool("mcp_tool");
        var toolboxTool = CreateTool("toolbox_tool");

        var merged = ProductAgentFactory.MergeTools([mcpTool], toolboxTool, includeToolbox: false);

        merged.Should().ContainSingle().Which.Should().BeSameAs(mcpTool);
    }

    [Fact]
    public void MergeTools_KeepsMcpToolsOnly_WhenToolboxNotConfigured()
    {
        var mcpTool = CreateTool("mcp_tool");

        var merged = ProductAgentFactory.MergeTools([mcpTool], toolboxTool: null, includeToolbox: true);

        merged.Should().ContainSingle().Which.Should().BeSameAs(mcpTool);
    }

    private static AITool CreateTool(string name) =>
        AIFunctionFactory.Create(() => "ok", name: name, description: $"{name} test tool");
}