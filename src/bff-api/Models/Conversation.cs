namespace Contoso.BffApi.Models;

public sealed class Conversation
{
    public string Id { get; init; } = string.Empty;

    public string CustomerId { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    public List<ChatMessage> Messages { get; init; } = new();
}
