using System.Linq;
using System.Text.Json.Serialization;
using Contoso.BffApi.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;

namespace Contoso.BffApi.Services;

public sealed class CosmosConversationStore : IConversationStore
{
    private readonly Container _container;

    public CosmosConversationStore(CosmosClient client, IConfiguration configuration)
    {
        var databaseName = configuration["CosmosDb:AgentsDatabase"] ?? "contoso-agents";
        var containerName = configuration["CosmosDb:AgentsContainer"] ?? "conversations";
        _container = client.GetContainer(databaseName, containerName);
    }

    public async Task<Conversation> CreateConversationAsync(string customerId, CancellationToken ct = default)
    {
        var conversation = new Conversation
        {
            Id = Guid.NewGuid().ToString("N"),
            CustomerId = customerId,
            CreatedAt = DateTimeOffset.UtcNow,
            Messages = new List<ChatMessage>()
        };

        var document = ConversationDocument.FromConversation(conversation);
        await _container.CreateItemAsync(document, new PartitionKey(document.SessionId), cancellationToken: ct);
        return conversation;
    }

    public async Task<Conversation?> GetConversationAsync(string conversationId, CancellationToken ct = default)
    {
        try
        {
            var response = await _container.ReadItemAsync<ConversationDocument>(
                conversationId,
                new PartitionKey(conversationId),
                cancellationToken: ct);

            return response.Resource.ToConversation();
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<Conversation>> GetConversationsByCustomerAsync(string customerId, CancellationToken ct = default)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.customerId = @customerId")
            .WithParameter("@customerId", customerId);

        var results = new List<Conversation>();
        using var iterator = _container.GetItemQueryIterator<ConversationDocument>(query);

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(ct);
            results.AddRange(page.Select(document => document.ToConversation()));
        }

        return results
            .OrderByDescending(conversation => conversation.CreatedAt)
            .ToList();
    }

    public async Task AddMessageAsync(string conversationId, ChatMessage message, CancellationToken ct = default)
    {
        var document = await GetConversationDocumentAsync(conversationId, ct);
        if (document is null)
        {
            return;
        }

        document.Messages.Add(message);
        await _container.ReplaceItemAsync(
            document,
            document.Id,
            new PartitionKey(document.SessionId),
            cancellationToken: ct);
    }

    private async Task<ConversationDocument?> GetConversationDocumentAsync(string conversationId, CancellationToken ct)
    {
        try
        {
            var response = await _container.ReadItemAsync<ConversationDocument>(
                conversationId,
                new PartitionKey(conversationId),
                cancellationToken: ct);

            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private sealed class ConversationDocument
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("sessionId")]
        public string SessionId { get; init; } = string.Empty;

        [JsonPropertyName("customerId")]
        public string CustomerId { get; init; } = string.Empty;

        [JsonPropertyName("createdAt")]
        public DateTimeOffset CreatedAt { get; init; }

        [JsonPropertyName("messages")]
        public List<ChatMessage> Messages { get; init; } = new();

        public Conversation ToConversation() => new()
        {
            Id = Id,
            CustomerId = CustomerId,
            CreatedAt = CreatedAt,
            Messages = new List<ChatMessage>(Messages)
        };

        public static ConversationDocument FromConversation(Conversation conversation) => new()
        {
            Id = conversation.Id,
            SessionId = conversation.Id,
            CustomerId = conversation.CustomerId,
            CreatedAt = conversation.CreatedAt,
            Messages = new List<ChatMessage>(conversation.Messages)
        };
    }
}
