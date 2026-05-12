using System.Net;
using System.Net.Http.Json;
using Contoso.FraudWorkflow.Models;

namespace Contoso.FraudWorkflow.Services;

// Outbound client back to crm-api. Used by the workflow runner to close
// the loop on the customer's support ticket once a terminal decision is
// reached (auto-approve, operator approve/reject, timeout). Without this
// callback the customer never sees the outcome — the operator's click
// in the Operations dashboard would be invisible.
//
// Architecture HARD RULE: src/ projects share NO project references. We
// talk to crm-api over HTTP using the URL injected by AppHost
// (services__crm-api__http__0) or, in standalone mode, the
// CrmApi:BaseUrl config key.
//
// Network trust model: the CRM API has no auth surface and trusts any
// caller on the cluster network. The /internal/ path prefix is the
// signal that a route is service-only (not reverse-proxied by the BFF).
//
// Failure handling: every method swallows and logs. Losing a callback
// leaves the ticket in "open" state — the customer can re-trigger via
// the chat agent and an operator can also see the open ticket on a
// dashboard. We deliberately do NOT retry from inside the workflow
// because the workflow's superstep semantics make idempotent retries
// the runtime's job, not ours.
internal sealed class CrmApiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<CrmApiClient> _logger;

    public CrmApiClient(HttpClient http, ILogger<CrmApiClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    // POST /api/v1/internal/tickets/{ticketId}/refund-decision
    // Maps a FinalAction onto the (decision, source, reason) triple the
    // CRM API expects. Returns true on 2xx, false otherwise.
    public async Task<bool> ApplyRefundDecisionAsync(FinalAction action, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(action.TicketId))
        {
            // No originating ticket — synthetic alerts from the Operations
            // dashboard never have one. Skip silently; the dashboard's
            // own "outcomes" view is the audit trail in that case.
            return false;
        }

        try
        {
            var body = new
            {
                decision = MapDecision(action),
                source = action.Source,
                reason = action.Summary,
                alert_id = action.AlertId
            };

            // Don't path-encode "/" but DO escape the ticket id so we
            // can't be tricked into hitting another endpoint with a
            // crafted id. EscapeDataString preserves alphanumerics +
            // ST- prefix that we use everywhere.
            var path = $"/api/v1/internal/tickets/{Uri.EscapeDataString(action.TicketId)}/refund-decision";

            using var response = await _http.PostAsJsonAsync(path, body, ct).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // The ticket was cancelled by the customer mid-review,
                // or the id never existed. Log and move on.
                _logger.LogInformation(
                    "CRM ticket {TicketId} not found when applying decision {Decision} (alert {AlertId}).",
                    action.TicketId, action.Decision, action.AlertId);
                return false;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "CRM API rejected refund-decision callback for ticket {TicketId} (alert {AlertId}): {StatusCode}.",
                    action.TicketId, action.AlertId, (int)response.StatusCode);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to apply refund decision for ticket {TicketId} (alert {AlertId}).",
                action.TicketId, action.AlertId);
            return false;
        }
    }

    private static string MapDecision(FinalAction action) => action.Decision switch
    {
        ApprovalDecision.Approve => "approve",
        ApprovalDecision.Reject => "reject",
        ApprovalDecision.TimedOut => "timeout",
        // Reinvestigate is out of scope for the lab today — surface as
        // "reject" so the customer is asked for follow-up rather than
        // having a refund silently issued.
        ApprovalDecision.Reinvestigate => "reject",
        _ => "reject"
    };
}
