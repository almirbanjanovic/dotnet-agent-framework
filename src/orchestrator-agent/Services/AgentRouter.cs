using System.Net.Http.Json;
using Contoso.OrchestratorAgent.Models;

namespace Contoso.OrchestratorAgent.Services;

// Forwards the chat turn to the specialist agent picked by IntentClassifier.
// We pass through the raw HTTP status + payload so any error the agent
// emits (validation, rate-limit, 5xx) reaches the BFF unchanged.

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
