using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Contoso.CrmMcp.Models;

// The [Description] attributes flow through MCP into the JSON schema the
// LLM sees when it picks the create_support_ticket tool. The CRM API
// validates these enum values strictly (see crm-api/Endpoints/
// SupportTicketEndpoints.cs s_validCategories / s_validPriorities). If
// the schema doesn't enumerate them up-front, the model guesses
// ("Return", "Normal"), gets a 400, retries from the error message, and
// the user sees an unnecessary "technical error… let me try again"
// turn in chat. Keep these in sync with the API's allow-lists.
public sealed class CreateTicketRequest
{
    [JsonPropertyName("customer_id")]
    [Description("Customer ID the ticket belongs to.")]
    public string CustomerId { get; set; } = string.Empty;

    [JsonPropertyName("order_id")]
    [Description("Optional order ID this ticket relates to.")]
    public string? OrderId { get; set; }

    [JsonPropertyName("category")]
    [Description("Ticket category. MUST be one of (lowercase, exact): 'shipping', 'product-issue', 'return', 'general'. Use 'return' for refund/return requests.")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("priority")]
    [Description("Ticket priority. MUST be one of (lowercase, exact): 'low', 'medium', 'high'. Default to 'medium' unless the customer signals urgency.")]
    public string Priority { get; set; } = string.Empty;

    [JsonPropertyName("subject")]
    [Description("Short subject line summarizing the issue.")]
    public string Subject { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    [Description("Detailed description of the customer's issue, including order ID and item names where relevant.")]
    public string Description { get; set; } = string.Empty;
}
