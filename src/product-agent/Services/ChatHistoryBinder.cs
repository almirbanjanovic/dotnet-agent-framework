using Microsoft.Extensions.AI;

// Translates the wire-format `HistoryMessage[]` from the orchestrator into
// the `ChatMessage` list the Agent Framework expects, and appends the
// new user turn with the customer ID prefix so MCP tools have the
// identity in-band (no need to thread CustomerId through every tool call).

internal static class ChatHistoryBinder
{
    public static IList<ChatMessage> Build(
        IReadOnlyList<HistoryMessage>? history,
        string customerId,
        string message)
    {
        var messages = new List<ChatMessage>();

        if (history is { Count: > 0 })
        {
            foreach (var entry in history)
            {
                var role = string.Equals(entry.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                    ? ChatRole.Assistant
                    : ChatRole.User;
                messages.Add(new ChatMessage(role, entry.Content));
            }
        }

        messages.Add(new ChatMessage(ChatRole.User, $"CustomerId: {customerId}\nMessage: {message}"));
        return messages;
    }
}
