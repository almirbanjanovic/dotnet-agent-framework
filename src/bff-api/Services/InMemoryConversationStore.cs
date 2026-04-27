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
        return Task.FromResult(conversation);
    }

    public Task<Conversation?> GetConversationAsync(string conversationId, CancellationToken ct = default)
    {
        return Task.FromResult(_conversations.TryGetValue(conversationId, out var conversation)
            ? conversation
            : null);
    }

    public Task<IReadOnlyList<Conversation>> GetConversationsByCustomerAsync(string customerId, CancellationToken ct = default)
    {
        var conversations = _conversations.Values
            .Where(conversation => string.Equals(conversation.CustomerId, customerId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(conversation => conversation.CreatedAt)
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
            }
        }

        return Task.CompletedTask;
    }
}
