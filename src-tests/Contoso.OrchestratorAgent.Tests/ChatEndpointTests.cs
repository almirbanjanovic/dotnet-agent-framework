using System.Net;
using System.Net.Http.Json;
using Contoso.OrchestratorAgent.Models;
using Contoso.OrchestratorAgent.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Contoso.OrchestratorAgent.Tests;

public sealed class ChatEndpointTests
{
    [Fact]
    public async Task PostChat_ValidRequest_CallsClassifierAndRouter()
    {
        var classifierClient = new TestIntentClassifierClient { Response = "CRM" };
        var handlerCallCount = 0;
        var crmHandler = new TestHttpMessageHandler((_, _) =>
        {
            handlerCallCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"ok\"}")
            });
        });
        using var factory = CreateFactory(classifierClient, crmHandler);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/chat", new ChatRequest("C-1", "status update"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        classifierClient.LastPrompt.Should().Contain("status update");
        handlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task PostChat_EmptyMessage_ReturnsBadRequest()
    {
        using var factory = CreateFactory(new TestIntentClassifierClient(), TestHttpMessageHandler.Create("{}"));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/chat", new ChatRequest("C-1", string.Empty));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("customerId and message are required");
    }

    [Fact]
    public async Task PostChat_EmptyCustomerId_ReturnsBadRequest()
    {
        using var factory = CreateFactory(new TestIntentClassifierClient(), TestHttpMessageHandler.Create("{}"));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/chat", new ChatRequest(string.Empty, "hello"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("customerId and message are required");
    }

    [Fact]
    public async Task PostChat_NullPayload_ReturnsStatusCodeOnly()
    {
        var emptyHandler = new TestHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)
            {
                Content = new StringContent(string.Empty)
            }));
        using var factory = CreateFactory(new TestIntentClassifierClient(), emptyHandler);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/chat", new ChatRequest("C-1", "hello"));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task PostChat_HappyPath_ReturnsJsonContent()
    {
        using var factory = CreateFactory(new TestIntentClassifierClient(), TestHttpMessageHandler.Create("{\"reply\":\"ok\"}"));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/chat", new ChatRequest("C-1", "hello"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"reply\"");
    }

    [Fact]
    public async Task PostChat_GuestCustomerId_BypassesClassifierAndRoutesToProduct()
    {
        // Guests must not pay for an LLM classification round-trip and
        // must never be routed to CRM. The orchestrator forces PRODUCT.
        var classifierClient = new TestIntentClassifierClient { Response = "CRM" };
        var crmCalls = 0;
        var productCalls = 0;
        var crmHandler = new TestHttpMessageHandler((_, _) =>
        {
            crmCalls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            });
        });
        var productHandler = new TestHttpMessageHandler((_, _) =>
        {
            productCalls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"reply\":\"product\"}")
            });
        });
        using var factory = CreateFactory(classifierClient, crmHandler, productHandler);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/chat",
            new ChatRequest("guest-ABCDEFGH", "Recommend a tent"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        productCalls.Should().Be(1);
        crmCalls.Should().Be(0);
        classifierClient.LastPrompt.Should().BeNull("classifier must not be invoked for guests");
    }

    private static WebApplicationFactory<Program> CreateFactory(
        TestIntentClassifierClient classifierClient,
        TestHttpMessageHandler crmHandler,
        TestHttpMessageHandler? productHandler = null)
    {
        var classifier = new IntentClassifier(classifierClient);
        var router = new AgentRouter(
            new CrmAgentClient(new HttpClient(crmHandler) { BaseAddress = new Uri("http://crm") }),
            new ProductAgentClient(new HttpClient(productHandler ?? TestHttpMessageHandler.Create("{}")) { BaseAddress = new Uri("http://product") }));

        return new OrchestratorWebApplicationFactory(classifier, router);
    }

    private sealed class OrchestratorWebApplicationFactory(
        IntentClassifier classifier,
        AgentRouter router) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IntentClassifier>();
                services.RemoveAll<AgentRouter>();
                services.AddSingleton(classifier);
                services.AddSingleton(router);
            });
        }
    }

    private sealed class TestIntentClassifierClient : IIntentClassifierClient
    {
        public string Response { get; set; } = "CRM";

        public string? LastPrompt { get; private set; }

        public Task<string> RunAsync(string prompt, CancellationToken cancellationToken)
        {
            LastPrompt = prompt;
            return Task.FromResult(Response);
        }
    }
}
