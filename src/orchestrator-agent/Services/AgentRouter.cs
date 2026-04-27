using System.Net.Http.Json;
using Contoso.OrchestratorAgent.Models;

namespace Contoso.OrchestratorAgent.Services;

internal sealed class AgentRouter
{
    private readonly CrmAgentClient _crmClient;
    private readonly ProductAgentClient _productClient;

    public AgentRouter(CrmAgentClient crmClient, ProductAgentClient productClient)
    {
        _crmClient = crmClient;
        _productClient = productClient;
    }

    public async Task<AgentRouterResult> RouteAsync(
        string intent,
        ChatRequest request,
        CancellationToken cancellationToken)
    {
        var client = intent.Equals("PRODUCT", StringComparison.OrdinalIgnoreCase)
            ? _productClient.HttpClient
            : _crmClient.HttpClient;

        var response = await client.PostAsJsonAsync("/api/v1/chat", request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        return new AgentRouterResult((int)response.StatusCode, payload);
    }
}

internal sealed record AgentRouterResult(int StatusCode, string Payload);

internal sealed class CrmAgentClient
{
    public CrmAgentClient(HttpClient httpClient)
    {
        HttpClient = httpClient;
    }

    public HttpClient HttpClient { get; }
}

internal sealed class ProductAgentClient
{
    public ProductAgentClient(HttpClient httpClient)
    {
        HttpClient = httpClient;
    }

    public HttpClient HttpClient { get; }
}
