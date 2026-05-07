// Component-independent copy of the GuestId helper. Mirrors the BFF's
// implementation (see src/bff-api/Services/GuestId.cs). Per the
// architecture rule, services do NOT share code via project references.
internal static class GuestId
{
    public const string Prefix = "guest-";

    public static bool IsGuest(string? customerId) =>
        !string.IsNullOrEmpty(customerId)
        && customerId.StartsWith(Prefix, StringComparison.Ordinal);

    // Appended to the system prompt when the caller is anonymous. Tells
    // the model to refuse account/order tool calls and to suggest the
    // visitor sign in for anything customer-specific. This is a
    // soft guardrail; the hard guardrail is that the orchestrator
    // routes guests here (CRM tools are also stripped from the tool
    // set in the endpoint), and the CRM agent itself 403s guest ids.
    public const string AnonymousGuardrail =
        "\n\n--- ANONYMOUS GUEST SESSION ---\n" +
        "The current visitor is NOT signed in. Their CustomerId starts with 'guest-' and " +
        "does NOT correspond to a real customer record. You MUST:\n" +
        "  - Refuse to call any tool that needs customer/account context (orders, profile, " +
        "returns, addresses, payment, support tickets).\n" +
        "  - For account-specific questions, politely ask the visitor to sign in.\n" +
        "  - Continue to help with general product questions, recommendations, the return " +
        "policy, sizing, and FAQs.\n" +
        "  - Never invent a customer name, order id, or account detail.";
}
