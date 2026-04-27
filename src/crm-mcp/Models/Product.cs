using System.Text.Json.Serialization;

namespace Contoso.CrmMcp.Models;

public sealed class Product
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    [JsonPropertyName("in_stock")]
    public bool InStock { get; set; }

    [JsonPropertyName("rating")]
    public double Rating { get; set; }

    [JsonPropertyName("weight_kg")]
    public double WeightKg { get; set; }

    [JsonPropertyName("image_filename")]
    public string ImageFilename { get; set; } = string.Empty;
}
