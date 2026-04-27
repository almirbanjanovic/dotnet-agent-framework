namespace Contoso.BlazorUi.Models;

public sealed record ChatMessage(string Role, string Content, DateTimeOffset Timestamp);
