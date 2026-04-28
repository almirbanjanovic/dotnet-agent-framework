using System.Net;
using Contoso.CrmMcp.Clients;
using Contoso.CrmMcp.Tools;
using FluentAssertions;
using ModelContextProtocol;

namespace Contoso.CrmMcp.Tests;

public sealed class ProductToolsTests
{
    [Fact]
    public async Task GetProductsAsync_ClientThrows_WrapsInMcpException()
    {
        var (tools, _) = CreateTools("fail", HttpStatusCode.InternalServerError);

        var act = () => tools.GetProductsAsync();

        await act.Should().ThrowAsync<McpException>()
            .WithMessage("Failed to get products.*");
    }

    [Fact]
    public async Task GetProductDetailAsync_ClientThrows_WrapsInMcpException()
    {
        var (tools, _) = CreateTools("fail", HttpStatusCode.InternalServerError);

        var act = () => tools.GetProductDetailAsync("P-9");

        await act.Should().ThrowAsync<McpException>()
            .WithMessage("Failed to get product 'P-9'.*");
    }

    private static (ProductTools Tools, TestHttpMessageHandler Handler) CreateTools(
        string json,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = TestHttpMessageHandler.CreateJson(json, statusCode);
        var client = new CrmApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });
        return (new ProductTools(client), handler);
    }
}
