using System.Text.Json.Serialization;

namespace Contoso.CrmMcp.Models;

public sealed class CreateTicketRequest
{
    [JsonPropertyName("customer_id")]
    public string CustomerId { get; set; } = string.Empty;

    [JsonPropertyName("order_id")]
    public string? OrderId { get; set; }

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("priority")]
    public string Priority { get; set; } = string.Empty;

    [JsonPropertyName("subject")]
    public string Subject { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}
