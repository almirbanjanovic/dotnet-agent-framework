namespace Contoso.BlazorUi.Models;

public sealed record ChatRequest(string Message, string? ConversationId);
