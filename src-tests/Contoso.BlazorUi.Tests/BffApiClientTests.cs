using System.Net;
using System.Text.Json;
using Contoso.BlazorUi.Models;
using Contoso.BlazorUi.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace Contoso.BlazorUi.Tests;

public class BffApiClientTests
{
    [Fact]
    public async Task SendChatAsync_AddsCustomerHeader()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.OkJson(ChatResponseJson()));
        var client = CreateClient(handler, customerId: "cust-42");

        await client.SendChatAsync(new ChatRequest("hello", "conv-1"));

        handler.Requests.Should().ContainSingle();
        var request = handler.Requests[0];
        request.Headers.TryGetValues("X-Customer-Id", out var values).Should().BeTrue();
        values!.Should().ContainSingle("cust-42");
    }

    [Fact]
    public async Task GetCustomerAsync_UsesCorrectUrl()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.OkJson(CustomerJson()));
        var client = CreateClient(handler, customerId: "cust-1");

        await client.GetCustomerAsync("cust-1");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].RequestUri!.PathAndQuery.Should().Be("/api/v1/customers/cust-1");
    }

    [Fact]
    public async Task GetOrdersAsync_UsesCorrectUrl()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.OkJson("[]"));
        var client = CreateClient(handler, customerId: "cust-1");

        await client.GetOrdersAsync("cust-1");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].RequestUri!.PathAndQuery.Should().Be("/api/v1/customers/cust-1/orders");
    }

    [Fact]
    public async Task SendChatAsync_PostsToChatEndpoint()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.OkJson(ChatResponseJson()));
        var client = CreateClient(handler, customerId: "cust-1");

        await client.SendChatAsync(new ChatRequest("hello", "conv-1"));

        handler.Requests.Should().ContainSingle();
        var request = handler.Requests[0];
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.PathAndQuery.Should().Be("/api/v1/chat");

        var payload = handler.RequestBodies[0];
        payload.Should().NotBeNull();
        using var document = JsonDocument.Parse(payload!);
        document.RootElement.GetProperty("Message").GetString().Should().Be("hello");
        document.RootElement.GetProperty("ConversationId").GetString().Should().Be("conv-1");
    }

    [Fact]
    public async Task SendChatAsync_ErrorResponse_Throws()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = CreateClient(handler, customerId: "cust-1");

        var action = () => client.SendChatAsync(new ChatRequest("hello", "conv-1"));

        await action.Should().ThrowAsync<HttpRequestException>();
    }

    private static BffApiClient CreateClient(StubHttpMessageHandler handler, string? customerId)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var authStateProvider = CreateAuthStateProvider(customerId);
        return new BffApiClient(httpClient, authStateProvider);
    }

    private static AuthStateProvider CreateAuthStateProvider(string? customerId)
    {
        var configuration = new ConfigurationBuilder().Build();
        var environment = new TestWebAssemblyHostEnvironment { EnvironmentName = "Production" };
        var provider = new AuthStateProvider(configuration, environment);
        if (!string.IsNullOrWhiteSpace(customerId))
        {
            provider.SetCustomer(new CustomerOption(customerId, "Test Customer"));
        }

        return provider;
    }

    private static string ChatResponseJson() =>
        """
        {"conversationId":"conv-1","response":"ok","toolCalls":[]}
        """;

    private static string CustomerJson() =>
        """
        {"id":"cust-1","first_name":"Emma","last_name":"Wilson","email":"emma@example.com","loyalty_tier":"Gold","account_status":"Active"}
        """;
}
