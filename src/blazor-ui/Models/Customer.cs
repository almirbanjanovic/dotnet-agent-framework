using System.Text.Json.Serialization;

namespace Contoso.BlazorUi.Models;

public sealed class Customer
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("loyalty_tier")]
    public string LoyaltyTier { get; set; } = string.Empty;

    [JsonPropertyName("account_status")]
    public string AccountStatus { get; set; } = string.Empty;
}
