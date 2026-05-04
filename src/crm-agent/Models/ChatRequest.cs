// Inbound chat payload accepted by the CRM Agent's /api/v1/chat endpoint.
// Same shape as the Orchestrator's ChatRequest so the orchestrator can
// forward unchanged. Kept in the global namespace because the test suite
// (Contoso.CrmAgent.Tests) references it without a `using`.

internal sealed record ChatRequest(
    string CustomerId,
    string Message,
    IReadOnlyList<HistoryMessage>? History = null);

internal sealed record HistoryMessage(string Role, string Content);
