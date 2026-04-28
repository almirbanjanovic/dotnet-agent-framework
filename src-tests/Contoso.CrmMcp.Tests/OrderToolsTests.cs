using System.Net;
using Contoso.CrmMcp.Clients;
using Contoso.CrmMcp.Models;
using System.Text.Json;
using Contoso.CrmMcp.Tools;
using FluentAssertions;
using ModelContextProtocol;

namespace Contoso.CrmMcp.Tests;

public sealed class OrderToolsTests
{
    [Fact]
    public async Task GetCustomerOrdersAsync_Success_ReturnsSerialized()
    {
        var orders = new List<Order>
        {
            new() { Id = "O-1", CustomerId = "C-1", Status = "Shipped" }
        };
        var (tools, handler) = CreateTools(System.Text.Json.JsonSerializer.Serialize(orders));

        var response = await tools.GetCustomerOrdersAsync("C-1");

        var payload = JsonDocument.Parse(response);
        payload.RootElement.GetArrayLength().Should().Be(1);
        payload.RootElement[0].GetProperty("id").GetString().Should().Be("O-1");
        handler.Request!.RequestUri!.PathAndQuery.Should().Be("/api/v1/customers/C-1/orders");
    }

    [Fact]
    public async Task GetOrderDetailAsync_ClientThrows_WrapsInMcpException()
    {
        var (tools, _) = CreateTools("fail", HttpStatusCode.InternalServerError);

        var act = () => tools.GetOrderDetailAsync("O-2");

        await act.Should().ThrowAsync<McpException>()
            .WithMessage("Failed to get order 'O-2'.*");
    }

    [Fact]
    public async Task GetOrderItemsAsync_ClientThrows_WrapsInMcpException()
    {
        var (tools, _) = CreateTools("fail", HttpStatusCode.InternalServerError);

        var act = () => tools.GetOrderItemsAsync("O-3");

        await act.Should().ThrowAsync<McpException>()
            .WithMessage("Failed to get order items for 'O-3'.*");
    }

    private static (OrderTools Tools, TestHttpMessageHandler Handler) CreateTools(
        string json,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = TestHttpMessageHandler.CreateJson(json, statusCode);
        var client = new CrmApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });
        return (new OrderTools(client), handler);
    }
}
