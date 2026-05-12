using System.Text.Json.Serialization;

namespace Contoso.CrmApi.Models;

// Wire DTO posted by fraud-workflow to /api/v1/internal/tickets/{id}/refund-decision.
// This is a *service-to-service* DTO — the customer-facing PATCH endpoint
// (UpdateTicketStatusRequest) is intentionally separate so the two evolve
// independently and the LLM-driven path cannot accidentally invoke this
// route by hallucinating a body.
public sealed class RefundDecisionRequest
{
    // "approve" | "reject" | "below_threshold" | "timeout".
    // Maps to the FinalAction.Decision + Source pair in fraud-workflow.
    // Validated by the endpoint; unknown values are rejected with 400.
    [JsonPropertyName("decision")]
    public string Decision { get; set; } = string.Empty;

    // "auto" | "operator" | "timeout" | "system". Surfaced verbatim in
    // the appended comment so customers can tell who decided.
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    // Free-text rationale. Goes into the appended comment line.
    // The endpoint sanitizes — newlines and control chars are stripped
    // so a single decision can't inject extra audit lines.
    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    // Optional alert id for traceability. Threaded into the comment line
    // when present so an operator reading the ticket can find the
    // corresponding fraud-workflow run.
    [JsonPropertyName("alert_id")]
    public string? AlertId { get; set; }
}
