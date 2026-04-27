namespace Contoso.BffApi.Models;

public sealed record ChatRequest(string Message, string? ConversationId);
