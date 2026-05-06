using System.Collections.Generic;

namespace Contoso.BlazorUi.Models;

// Strongly-typed view of one Server-Sent Event yielded from BffApiClient.
// `Event` is the SSE event name; `Data` is the JSON payload (parsed by
// the consumer based on Event).
//
// The server emits these events for /api/v1/chat/stream:
//   conversation : { conversationId }                    — first event
//   stage        : { stage, agent? }                     — classifying / routed
//   token        : { text }                              — incremental token
//   tool         : { name, arguments }                   — agent tool call
//   error        : { message }                           — failure
//   done         : { conversationId, toolCalls }         — terminal event

public sealed record ChatStreamEvent(string Event, string Data);

public sealed record ConversationStartData(string ConversationId);

public sealed record StageData(string Stage, string? Agent);

public sealed record TokenData(string Text);

public sealed record ToolData(string Name, IReadOnlyDictionary<string, object?>? Arguments);

public sealed record ErrorData(string Message);

public sealed record DoneData(string ConversationId, IReadOnlyList<ToolCallInfo> ToolCalls);
