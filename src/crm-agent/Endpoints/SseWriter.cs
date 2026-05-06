using System.Text.Json;

namespace Contoso.CrmAgent.Endpoints;

// Minimal Server-Sent Events writer. Inlined per the repo's component-
// independence rule (no Shared/ project). Each event flushes to the
// underlying response so the browser sees tokens as they're produced.

internal static class SseWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static async Task WriteAsync(HttpResponse response, string eventName, object? data, CancellationToken ct)
    {
        var json = data is null ? "{}" : JsonSerializer.Serialize(data, JsonOptions);
        await response.WriteAsync($"event: {eventName}\ndata: {json}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }
}
