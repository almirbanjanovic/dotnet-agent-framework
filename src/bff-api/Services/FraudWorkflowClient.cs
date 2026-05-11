using System.Net.Http.Json;

namespace Contoso.BffApi.Services;

// Typed client for the fraud-workflow service. Used by the operator
// dashboard endpoints in /api/v1/operations and /api/v1/refunds.
//
// The fraud-workflow service is internal to the back-end; the BFF is the
// only thing the Blazor UI talks to. We deliberately do NOT attach the
// CustomerHeaderHandler here — refund decisions are made by *operators*,
// not customers, and forwarding a customer header would mis-attribute
// audit records inside the workflow.

public sealed class FraudWorkflowClient
{
    private readonly HttpClient _http;

    public FraudWorkflowClient(HttpClient http) => _http = http;

    public Task<HttpResponseMessage> SubmitRefundAsync(object body, CancellationToken ct = default)
        => _http.PostAsJsonAsync("/api/v1/refunds", body, ct);

    public Task<HttpResponseMessage> ListPendingAsync(CancellationToken ct = default)
        => _http.GetAsync("/api/v1/operations/pending", ct);

    public Task<HttpResponseMessage> SubmitDecisionAsync(object body, CancellationToken ct = default)
        => _http.PostAsJsonAsync("/api/v1/operations/decisions", body, ct);

    public Task<HttpResponseMessage> GetOutcomeAsync(string alertId, CancellationToken ct = default)
        => _http.GetAsync($"/api/v1/operations/{Uri.EscapeDataString(alertId)}", ct);

    public Task<HttpResponseMessage> GetHealthAsync(CancellationToken ct = default)
        => _http.GetAsync("/health", ct);
}
