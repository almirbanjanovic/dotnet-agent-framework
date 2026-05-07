using System.Text.Json;

namespace Contoso.CrmAgent.Endpoints;

// Minimal Server-Sent Events writer. Inlined per the repo's component-
// independence rule (no Shared/ project). Each event flushes to the
// underlying response so the browser sees tokens as they're produced.

internal static class SseWriter
{
    // Hard cap on a single event payload. An event larger than this is
    // almost always a runaway model output or a buggy serializer; we'd
    // rather drop the event and log than blow a downstream proxy or the
    // browser's event-buffer budget.
    private const int MaxEventPayloadBytes = 256 * 1024; // 256 KB

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static async Task WriteAsync(HttpResponse response, string eventName, object? data, CancellationToken ct)
    {
        var json = data is null ? "{}" : JsonSerializer.Serialize(data, JsonOptions);
        // Compare against the actual UTF-8 byte count Kestrel will write,
        // not char/code-unit count — multi-byte characters can otherwise
        // bypass the cap by up to ~3x.
        var byteCount = System.Text.Encoding.UTF8.GetByteCount(json);
        if (byteCount > MaxEventPayloadBytes)
        {
            json = JsonSerializer.Serialize(
                new { error = "event_payload_too_large", eventName, sizeBytes = byteCount },
                JsonOptions);
            eventName = "error";
        }
        await response.WriteAsync($"event: {eventName}\ndata: {json}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }
}
