using System.Net.Http.Json;

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
    //   { customerId, orderId, amount, reason }
    // The fraud workflow gates on `Refund:Threshold` (default $200): below
    // threshold returns 200 OK with status="below_threshold" and does NOT
    // start the graph; above threshold returns 202 Accepted + alertId.
    public async Task<bool> SubmitRefundAlertAsync(
        string customerId, string orderId, decimal amount, string reason, CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync(
                "/api/v1/refunds",
                new { customerId, orderId, amount, reason },
                ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Fraud workflow refused refund alert for customer {CustomerId} order {OrderId}: {StatusCode}.",
                    customerId, orderId, (int)response.StatusCode);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            // Fire-and-forget: never propagate. The ticket creation that
            // triggered us has already succeeded; the worst case is that
            // an ops review is missed, which is what we log for.
            _logger.LogWarning(ex,
                "Failed to submit refund alert to fraud-workflow for customer {CustomerId} order {OrderId}.",
                customerId, orderId);
            return false;
        }
    }
}
