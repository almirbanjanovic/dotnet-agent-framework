using System.Text.RegularExpressions;

namespace Contoso.BffApi.Services;

// Single source of truth for "is this customer id a guest (anonymous)
// session?". The convention — guest- prefix on the resolved customer id
// — is repeated in each service (per the architecture's
// component-independence rule); this file is the BFF's copy.
//
// Guest sessions are minted by the Blazor UI (random 16-char alphanumeric)
// and round-tripped via the X-Guest-Session-Id header so an anonymous
// visitor can hold a conversation without signing in. They are *not* a
// security boundary — anyone can fabricate a guest id — and so MUST
// only be used for features that don't require the caller to be a
// specific human (general Q&A, catalog Q&A). Anything customer-specific
// (orders, profile, returns) MUST refuse a guest id.
public static class GuestId
{
    public const string Prefix = "guest-";

    // Bound the header on both ends: too short collides too easily, too
    // long is an obvious attempt to abuse the per-id rate-limit partition.
    private const int MinSessionLength = 8;
    private const int MaxSessionLength = 128;

    private static readonly Regex SessionPattern = new(
        "^[A-Za-z0-9_-]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(50));

    public static bool IsGuest(string? customerId) =>
        !string.IsNullOrEmpty(customerId)
        && customerId.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>
    /// Validates and converts an opaque session token from the
    /// X-Guest-Session-Id header into a canonical guest customer id.
    /// Returns null if the token is missing or malformed.
    /// </summary>
    public static string? FromHeader(string? sessionToken)
    {
        if (string.IsNullOrWhiteSpace(sessionToken)) return null;
        if (sessionToken.Length < MinSessionLength || sessionToken.Length > MaxSessionLength) return null;
        if (!SessionPattern.IsMatch(sessionToken)) return null;
        return Prefix + sessionToken;
    }
}
