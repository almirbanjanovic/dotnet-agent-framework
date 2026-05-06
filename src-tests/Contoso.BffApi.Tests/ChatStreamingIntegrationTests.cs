using System.Net;
using System.Net.Http.Json;
using System.Text;
using Contoso.BffApi.Models;
using FluentAssertions;

namespace Contoso.BffApi.Tests;

// Smoke-tests for /api/v1/chat/stream that focus on protocol compliance:
//   - The conversation event is emitted before any upstream contact.
//   - Tokens forwarded from the orchestrator are visible to the client.
//   - The orchestrator's terminal `done` is suppressed and replaced by the
//     BFF's persistence-anchored done.
//   - Multi-line `data:` blocks (RFC-allowed) are joined and forwarded
//     correctly by the BFF parser.
[Collection(nameof(BffApiFactoryCollection))]
public class ChatStreamingIntegrationTests
{
    [Fact]
    public async Task ChatStream_HappyPath_EmitsConversationTokensThenDone()
    {
        var orchestratorSse =
            "event: stage\ndata: {\"stage\":\"classifying\"}\n\n" +
            "event: stage\ndata: {\"stage\":\"routed\",\"agent\":\"crm\"}\n\n" +
            "event: token\ndata: {\"text\":\"Hello \"}\n\n" +
            "event: token\ndata: {\"text\":\"world\"}\n\n" +
            "event: done\ndata: {\"agent\":\"crm\"}\n\n";

        using var factory = new BffApiWebApplicationFactory(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(orchestratorSse, Encoding.UTF8, "text/event-stream")
            });
        var client = factory.CreateClient();

        var (events, body) = await ReadStreamAsync(client, "cust-1", "say hi");

        events.Should().Contain(e => e.Event == "conversation");
        events.Where(e => e.Event == "token").Should().HaveCount(2);
        events.First(e => e.Event == "token").Data.Should().Contain("Hello ");

        // BFF emits exactly one `done` and suppresses upstream `done`.
        events.Count(e => e.Event == "done").Should().Be(1);
        events.Last().Event.Should().Be("done");

        // The conversation event must come BEFORE any token so the client
        // can pin the conversation ID even if the agent is slow.
        var convoIdx = events.FindIndex(e => e.Event == "conversation");
        var firstTokenIdx = events.FindIndex(e => e.Event == "token");
        convoIdx.Should().BeLessThan(firstTokenIdx);

        body.Should().Contain("event: stage");
        body.Should().Contain("event: token");
    }

    [Fact]
    public async Task ChatStream_OrchestratorReturnsError_EmitsErrorEvent()
    {
        using var factory = new BffApiWebApplicationFactory(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("upstream blew up", Encoding.UTF8, "text/plain")
            });
        var client = factory.CreateClient();

        var (events, _) = await ReadStreamAsync(client, "cust-1", "hi");

        events.Should().Contain(e => e.Event == "conversation");
        events.Should().Contain(e => e.Event == "error");
        events.Last(e => e.Event == "error").Data.Should().Contain("500");
    }

    [Fact]
    public async Task ChatStream_OrchestratorReturnsError_DoesNotLeakUpstreamBody()
    {
        // Regression: the SSE error event must NOT include the upstream
        // body (status code only). The first round of fixes covered the
        // buffered /chat path; this locks down /chat/stream too.
        const string sensitiveToken = "SENSITIVE_UPSTREAM_DETAIL_xyz789";
        using var factory = new BffApiWebApplicationFactory(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent(sensitiveToken, Encoding.UTF8, "text/plain")
            });
        var client = factory.CreateClient();

        var (events, body) = await ReadStreamAsync(client, "cust-1", "hi");

        var errorEvent = events.LastOrDefault(e => e.Event == "error");
        errorEvent.Should().NotBeNull();
        errorEvent!.Data.Should().NotContain(sensitiveToken,
            "the upstream body must never flow through to the SSE error event");
        body.Should().NotContain(sensitiveToken,
            "the upstream body must never appear anywhere in the SSE response");
    }

    [Fact]
    public async Task ChatStream_MissingMessage_EmitsErrorEvent()
    {
        using var factory = new BffApiWebApplicationFactory();
        var client = factory.CreateClient();

        var (events, _) = await ReadStreamAsync(client, "cust-1", string.Empty);

        events.Should().HaveCount(1);
        events[0].Event.Should().Be("error");
    }

    [Fact]
    public async Task ChatStream_MultiLineDataBlock_IsJoinedByBff()
    {
        // RFC: multiple `data:` lines in one block are joined by '\n'.
        // The BFF must forward both lines AND assemble them correctly when
        // accumulating the assistant message for persistence.
        var orchestratorSse =
            "event: token\n" +
            "data: {\"text\":\"line1\"}\n\n" +
            "event: message\n" +
            "data: first\n" +
            "data: second\n\n";

        using var factory = new BffApiWebApplicationFactory(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(orchestratorSse, Encoding.UTF8, "text/event-stream")
            });
        var client = factory.CreateClient();

        var (events, _) = await ReadStreamAsync(client, "cust-1", "test");

        // Multi-line `data:` should arrive as a single ChatStreamEvent with
        // the lines joined by '\n'.
        var msgEvent = events.FirstOrDefault(e => e.Event == "message");
        msgEvent.Should().NotBeNull();
        msgEvent!.Data.Should().Be("first\nsecond");
    }

    // ── helpers ──

    private static async Task<(List<SseEvent> Events, string RawBody)> ReadStreamAsync(
        HttpClient client,
        string customerId,
        string message)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/chat/stream")
        {
            Content = JsonContent.Create(new ChatRequest(message, null))
        };
        request.Headers.Add("X-Customer-Id", customerId);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        return (ParseSse(body), body);
    }

    private static List<SseEvent> ParseSse(string body)
    {
        var events = new List<SseEvent>();
        string? eventName = null;
        var dataBuffer = new StringBuilder();

        foreach (var line in body.Split('\n').Select(l => l.TrimEnd('\r')))
        {
            if (line.Length == 0)
            {
                if (dataBuffer.Length > 0)
                {
                    events.Add(new SseEvent(eventName ?? "message", dataBuffer.ToString()));
                }
                eventName = null;
                dataBuffer.Clear();
                continue;
            }
            if (line.StartsWith(":")) continue;
            var idx = line.IndexOf(':');
            string field, value;
            if (idx < 0) { field = line; value = string.Empty; }
            else
            {
                field = line.Substring(0, idx);
                value = line.Substring(idx + 1);
                if (value.StartsWith(" ")) value = value.Substring(1);
            }
            switch (field)
            {
                case "event": eventName = value; break;
                case "data":
                    if (dataBuffer.Length > 0) dataBuffer.Append('\n');
                    dataBuffer.Append(value);
                    break;
            }
        }

        if (dataBuffer.Length > 0)
        {
            events.Add(new SseEvent(eventName ?? "message", dataBuffer.ToString()));
        }
        return events;
    }

    private sealed record SseEvent(string Event, string Data);
}
