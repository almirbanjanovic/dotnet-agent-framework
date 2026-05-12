using System.Text.Json.Serialization;

namespace Contoso.BlazorUi.Models;

// Mirror of Contoso.CrmApi.Models.SupportTicket. Per the repo HARD RULE
// "every project under src/ MUST be completely self-contained" we
// duplicate the contract here rather than reference the CRM project.
// Keep field names in sync with the CRM API JSON shape.
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

    // Append-only audit thread written by fraud-workflow on every refund
    // decision. Surfaced verbatim on the /tickets page so the customer
    // can see resolution context ("approved", "needs more info", etc.).
    [JsonPropertyName("comments")]
    public string? Comments { get; set; }

    // Prepaid return label fields (category="return" only). Mirror of
    // the CRM API contract — duplicated per the component-independence
    // rule. The /tickets page renders an inline panel with the label id
    // and carrier when ReturnLabelStatus is non-null.
    [JsonPropertyName("return_label_id")]
    public string? ReturnLabelId { get; set; }

    [JsonPropertyName("return_label_carrier")]
    public string? ReturnLabelCarrier { get; set; }

    [JsonPropertyName("return_label_url")]
    public string? ReturnLabelUrl { get; set; }

    [JsonPropertyName("return_label_status")]
    public string? ReturnLabelStatus { get; set; }

    [JsonPropertyName("return_label_created_at")]
    public string? ReturnLabelCreatedAt { get; set; }

    [JsonPropertyName("return_label_voided_at")]
    public string? ReturnLabelVoidedAt { get; set; }
}
