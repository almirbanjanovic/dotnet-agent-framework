using Contoso.BffApi.Models;

namespace Contoso.BffApi.Services;

public interface IConversationStore
{
    Task<Conversation> CreateConversationAsync(string customerId, CancellationToken ct = default);
    Task<Conversation?> GetConversationAsync(string conversationId, CancellationToken ct = default);
    Task<IReadOnlyList<Conversation>> GetConversationsByCustomerAsync(string customerId, CancellationToken ct = default);
    Task AddMessageAsync(string conversationId, ChatMessage message, CancellationToken ct = default);
}
