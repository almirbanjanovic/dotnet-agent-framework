using System.Text.Json.Serialization;

namespace Contoso.CrmApi.Models;

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

    // Denormalised from the product catalog at query time. Populated so
    // chat agents can include the product image in markdown replies
    // without making a second tool call (and therefore without having
    // to guess the filename from the product name — the file on disk
    // is `merino-base-layer-top.png`, not `merino-wool-base-layer-top.png`).
    [JsonPropertyName("image_filename")]
    public string ImageFilename { get; set; } = string.Empty;
}
