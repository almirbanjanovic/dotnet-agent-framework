using Contoso.BffApi.Models;

namespace Contoso.BffApi.Services;

/// <summary>
/// Bounds applied to conversation persistence and to the history we
/// forward to the orchestrator. Without these, a chatty client can grow
/// a single conversation past Cosmos's 2 MB document limit (which then
/// 413s on every subsequent write), and a long-lived conversation
/// monotonically inflates the LLM context window — driving cost and
/// latency until the model truncates and starts hallucinating.
/// </summary>
public static class ConversationLimits
{
    /// <summary>
    /// Maximum bytes (UTF-8) allowed in a single chat message body
    /// submitted to the BFF. Anything larger is rejected with
    /// HTTP 413 Payload Too Large. Generous on purpose — the only
    /// legitimate reason to exceed this is a pasted document, and
    /// that's better handled by uploading to Knowledge MCP.
    /// </summary>
    public const int MaxMessageContentBytes = 64 * 1024;

    /// <summary>
    /// Maximum number of messages we KEEP in storage per conversation.
    /// When AddMessageAsync would push past this, the oldest messages
    /// are dropped (FIFO).
    /// </summary>
    public const int MaxStoredMessagesPerConversation = 200;

    /// <summary>
    /// Maximum total bytes (UTF-8 message content) we KEEP in storage
    /// per conversation. Sized well below Cosmos's 2 MB hard per-item
    /// limit to leave room for envelope/metadata. Trim drops oldest
    /// messages until the budget fits — protects against the worst case
    /// where a few near-MaxMessageContentBytes messages would otherwise
    /// blow the document limit before the count cap fires.
    /// </summary>
    public const int MaxStoredContentBytes = 1_500_000; // ~1.43 MiB

    /// <summary>
    /// Maximum number of recent messages we replay to the orchestrator
    /// on each turn. Smaller than MaxStoredMessagesPerConversation so
    /// that even when storage is full, the LLM context stays bounded.
    /// </summary>
    public const int MaxHistoryMessagesForOrchestrator = 100;

    /// <summary>
    /// Maximum total bytes (UTF-8 content) we replay to the orchestrator
    /// per turn. Acts as a budget that bounds LLM context cost / latency
    /// independently of message count. Sized well below typical 128k-
    /// token model context windows.
    /// </summary>
    public const int MaxHistoryContentBytes = 256 * 1024; // 256 KiB

    /// <summary>
    /// Drops oldest messages from <paramref name="messages"/> in place
    /// until both the count and total-byte budgets are satisfied. No-op
    /// when already within bounds.
    /// </summary>
    public static void TrimOldest(List<ChatMessage> messages)
    {
        // Count cap.
        var excess = messages.Count - MaxStoredMessagesPerConversation;
        if (excess > 0)
        {
            messages.RemoveRange(0, excess);
        }

        // Byte cap. Walk forward from the head, dropping until the tail
        // fits under MaxStoredContentBytes. We never drop the most recent
        // message because it has just been appended by the caller.
        if (messages.Count == 0)
        {
            return;
        }
        long totalBytes = 0;
        for (int i = 0; i < messages.Count; i++)
        {
            totalBytes += System.Text.Encoding.UTF8.GetByteCount(messages[i].Content ?? string.Empty);
        }
        var dropFrom = 0;
        while (totalBytes > MaxStoredContentBytes && dropFrom < messages.Count - 1)
        {
            totalBytes -= System.Text.Encoding.UTF8.GetByteCount(messages[dropFrom].Content ?? string.Empty);
            dropFrom++;
        }
        if (dropFrom > 0)
        {
            messages.RemoveRange(0, dropFrom);
        }
    }

    /// <summary>
    /// Returns the most recent N messages (where N is bounded by both
    /// <see cref="MaxHistoryMessagesForOrchestrator"/> and
    /// <see cref="MaxHistoryContentBytes"/>) from <paramref name="messages"/>,
    /// in original order. Filters out messages with empty content.
    /// </summary>
    public static IReadOnlyList<ChatMessage> SelectHistoryForOrchestrator(IReadOnlyList<ChatMessage> messages)
    {
        if (messages.Count == 0)
        {
            return Array.Empty<ChatMessage>();
        }

        var window = new List<ChatMessage>(Math.Min(messages.Count, MaxHistoryMessagesForOrchestrator));
        long byteBudget = MaxHistoryContentBytes;
        // Walk newest-to-oldest; stop once we hit either bound. This way
        // we always keep the *most recent* turns, which is what matters
        // for LLM coherence.
        for (int i = messages.Count - 1; i >= 0 && window.Count < MaxHistoryMessagesForOrchestrator; i--)
        {
            var msg = messages[i];
            if (string.IsNullOrEmpty(msg.Content))
            {
                continue;
            }
            var bytes = System.Text.Encoding.UTF8.GetByteCount(msg.Content);
            if (bytes > byteBudget)
            {
                break;
            }
            byteBudget -= bytes;
            window.Add(msg);
        }
        window.Reverse();
        return window;
    }

    /// <summary>
    /// Returns true when <paramref name="message"/> exceeds
    /// <see cref="MaxMessageContentBytes"/> in UTF-8.
    /// </summary>
    public static bool ExceedsMessageLimit(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return false;
        }

        // Avoid full UTF-8 encode when char count alone proves we're
        // safe (UTF-8 max = 4 bytes/char) or already over (UTF-8 min = 1
        // byte/char).
        if (message.Length * 4 <= MaxMessageContentBytes)
        {
            return false;
        }
        if (message.Length > MaxMessageContentBytes)
        {
            return true;
        }

        return System.Text.Encoding.UTF8.GetByteCount(message) > MaxMessageContentBytes;
    }
}
