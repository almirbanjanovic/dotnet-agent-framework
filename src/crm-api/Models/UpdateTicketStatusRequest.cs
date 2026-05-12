using System.Text.Json.Serialization;

namespace Contoso.CrmApi.Models;

// Body for PATCH /api/v1/tickets/{id}. Used by the Blazor UI's "Cancel"
// affordance and by the agent's `cancel_support_ticket` MCP tool.
//
// `customer_id` is the legacy/test fallback when no X-Customer-Entra-Id
// header is present. When the header IS present, it wins (the body
// cannot be used to mutate another customer's ticket).
public sealed class UpdateTicketStatusRequest
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("customer_id")]
    public string? CustomerId { get; set; }
}
