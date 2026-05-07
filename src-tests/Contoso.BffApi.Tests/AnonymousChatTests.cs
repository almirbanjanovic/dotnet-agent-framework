using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Contoso.BffApi.Models;
using FluentAssertions;

namespace Contoso.BffApi.Tests;

// Anonymous (guest-session) chat — covers the path where the visitor has
// no signed-in identity and supplies only an X-Guest-Session-Id header.
[Collection(nameof(BffApiFactoryCollection))]
public class AnonymousChatTests
{
    private const string GuestToken = "GUESTABCDEF012345";

    [Fact]
    public async Task ChatEndpoint_GuestSessionHeader_GetsConversation()
    {
        // Capture what the BFF forwards to the orchestrator so we can
        // confirm the guest customer id flows through unchanged.
        string? capturedCustomerId = null;
        using var factory = new BffApiWebApplicationFactory(req =>
        {
            var bytes = req.Content!.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(bytes);
            capturedCustomerId = doc.RootElement.GetProperty("customerId").GetString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(BuildAgentResponse("Sure!"), Encoding.UTF8, "application/json")
            };
        });
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/chat")
        {
            Content = JsonContent.Create(new ChatRequest("Recommend a tent", null))
        };
        request.Headers.Add("X-Guest-Session-Id", GuestToken);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ChatResponse>();
        payload!.ConversationId.Should().NotBeNullOrWhiteSpace();
        capturedCustomerId.Should().Be("guest-" + GuestToken);
    }

    [Fact]
    public async Task ChatEndpoint_NoCustomerAndNoGuestHeader_Returns401()
    {
        using var factory = new BffApiWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/chat",
            new ChatRequest("hi", null));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ChatEndpoint_MalformedGuestHeader_Returns401()
    {
        using var factory = new BffApiWebApplicationFactory();
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/chat")
        {
            Content = JsonContent.Create(new ChatRequest("hi", null))
        };
        // Header injection attempt (semicolon) — must be rejected.
        request.Headers.TryAddWithoutValidation("X-Guest-Session-Id", "abc;def");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ChatEndpoint_DifferentGuestCannotReadAnotherGuestsConversation()
    {
        using var factory = new BffApiWebApplicationFactory(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(BuildAgentResponse("ok"), Encoding.UTF8, "application/json")
            });
        var client = factory.CreateClient();

        // Guest A creates a conversation.
        var firstRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/chat")
        {
            Content = JsonContent.Create(new ChatRequest("first turn", null))
        };
        firstRequest.Headers.Add("X-Guest-Session-Id", "GUESTA__AAAAAAAA");
        var firstResponse = await client.SendAsync(firstRequest);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var first = await firstResponse.Content.ReadFromJsonAsync<ChatResponse>();

        // Guest B tries to follow up on Guest A's conversation id.
        var hijack = new HttpRequestMessage(HttpMethod.Post, "/api/v1/chat")
        {
            Content = JsonContent.Create(new ChatRequest("hijack", first!.ConversationId))
        };
        hijack.Headers.Add("X-Guest-Session-Id", "GUESTB__BBBBBBBB");

        var hijackResponse = await client.SendAsync(hijack);

        hijackResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ChatEndpoint_AuthenticatedUserHeader_TakesPrecedenceOverGuestHeader()
    {
        // Defense-in-depth: an authed (or impersonating) request that
        // also carries a guest header must NOT be downgraded to a guest
        // session. The dev-auth header (X-Customer-Id) wins.
        string? capturedCustomerId = null;
        using var factory = new BffApiWebApplicationFactory(req =>
        {
            var bytes = req.Content!.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(bytes);
            capturedCustomerId = doc.RootElement.GetProperty("customerId").GetString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(BuildAgentResponse("ok"), Encoding.UTF8, "application/json")
            };
        });
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/chat")
        {
            Content = JsonContent.Create(new ChatRequest("hi", null))
        };
        request.Headers.Add("X-Customer-Id", "real-cust");
        request.Headers.Add("X-Guest-Session-Id", "GUESTABCDEF012345");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        capturedCustomerId.Should().Be("real-cust");
    }

    [Fact]
    public async Task ChatStreamEndpoint_NoIdentity_Returns401NotSseSuccess()
    {
        // Streaming variant of "no auth, no guest header" — must surface
        // as HTTP 401 (not a 200 text/event-stream that just happens to
        // carry an SSE error event), so EnsureSuccessStatusCode in the
        // Blazor client and any health probe see the failure.
        using var factory = new BffApiWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/chat/stream",
            new ChatRequest("hi", null));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static string BuildAgentResponse(string response) =>
        JsonSerializer.Serialize(new { response, toolCalls = Array.Empty<object>() });
}