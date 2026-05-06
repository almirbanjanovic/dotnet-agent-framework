using System.Text.Json.Serialization;

namespace Contoso.BlazorUi.Models;

public sealed record PlaceOrderRequest(
    [property: JsonPropertyName("shipping_address")] string? ShippingAddress,
    [property: JsonPropertyName("items")] IReadOnlyList<PlaceOrderItem> Items);

public sealed record PlaceOrderItem(
    [property: JsonPropertyName("product_id")] string ProductId,
    [property: JsonPropertyName("quantity")] int Quantity);

public sealed class PlaceOrderResponse
{
    [JsonPropertyName("order")]
    public Order Order { get; set; } = new();

    [JsonPropertyName("items")]
    public IReadOnlyList<OrderItem> Items { get; set; } = Array.Empty<OrderItem>();
}
