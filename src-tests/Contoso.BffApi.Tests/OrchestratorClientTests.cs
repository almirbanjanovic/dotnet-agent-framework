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
        var request = handler.Requests[0];
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.PathAndQuery.Should().Be("/api/v1/chat");
        var payload = await request.Content!.ReadAsStringAsync();
        using var document = JsonDocument.Parse(payload);
        document.RootElement.GetProperty("customerId").GetString().Should().Be("cust-1");
        document.RootElement.GetProperty("message").GetString().Should().Be("hello");
    }

    [Fact]
    public async Task GetHealthAsync_GetsCorrectUrl()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var orchestrator = new OrchestratorClient(client);

        await orchestrator.GetHealthAsync();

        handler.Requests.Should().ContainSingle();
        var request = handler.Requests[0];
        request.Method.Should().Be(HttpMethod.Get);
        request.RequestUri!.PathAndQuery.Should().Be("/health");
    }
}
