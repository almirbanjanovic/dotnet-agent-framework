using System.Net;
using Contoso.BffApi.Services;
using FluentAssertions;

namespace Contoso.BffApi.Tests;

public class CrmApiClientTests
{
    [Fact]
    public async Task GetCustomerAsync_ConstructsCorrectUrl()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var crmClient = new CrmApiClient(client);

        await crmClient.GetCustomerAsync("123");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].RequestUri!.PathAndQuery.Should().Be("/api/v1/customers/123");
    }

    [Fact]
    public async Task GetCustomerOrdersAsync_ConstructsCorrectUrl()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var crmClient = new CrmApiClient(client);

        await crmClient.GetCustomerOrdersAsync("123");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].RequestUri!.PathAndQuery.Should().Be("/api/v1/customers/123/orders");
    }

    [Fact]
    public async Task GetHealthAsync_ConstructsCorrectUrl()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var crmClient = new CrmApiClient(client);

        await crmClient.GetHealthAsync();

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].RequestUri!.PathAndQuery.Should().Be("/health");
    }
}
