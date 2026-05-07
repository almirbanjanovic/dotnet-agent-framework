using System.Text.RegularExpressions;

namespace Contoso.OrchestratorAgent.Services;

// Component-independent copy of the GuestId helper. Mirrors the BFF's
// implementation (see src/bff-api/Services/GuestId.cs). Per the
// architecture rule, services do NOT share code via project references —
// the same convention is inlined into every consumer.
internal static class GuestId
{
    public const string Prefix = "guest-";

    public static bool IsGuest(string? customerId) =>
        !string.IsNullOrEmpty(customerId)
        && customerId.StartsWith(Prefix, StringComparison.Ordinal);
}
