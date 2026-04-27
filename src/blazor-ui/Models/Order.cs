using System.Text.Json.Serialization;

namespace Contoso.BlazorUi.Models;

public sealed class Order
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("customer_id")]
    public string CustomerId { get; set; } = string.Empty;

    [JsonPropertyName("order_date")]
    public string OrderDate { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("total_amount")]
    public decimal TotalAmount { get; set; }

    [JsonPropertyName("tracking_number")]
    public string? TrackingNumber { get; set; }

    [JsonPropertyName("estimated_delivery")]
    public string? EstimatedDelivery { get; set; }

    [JsonPropertyName("items")]
    public IReadOnlyList<OrderItem>? Items { get; set; }
}
