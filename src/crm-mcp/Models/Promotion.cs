using System.Text.Json.Serialization;

namespace Contoso.CrmMcp.Models;

public sealed class Promotion
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("discount_percent")]
    public int DiscountPercent { get; set; }

    [JsonPropertyName("eligible_categories")]
    public string EligibleCategories { get; set; } = string.Empty;

    [JsonPropertyName("min_loyalty_tier")]
    public string MinLoyaltyTier { get; set; } = string.Empty;

    [JsonPropertyName("start_date")]
    public string StartDate { get; set; } = string.Empty;

    [JsonPropertyName("end_date")]
    public string EndDate { get; set; } = string.Empty;

    [JsonPropertyName("active")]
    public bool Active { get; set; }
}
