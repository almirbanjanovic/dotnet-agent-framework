using System.Net;
using Contoso.CrmMcp.Clients;
using Contoso.CrmMcp.Models;
using System.Text.Json;
using Contoso.CrmMcp.Tools;
using FluentAssertions;
using ModelContextProtocol;

namespace Contoso.CrmMcp.Tests;

public sealed class CustomerToolsTests
{
    [Fact]
    public async Task GetCustomerDetailAsync_Success_ReturnsSerializedJson()
    {
        var customer = new Customer { Id = "C-42", FirstName = "Grace", LastName = "Hopper" };
        var (tools, handler) = CreateTools(System.Text.Json.JsonSerializer.Serialize(customer));

        var response = await tools.GetCustomerDetailAsync("C-42");

        var payload = JsonDocument.Parse(response);
        payload.RootElement.GetProperty("id").GetString().Should().Be("C-42");
        handler.Request!.RequestUri!.PathAndQuery.Should().Be("/api/v1/customers/C-42");
    }

    [Fact]
    public async Task GetCustomerDetailAsync_ClientThrows_WrapsInMcpException()
    {
        var (tools, _) = CreateTools("fail", HttpStatusCode.InternalServerError);

        var act = () => tools.GetCustomerDetailAsync("C-42");

        await act.Should().ThrowAsync<McpException>()
            .WithMessage("Failed to get customer 'C-42'.*");
    }

    private static (CustomerTools Tools, TestHttpMessageHandler Handler) CreateTools(
        string json,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = TestHttpMessageHandler.CreateJson(json, statusCode);
        var client = new CrmApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });
        return (new CustomerTools(client), handler);
    }
}
