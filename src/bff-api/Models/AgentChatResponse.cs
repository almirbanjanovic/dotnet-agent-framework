namespace Contoso.BffApi.Models;

// Wire-format DTO for the orchestrator's chat response. The BFF deserialises
// it to extract the assistant text + tool calls, then re-projects them into
// the public ChatResponse for the UI.

internal sealed record AgentChatResponse(string Response, IReadOnlyList<ToolCallInfo> ToolCalls);
