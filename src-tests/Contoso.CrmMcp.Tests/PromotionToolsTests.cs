using System.Net;
using Contoso.CrmMcp.Clients;
using Contoso.CrmMcp.Tools;
using FluentAssertions;
using ModelContextProtocol;

namespace Contoso.CrmMcp.Tests;

public sealed class PromotionToolsTests
{
    [Fact]
    public async Task GetPromotionsAsync_ClientThrows_WrapsInMcpException()
    {
        var (tools, _) = CreateTools("fail", HttpStatusCode.InternalServerError);

        var act = () => tools.GetPromotionsAsync();

        await act.Should().ThrowAsync<McpException>()
            .WithMessage("Failed to list promotions.*");
    }

    [Fact]
    public async Task GetEligiblePromotionsAsync_ClientThrows_WrapsInMcpException()
    {
        var (tools, _) = CreateTools("fail", HttpStatusCode.InternalServerError);

        var act = () => tools.GetEligiblePromotionsAsync("C-10");

        await act.Should().ThrowAsync<McpException>()
            .WithMessage("Failed to get eligible promotions for 'C-10'.*");
    }

    private static (PromotionTools Tools, TestHttpMessageHandler Handler) CreateTools(
        string json,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = TestHttpMessageHandler.CreateJson(json, statusCode);
        var client = new CrmApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });
        return (new PromotionTools(client), handler);
    }
}
