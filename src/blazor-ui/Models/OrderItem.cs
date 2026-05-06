using System.Text.Json.Serialization;

namespace Contoso.BlazorUi.Models;

public sealed class OrderItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("order_id")]
    public string OrderId { get; set; } = string.Empty;

    [JsonPropertyName("product_id")]
    public string ProductId { get; set; } = string.Empty;

    [JsonPropertyName("product_name")]
    public string ProductName { get; set; } = string.Empty;

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    [JsonPropertyName("unit_price")]
    public decimal UnitPrice { get; set; }

    // Populated by the BFF (denormalised from the product catalog) so
    // the orders page can show product images alongside line items
    // without an extra round-trip per item.
    [JsonPropertyName("image_filename")]
    public string ImageFilename { get; set; } = string.Empty;
}
