using System.Text.Json.Serialization;

namespace Contoso.BffApi.Endpoints;

// Body for PATCH /api/v1/customers/{id}/tickets/{ticketId}. Mirror of
// the CRM API's `UpdateTicketStatusRequest`. Per the architecture HARD
// RULE the BFF cannot reference Contoso.CrmApi, so we duplicate the DTO.
internal sealed class UpdateTicketStatusRequest
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}
