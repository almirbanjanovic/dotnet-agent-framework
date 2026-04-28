using Contoso.BffApi.Models;
using Contoso.BffApi.Services;
using FluentAssertions;

namespace Contoso.BffApi.Tests;

public class InMemoryConversationStoreTests
{
    [Fact]
    public async Task CreateConversationAsync_ReturnsNewConversation()
    {
        var store = new InMemoryConversationStore();

        var conversation = await store.CreateConversationAsync("cust-1");

        conversation.Id.Should().NotBeNullOrWhiteSpace();
        conversation.CustomerId.Should().Be("cust-1");
        conversation.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task GetConversationAsync_ExistingId_ReturnsConversation()
    {
        var store = new InMemoryConversationStore();
        var created = await store.CreateConversationAsync("cust-1");

        var conversation = await store.GetConversationAsync(created.Id);

        conversation.Should().NotBeNull();
        conversation!.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task GetConversationAsync_UnknownId_ReturnsNull()
    {
        var store = new InMemoryConversationStore();

        var conversation = await store.GetConversationAsync("missing");

        conversation.Should().BeNull();
    }

    [Fact]
    public async Task GetConversationsByCustomerAsync_FiltersCorrectly()
    {
        var store = new InMemoryConversationStore();
        var custA = await store.CreateConversationAsync("cust-a");
        var custB = await store.CreateConversationAsync("cust-b");

        var conversations = await store.GetConversationsByCustomerAsync("cust-a");

        conversations.Should().ContainSingle();
        conversations[0].Id.Should().Be(custA.Id);
        conversations.Should().NotContain(c => c.Id == custB.Id);
    }

    [Fact]
    public async Task GetConversationsByCustomerAsync_OrdersByCreatedAtDesc()
    {
        var store = new InMemoryConversationStore();
        var first = await store.CreateConversationAsync("cust-1");
        await Task.Delay(10);
        var second = await store.CreateConversationAsync("cust-1");

        var conversations = await store.GetConversationsByCustomerAsync("cust-1");

        conversations.Should().HaveCount(2);
        conversations[0].Id.Should().Be(second.Id);
        conversations[1].Id.Should().Be(first.Id);
    }

    [Fact]
    public async Task AddMessageAsync_ValidConversation_AppendsMessage()
    {
        var store = new InMemoryConversationStore();
        var conversation = await store.CreateConversationAsync("cust-1");
        var message = new ChatMessage("user", "hello", DateTimeOffset.UtcNow);

        await store.AddMessageAsync(conversation.Id, message);
        var updated = await store.GetConversationAsync(conversation.Id);

        updated!.Messages.Should().ContainSingle();
        updated.Messages[0].Content.Should().Be("hello");
    }

    [Fact]
    public async Task AddMessageAsync_ConcurrentWrites_NoDataLoss()
    {
        var store = new InMemoryConversationStore();
        var conversation = await store.CreateConversationAsync("cust-1");

        var tasks = Enumerable.Range(0, 100)
            .Select(index => store.AddMessageAsync(
                conversation.Id,
                new ChatMessage("user", $"message-{index}", DateTimeOffset.UtcNow)));

        await Task.WhenAll(tasks);
        var updated = await store.GetConversationAsync(conversation.Id);

        updated!.Messages.Should().HaveCount(100);
    }
}
