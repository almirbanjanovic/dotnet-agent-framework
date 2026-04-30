namespace Contoso.OrchestratorAgent.Models;

public sealed record ChatRequest(
    string CustomerId,
    string Message,
    IReadOnlyList<HistoryMessage>? History = null);

public sealed record HistoryMessage(string Role, string Content);
