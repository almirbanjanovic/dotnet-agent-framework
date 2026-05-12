using System.Net.Http.Json;
using System.Text.Json;

namespace Contoso.CrmApi.Services;

// Outbound client to the fraud-workflow service. Used by the support-ticket
// endpoint to fan a `category=return` ticket out as a refund alert so the
// fraud-workflow's risk-scoring graph runs against real customer activity
// (today the only trigger is the dev "Simulate alert" button on the
// Operations page).
//
// Architecture HARD RULE: src/ projects share NO project references. We talk
// to fraud-workflow over HTTP using the URL injected by AppHost
// (services__fraud-workflow__http__0) or, in standalone mode, the
// FraudWorkflow:BaseUrl config key.
//
// Every call is fire-and-forget from the ticket endpoint's POV:
//   - Errors must NEVER fail the customer's ticket-creation request.
//   - We log the failure so operators can spot the gap.
//   - The endpoint inspects the returned status: "below_threshold" means
//     the workflow short-circuited and the caller should resolve the
//     ticket directly (the workflow won't call back).
public enum FraudWorkflowResponseStatus
{
    // Network failure, 5xx from the workflow, or an unexpected response
    // shape. Treat as "alert was lost" — the ticket stays open and
    // operators can retrigger from the Operations dashboard.
    Failed,
    // Workflow accepted the alert and started running. Caller should
    // expect a callback to /api/v1/internal/tickets/{id}/refund-decision
    // when a terminal decision is reached.
    Accepted,
    // Workflow short-circuited because amount < Refund:Threshold. No
    // workflow run, no callback. Caller should resolve the ticket
    // directly.
    BelowThreshold
}

public readonly record struct FraudWorkflowSubmitOutcome(
    FraudWorkflowResponseStatus Status,
    string? AlertId);

public sealed class FraudWorkflowClient
{
    private readonly HttpClient _http;
    private readonly ILogger<FraudWorkflowClient> _logger;

    public FraudWorkflowClient(HttpClient http, ILogger<FraudWorkflowClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    // Wire DTO matches Contoso.FraudWorkflow.Endpoints.RefundEndpoint.RefundRequest:
    //   { customerId, orderId, amount, reason, ticketId }
    // The fraud workflow gates on `Refund:Threshold` (default $200): below
    // threshold returns 200 OK with status="below_threshold" and does NOT
    // start the graph; above threshold returns 202 Accepted + alertId.
    public async Task<FraudWorkflowSubmitOutcome> SubmitRefundAlertAsync(
        string customerId,
        string orderId,
        decimal amount,
        string reason,
        string? ticketId,
        CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync(
                "/api/v1/refunds",
                new { customerId, orderId, amount, reason, ticketId },
                ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Fraud workflow refused refund alert for customer {CustomerId} order {OrderId}: {StatusCode}.",
                    customerId, orderId, (int)response.StatusCode);
                return new FraudWorkflowSubmitOutcome(FraudWorkflowResponseStatus.Failed, null);
            }

            // Inspect the response body to distinguish 200 OK
            // (below-threshold short-circuit) from 202 Accepted (workflow
            // started). We do this loosely — the workflow's response
            // shape is deliberately small but the exact field names
            // matter; mismatch falls back to Accepted (safe default —
            // we'll still wait for a callback).
            try
            {
                using var doc = await JsonDocument.ParseAsync(
                    await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                var status = doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() : null;
                var alertId = doc.RootElement.TryGetProperty("alertId", out var a) ? a.GetString() : null;

                return string.Equals(status, "below_threshold", StringComparison.OrdinalIgnoreCase)
                    ? new FraudWorkflowSubmitOutcome(FraudWorkflowResponseStatus.BelowThreshold, null)
                    : new FraudWorkflowSubmitOutcome(FraudWorkflowResponseStatus.Accepted, alertId);
            }
            catch (JsonException)
            {
                // Treat as Accepted — we already saw a 2xx, the body just
                // isn't parseable. Worst case we wait for a callback that
                // never comes; better than re-resolving a ticket that the
                // workflow is actively running against.
                return new FraudWorkflowSubmitOutcome(FraudWorkflowResponseStatus.Accepted, null);
            }
        }
        catch (Exception ex)
        {
            // Fire-and-forget: never propagate. The ticket creation that
            // triggered us has already succeeded; the worst case is that
            // an ops review is missed, which is what we log for.
            _logger.LogWarning(ex,
                "Failed to submit refund alert to fraud-workflow for customer {CustomerId} order {OrderId}.",
                customerId, orderId);
            return new FraudWorkflowSubmitOutcome(FraudWorkflowResponseStatus.Failed, null);
        }
    }
}
