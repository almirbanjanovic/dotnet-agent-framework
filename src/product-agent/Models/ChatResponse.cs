// Outbound chat response. `ToolCalls` lets the BFF surface (and the UI
// render) which MCP tools the agent invoked while answering — useful both
// for transparency in the UI and for assertions in tests.

internal sealed record ChatResponse(string Response, IReadOnlyList<ToolCallInfo> ToolCalls);

internal sealed record ToolCallInfo(string Name, IReadOnlyDictionary<string, object?> Arguments);
