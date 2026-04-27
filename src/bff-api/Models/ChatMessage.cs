namespace Contoso.BffApi.Models;

public sealed record ChatMessage(string Role, string Content, DateTimeOffset Timestamp);
