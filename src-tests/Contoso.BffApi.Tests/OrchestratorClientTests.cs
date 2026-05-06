using System.Net;
using System.Text.Json;
using Contoso.BffApi.Services;
using FluentAssertions;

namespace Contoso.BffApi.Tests;

public class OrchestratorClientTests
{
    [Fact]
    public async Task SendAsync_PostsToCorrectUrl()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var orchestrator = new OrchestratorClient(client);

        await orchestrator.SendAsync("cust-1", "hello");

        handler.Requests.Should().ContainSingle();
        var request = handler.Requests.First();
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.PathAndQuery.Should().Be("/api/v1/chat");
        var payload = await request.Content!.ReadAsStringAsync();
        using var document = JsonDocument.Parse(payload);
        document.RootElement.GetProperty("customerId").GetString().Should().Be("cust-1");
        document.RootElement.GetProperty("message").GetString().Should().Be("hello");
    }

    [Fact]
    public async Task SendAsync_ForwardsHistoryInRequestBody()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var orchestrator = new OrchestratorClient(client);

        var history = new[]
        {
            new OrchestratorHistoryMessage("user", "earlier question"),
            new OrchestratorHistoryMessage("assistant", "earlier answer")
        };

        await orchestrator.SendAsync("cust-1", "follow up", history);

        var request = handler.Requests.Should().ContainSingle().Subject;
        var payload = await request.Content!.ReadAsStringAsync();
        using var document = JsonDocument.Parse(payload);
        var historyArray = document.RootElement.GetProperty("history");
        historyArray.GetArrayLength().Should().Be(2);
        historyArray[0].GetProperty("role").GetString().Should().Be("user");
        historyArray[0].GetProperty("content").GetString().Should().Be("earlier question");
        historyArray[1].GetProperty("role").GetString().Should().Be("assistant");
        historyArray[1].GetProperty("content").GetString().Should().Be("earlier answer");
    }

    [Fact]
    public async Task SendAsync_NoHistory_SerializesEmptyArray()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var orchestrator = new OrchestratorClient(client);

        await orchestrator.SendAsync("cust-1", "hello");

        var request = handler.Requests.Should().ContainSingle().Subject;
        var payload = await request.Content!.ReadAsStringAsync();
        using var document = JsonDocument.Parse(payload);
        document.RootElement.GetProperty("history").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task GetHealthAsync_GetsCorrectUrl()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var orchestrator = new OrchestratorClient(client);

        await orchestrator.GetHealthAsync();

        handler.Requests.Should().ContainSingle();
        var request = handler.Requests.First();
        request.Method.Should().Be(HttpMethod.Get);
        request.RequestUri!.PathAndQuery.Should().Be("/health");
    }
}
