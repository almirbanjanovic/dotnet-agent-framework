using System.Collections.Generic;
using System.Net.Http.Json;

namespace Contoso.BffApi.Services;

public sealed class OrchestratorClient
{
    private readonly HttpClient _httpClient;

    public OrchestratorClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task<HttpResponseMessage> SendAsync(
        string customerId,
        string message,
        IReadOnlyList<OrchestratorHistoryMessage>? history = null,
        CancellationToken ct = default)
        => _httpClient.PostAsJsonAsync(
            "/api/v1/chat",
            new OrchestratorChatRequest(customerId, message, history ?? Array.Empty<OrchestratorHistoryMessage>()),
            ct);

    // Opens an SSE stream against the orchestrator. The caller is
    // responsible for disposing the returned HttpResponseMessage.
    public Task<HttpResponseMessage> StreamAsync(
        string customerId,
        string message,
        IReadOnlyList<OrchestratorHistoryMessage>? history = null,
        CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/chat/stream")
        {
            Content = JsonContent.Create(new OrchestratorChatRequest(
                customerId,
                message,
                history ?? Array.Empty<OrchestratorHistoryMessage>()))
        };
        return _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    public Task<HttpResponseMessage> GetHealthAsync(CancellationToken ct = default)
        => _httpClient.GetAsync("/health", ct);

    private sealed record OrchestratorChatRequest(
        string CustomerId,
        string Message,
        IReadOnlyList<OrchestratorHistoryMessage> History);
}

public sealed record OrchestratorHistoryMessage(string Role, string Content);
