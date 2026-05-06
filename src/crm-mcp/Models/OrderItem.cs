using System.Text.Json.Serialization;

namespace Contoso.CrmMcp.Models;

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

    // Denormalised from the product catalog by the CRM API. Lets agents
    // emit `![ProductName](imageFilename)` markdown without needing a
    // second `get_product_by_id` tool call — and without having to guess
    // the filename from the product name (which produces 404s, e.g.
    // `merino-wool-base-layer-top.png` → actual file is
    // `merino-base-layer-top.png`).
    [JsonPropertyName("image_filename")]
    public string ImageFilename { get; set; } = string.Empty;
}
