using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Contoso.BffApi.Models;
using FluentAssertions;

namespace Contoso.BffApi.Tests;

public class ChatPipelineIntegrationTests
{
    [Fact]
    public async Task ChatEndpoint_EmptyMessage_Returns400()
    {
        using var factory = new BffApiWebApplicationFactory();
        var client = factory.CreateClient();

        var request = new ChatRequest(string.Empty, null);
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/chat")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("X-Customer-Id", "cust-1");

        var response = await client.SendAsync(message);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ChatEndpoint_MissingCustomerHeader_Returns401()
    {
        using var factory = new BffApiWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/chat", new ChatRequest("hi", null));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ChatEndpoint_NewConversation_ReturnsConversationId()
    {
        var payload = BuildAgentResponse("hello");
        using var factory = new BffApiWebApplicationFactory(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        var client = factory.CreateClient();

        var response = await SendChatAsync(client, "cust-1", "hi");

        response.ConversationId.Should().NotBeNullOrWhiteSpace();
        response.Response.Should().Be("hello");
    }

    [Fact]
    public async Task ChatEndpoint_CrossCustomerAccess_Returns404()
    {
        var callCount = 0;
        using var factory = new BffApiWebApplicationFactory(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(BuildAgentResponse("ok"), Encoding.UTF8, "application/json")
            };
        });
        var client = factory.CreateClient();

        var created = await SendChatAsync(client, "cust-a", "hi");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/chat")
        {
            Content = JsonContent.Create(new ChatRequest("follow-up", created.ConversationId))
        };
        request.Headers.Add("X-Customer-Id", "cust-b");
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task ChatEndpoint_OrchestratorNonJsonResponse_FallsBackToPayload()
    {
        using var factory = new BffApiWebApplicationFactory(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("plain-text", Encoding.UTF8, "text/plain")
            });
        var client = factory.CreateClient();

        var response = await SendChatAsync(client, "cust-1", "hi");

        response.Response.Should().Be("plain-text");
    }

    [Fact]
    public async Task ChatEndpoint_OrchestratorError_ProxiesStatusCode()
    {
        using var factory = new BffApiWebApplicationFactory(_ =>
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("orchestrator-failed", Encoding.UTF8, "text/plain")
            });
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/chat")
        {
            Content = JsonContent.Create(new ChatRequest("hi", null))
        };
        request.Headers.Add("X-Customer-Id", "cust-1");
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var payload = await response.Content.ReadAsStringAsync();
        payload.Should().Be("orchestrator-failed");
    }

    [Fact]
    public async Task ConversationListEndpoint_ReturnsCustomerConversations()
    {
        using var factory = new BffApiWebApplicationFactory(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(BuildAgentResponse("ok"), Encoding.UTF8, "application/json")
            });
        var client = factory.CreateClient();

        _ = await SendChatAsync(client, "cust-a", "hi");
        _ = await SendChatAsync(client, "cust-a", "hello");
        _ = await SendChatAsync(client, "cust-b", "hola");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/conversations");
        request.Headers.Add("X-Customer-Id", "cust-a");
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var conversations = await response.Content.ReadFromJsonAsync<List<Conversation>>();
        conversations.Should().NotBeNull();
        conversations!.Should().HaveCount(2);
        conversations.Should().OnlyContain(c => c.CustomerId == "cust-a");
    }

    private static string BuildAgentResponse(string response) =>
        JsonSerializer.Serialize(new { response, toolCalls = Array.Empty<object>() });

    private static async Task<ChatResponse> SendChatAsync(HttpClient client, string customerId, string message)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/chat")
        {
            Content = JsonContent.Create(new ChatRequest(message, null))
        };
        request.Headers.Add("X-Customer-Id", customerId);
        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ChatResponse>();
        return payload!;
    }
}
