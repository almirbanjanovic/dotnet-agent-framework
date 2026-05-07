using System.Collections.Concurrent;
using System.Linq;
using Contoso.BffApi.Models;

namespace Contoso.BffApi.Services;

public sealed class InMemoryConversationStore : IConversationStore
{
    private readonly ConcurrentDictionary<string, Conversation> _conversations = new(StringComparer.OrdinalIgnoreCase);

    public Task<Conversation> CreateConversationAsync(string customerId, CancellationToken ct = default)
    {
        var conversation = new Conversation
        {
            Id = Guid.NewGuid().ToString("N"),
            CustomerId = customerId,
            CreatedAt = DateTimeOffset.UtcNow,
            Messages = new List<ChatMessage>()
        };

        _conversations[conversation.Id] = conversation;
        return Task.FromResult(Snapshot(conversation));
    }

    public Task<Conversation?> GetConversationAsync(string conversationId, CancellationToken ct = default)
    {
        if (!_conversations.TryGetValue(conversationId, out var conversation))
        {
            return Task.FromResult<Conversation?>(null);
        }

        return Task.FromResult<Conversation?>(Snapshot(conversation));
    }

    public Task<IReadOnlyList<Conversation>> GetConversationsByCustomerAsync(string customerId, CancellationToken ct = default)
    {
        var conversations = _conversations.Values
            .Where(conversation => string.Equals(conversation.CustomerId, customerId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(conversation => conversation.CreatedAt)
            .Select(Snapshot)
            .ToList();

        return Task.FromResult<IReadOnlyList<Conversation>>(conversations);
    }

    public Task AddMessageAsync(string conversationId, ChatMessage message, CancellationToken ct = default)
    {
        if (_conversations.TryGetValue(conversationId, out var conversation))
        {
            lock (conversation.Messages)
            {
                conversation.Messages.Add(message);
                // Bound storage so a chatty client cannot grow the
                // conversation indefinitely. See ConversationLimits for
                // rationale.
                ConversationLimits.TrimOldest(conversation.Messages);
            }
        }

        return Task.CompletedTask;
    }

    // Hand callers a defensive copy so they can iterate `Messages` without
    // racing with `AddMessageAsync` (writers hold `lock(conversation.Messages)`,
    // but ChatEndpoint readers don't take that lock — without snapshotting,
    // two concurrent requests on the same conversation race to throw
    // `InvalidOperationException: Collection was modified...` mid-LINQ).
    private static Conversation Snapshot(Conversation conversation)
    {
        List<ChatMessage> messages;
        lock (conversation.Messages)
        {
            messages = new List<ChatMessage>(conversation.Messages);
        }

        return new Conversation
        {
            Id = conversation.Id,
            CustomerId = conversation.CustomerId,
            CreatedAt = conversation.CreatedAt,
            Messages = messages
        };
    }
}
