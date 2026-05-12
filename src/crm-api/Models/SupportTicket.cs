using System.Text.Json.Serialization;

namespace Contoso.CrmApi.Models;

public sealed class SupportTicket
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("customer_id")]
    public string CustomerId { get; set; } = string.Empty;

    [JsonPropertyName("order_id")]
    public string? OrderId { get; set; }

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("subject")]
    public string Subject { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("priority")]
    public string Priority { get; set; } = string.Empty;

    [JsonPropertyName("opened_at")]
    public string OpenedAt { get; set; } = string.Empty;

    [JsonPropertyName("closed_at")]
    public string? ClosedAt { get; set; }

    // Append-only audit thread. The fraud-workflow service writes one
    // line here per refund decision (auto-approve, below-threshold,
    // operator approve/reject, timeout). The customer sees the latest
    // line on their /tickets page, so phrasing must be customer-safe.
    // Format per line: "[YYYY-MM-DDTHH:MM:SSZ source] message".
    [JsonPropertyName("comments")]
    public string? Comments { get; set; }

    // ── Prepaid return label (category="return" tickets only) ────────
    // All six fields are nullable: non-return tickets never populate
    // them, and a return ticket whose label create-time call failed
    // will have ReturnLabelStatus="failed" with the rest left empty.
    // Status vocabulary: "active" | "voided" | "failed". The label is
    // explicitly NOT moved to a "used" state when the refund is
    // approved — a real carrier label is consumed at drop-off, not at
    // merchant approval, and we have no signal for that.
    [JsonPropertyName("return_label_id")]
    public string? ReturnLabelId { get; set; }

    [JsonPropertyName("return_label_carrier")]
    public string? ReturnLabelCarrier { get; set; }

    // Carrier-issued PDF location. Currently a synthetic example.com
    // URL produced by FakeReturnLabelService; a real carrier impl
    // would return a short-lived signed link. Treat as opaque — the
    // UI does NOT linkify it (security: a leaked direct URL would
    // bypass the customer-owner check on the ticket).
    [JsonPropertyName("return_label_url")]
    public string? ReturnLabelUrl { get; set; }

    [JsonPropertyName("return_label_status")]
    public string? ReturnLabelStatus { get; set; }

    [JsonPropertyName("return_label_created_at")]
    public string? ReturnLabelCreatedAt { get; set; }

    [JsonPropertyName("return_label_voided_at")]
    public string? ReturnLabelVoidedAt { get; set; }
}
