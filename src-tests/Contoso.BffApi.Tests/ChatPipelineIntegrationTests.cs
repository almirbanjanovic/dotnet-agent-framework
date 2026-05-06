using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Contoso.BffApi.Models;
using FluentAssertions;

namespace Contoso.BffApi.Tests;

[Collection(nameof(BffApiFactoryCollection))]
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
    public async Task ChatEndpoint_OrchestratorError_Returns502WithSafeBody()
    {
        // Whatever the upstream agent returns (4xx or 5xx with whatever body),
        // the BFF normalises it to 502 BadGateway with a JSON body the UI can
        // render. We deliberately do NOT echo the upstream body to the
        // browser — it may contain stack frames, internal exception messages,
        // payload echoes, or other PII. Operators get the full body in the
        // server log; clients get a generic message.
        using var factory = new BffApiWebApplicationFactory(_ =>
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("orchestrator-failed-with-internal-stack-frame", Encoding.UTF8, "text/plain")
            });
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/chat")
        {
            Content = JsonContent.Create(new ChatRequest("hi", null))
        };
        request.Headers.Add("X-Customer-Id", "cust-1");
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        var payload = await response.Content.ReadAsStringAsync();
        payload.Should().Contain("OrchestratorError");
        payload.Should().Contain("400");                         // status code is safe diagnostic info
        payload.Should().NotContain("orchestrator-failed");      // upstream body MUST NOT leak to client
        payload.Should().NotContain("internal-stack-frame");
    }

    [Fact]
    public async Task ChatEndpoint_OrchestratorThrows_DoesNotLeakExceptionMessage()
    {
        // Regression test: when the orchestrator HTTP call itself throws
        // (network failure, DNS, TLS, etc.), the BFF must NOT include
        // ex.Message in the response body. Wrapping exceptions can include
        // hostnames, file paths, certificates, or even payload fragments.
        const string sensitiveToken = "SECRET_INTERNAL_HOSTNAME_42";
        using var factory = new BffApiWebApplicationFactory(_ =>
            throw new HttpRequestException(sensitiveToken));
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/chat")
        {
            Content = JsonContent.Create(new ChatRequest("hi", null))
        };
        request.Headers.Add("X-Customer-Id", "cust-1");
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        var payload = await response.Content.ReadAsStringAsync();
        payload.Should().NotContain(sensitiveToken,
            "ex.Message must never flow through to the browser response");
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

    [Fact]
    public async Task ChatEndpoint_SecondTurn_ForwardsPriorMessagesAsHistory()
    {
        var capturedPayloads = new List<string>();
        using var factory = new BffApiWebApplicationFactory(request =>
        {
            capturedPayloads.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(BuildAgentResponse("ok"), Encoding.UTF8, "application/json")
            };
        });
        var client = factory.CreateClient();

        var first = await SendChatAsync(client, "cust-1", "first question");

        var follow = new HttpRequestMessage(HttpMethod.Post, "/api/v1/chat")
        {
            Content = JsonContent.Create(new ChatRequest("second question", first.ConversationId))
        };
        follow.Headers.Add("X-Customer-Id", "cust-1");
        var followResponse = await client.SendAsync(follow);
        followResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        capturedPayloads.Should().HaveCount(2);

        using var firstDoc = JsonDocument.Parse(capturedPayloads[0]);
        firstDoc.RootElement.GetProperty("history").GetArrayLength().Should().Be(0);

        using var secondDoc = JsonDocument.Parse(capturedPayloads[1]);
        var history = secondDoc.RootElement.GetProperty("history");
        history.GetArrayLength().Should().Be(2);
        history[0].GetProperty("role").GetString().Should().Be("user");
        history[0].GetProperty("content").GetString().Should().Be("first question");
        history[1].GetProperty("role").GetString().Should().Be("assistant");
        history[1].GetProperty("content").GetString().Should().Be("ok");
        secondDoc.RootElement.GetProperty("message").GetString().Should().Be("second question");
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
