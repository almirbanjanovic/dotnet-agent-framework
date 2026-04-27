namespace Contoso.BlazorUi.Models;

public sealed record ChatResponse(string ConversationId, string Response, IReadOnlyList<ToolCallInfo> ToolCalls);

public sealed record ToolCallInfo(string Name, IReadOnlyDictionary<string, object?> Arguments);
