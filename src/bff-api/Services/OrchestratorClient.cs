using System.Net.Http.Json;

namespace Contoso.BffApi.Services;

public sealed class OrchestratorClient
{
    private readonly HttpClient _httpClient;

    public OrchestratorClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task<HttpResponseMessage> SendAsync(string customerId, string message, CancellationToken ct = default)
        => _httpClient.PostAsJsonAsync("/api/v1/chat", new OrchestratorChatRequest(customerId, message), ct);

    public Task<HttpResponseMessage> GetHealthAsync(CancellationToken ct = default)
        => _httpClient.GetAsync("/health", ct);

    private sealed record OrchestratorChatRequest(string CustomerId, string Message);
}
