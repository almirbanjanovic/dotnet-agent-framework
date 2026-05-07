using System.Security.Cryptography;
using Microsoft.JSInterop;

namespace Contoso.BlazorUi.Services;

/// <summary>
/// Mints (and caches) a stable opaque session id for the current browser
/// so anonymous visitors can hold a chat conversation without signing in.
/// The token is persisted in <c>localStorage</c> so it survives reloads
/// and is shared across tabs in the same browser.
///
/// The token is sent to the BFF as the <c>X-Guest-Session-Id</c> header.
/// It is NOT a security primitive: anyone can fabricate a value.
/// Downstream services treat any customer id starting with
/// <c>guest-</c> as untrusted (no CRM tools, no order lookup, etc.).
/// </summary>
public sealed class GuestSessionProvider
{
    // Same length / charset bounds as Contoso.BffApi.Services.GuestId.FromHeader
    // — keep these in sync. 16 chars of base32 ≈ 80 bits of entropy, plenty
    // for a non-secret session token.
    private const string StorageKey = "contoso.chat.guestSessionId";
    private const int TokenLength = 16;
    private const string Charset = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    private readonly IJSRuntime _jsRuntime;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _cached;

    public GuestSessionProvider(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async ValueTask<string> GetOrCreateAsync(CancellationToken ct = default)
    {
        if (_cached is not null)
        {
            return _cached;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (_cached is not null)
            {
                return _cached;
            }

            string? existing = null;
            try
            {
                existing = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", ct, StorageKey);
            }
            catch
            {
                // Pre-rendering or no localStorage — fall through to mint
                // an in-memory token. It won't survive a reload but the
                // current chat session still works.
            }

            if (!string.IsNullOrWhiteSpace(existing) && IsValid(existing))
            {
                _cached = existing!;
                return _cached;
            }

            var token = Mint();
            try
            {
                await _jsRuntime.InvokeVoidAsync("localStorage.setItem", ct, StorageKey, token);
            }
            catch
            {
                // Same as above — best effort.
            }

            _cached = token;
            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool IsValid(string token)
    {
        if (token.Length is < 8 or > 128) return false;
        foreach (var c in token)
        {
            // Match the BFF accept-list: A–Z, a–z, 0–9, _, -.
            var ok = (c >= 'A' && c <= 'Z')
                || (c >= 'a' && c <= 'z')
                || (c >= '0' && c <= '9')
                || c == '_' || c == '-';
            if (!ok) return false;
        }
        return true;
    }

    private static string Mint()
    {
        Span<byte> bytes = stackalloc byte[TokenLength];
        RandomNumberGenerator.Fill(bytes);
        Span<char> chars = stackalloc char[TokenLength];
        for (var i = 0; i < TokenLength; i++)
        {
            chars[i] = Charset[bytes[i] & 0x1F]; // 5 bits → one char of base32
        }
        return new string(chars);
    }
}
