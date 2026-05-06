using System.Text.Json.Serialization;

namespace Contoso.BlazorUi.Models;

public sealed class MeResponse
{
    [JsonPropertyName("customerId")]
    public string? CustomerId { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }
}
