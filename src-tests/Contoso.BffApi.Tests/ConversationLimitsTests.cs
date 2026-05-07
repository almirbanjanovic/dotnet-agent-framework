using Contoso.BffApi.Services;
using FluentAssertions;

namespace Contoso.BffApi.Tests;

public class ConversationLimitsTests
{
    [Fact]
    public void ExceedsMessageLimit_NullOrEmpty_ReturnsFalse()
    {
        ConversationLimits.ExceedsMessageLimit(null).Should().BeFalse();
        ConversationLimits.ExceedsMessageLimit(string.Empty).Should().BeFalse();
    }

    [Fact]
    public void ExceedsMessageLimit_SmallMessage_ReturnsFalse()
    {
        ConversationLimits.ExceedsMessageLimit("hello world").Should().BeFalse();
    }

    [Fact]
    public void ExceedsMessageLimit_AtBoundary_ReturnsFalse()
    {
        // Exactly MaxMessageContentBytes worth of single-byte UTF-8 chars.
        var content = new string('a', ConversationLimits.MaxMessageContentBytes);

        ConversationLimits.ExceedsMessageLimit(content).Should().BeFalse();
    }

    [Fact]
    public void ExceedsMessageLimit_OverBoundary_ReturnsTrue()
    {
        var content = new string('a', ConversationLimits.MaxMessageContentBytes + 1);

        ConversationLimits.ExceedsMessageLimit(content).Should().BeTrue();
    }

    [Fact]
    public void ExceedsMessageLimit_MultiByteUtf8_CountsBytes()
    {
        // Each '€' is 3 bytes in UTF-8. Crafting a string whose char count
        // is below the cap but whose byte count exceeds it must still be
        // rejected — otherwise i18n payloads bypass the bound.
        var perCharBytes = 3;
        var charCount = (ConversationLimits.MaxMessageContentBytes / perCharBytes) + 1;
        var content = new string('€', charCount);

        // Sanity: char length is well below cap so the fast-path can't
        // short-circuit — we exercise the actual UTF-8 byte count branch.
        content.Length.Should().BeLessThan(ConversationLimits.MaxMessageContentBytes);
        ConversationLimits.ExceedsMessageLimit(content).Should().BeTrue();
    }

    [Fact]
    public void TrimOldest_BelowCap_NoOp()
    {
        var messages = Enumerable.Range(0, 5)
            .Select(i => new Models.ChatMessage("user", $"m{i}", DateTimeOffset.UtcNow))
            .ToList();

        ConversationLimits.TrimOldest(messages);

        messages.Should().HaveCount(5);
    }

    [Fact]
    public void TrimOldest_OverCap_DropsOldestFirst()
    {
        var overflow = ConversationLimits.MaxStoredMessagesPerConversation + 7;
        var messages = Enumerable.Range(0, overflow)
            .Select(i => new Models.ChatMessage("user", $"m{i}", DateTimeOffset.UtcNow))
            .ToList();

        ConversationLimits.TrimOldest(messages);

        messages.Should().HaveCount(ConversationLimits.MaxStoredMessagesPerConversation);
        messages.First().Content.Should().Be("m7");
        messages.Last().Content.Should().Be($"m{overflow - 1}");
    }

    [Fact]
    public void TrimOldest_OverByteBudget_DropsOldestUntilUnderBudget()
    {
        // A handful of large messages can blow MaxStoredContentBytes long
        // before MaxStoredMessagesPerConversation fires. The byte cap
        // must drop oldest until total UTF-8 bytes fit, while always
        // preserving the most recent message.
        var bigMessage = new string('a', ConversationLimits.MaxMessageContentBytes); // 64 KiB
        var seedTime = DateTimeOffset.UtcNow;
        var messages = Enumerable.Range(0, 30)
            .Select(i => new Models.ChatMessage("user", bigMessage, seedTime.AddSeconds(i)))
            .ToList();

        // Pre-trim total bytes ~= 30 * 64 KiB = 1.875 MiB > MaxStoredContentBytes (1.43 MiB)
        ConversationLimits.TrimOldest(messages);

        // After trim, total content bytes must be at or under the budget.
        var totalBytes = messages.Sum(m => System.Text.Encoding.UTF8.GetByteCount(m.Content));
        totalBytes.Should().BeLessThanOrEqualTo(ConversationLimits.MaxStoredContentBytes);
        // Most recent message MUST be retained.
        messages.Should().NotBeEmpty();
        messages.Last().Timestamp.Should().Be(seedTime.AddSeconds(29));
    }

    [Fact]
    public void SelectHistoryForOrchestrator_OverByteBudget_KeepsMostRecent()
    {
        // The orchestrator forwarder must enforce a byte budget so that
        // a few large past turns don't blow the LLM context window even
        // when message count is well below the count cap.
        var bigContent = new string('z', 64 * 1024); // 64 KiB
        var messages = Enumerable.Range(0, 10)
            .Select(i => new Models.ChatMessage("user", bigContent, DateTimeOffset.UtcNow.AddSeconds(i)))
            .ToList();

        var window = ConversationLimits.SelectHistoryForOrchestrator(messages);
        var totalBytes = window.Sum(m => System.Text.Encoding.UTF8.GetByteCount(m.Content));

        totalBytes.Should().BeLessThanOrEqualTo(ConversationLimits.MaxHistoryContentBytes);
        // Returned in original (oldest-to-newest) order.
        for (int i = 1; i < window.Count; i++)
        {
            window[i].Timestamp.Should().BeOnOrAfter(window[i - 1].Timestamp);
        }
        // The newest message must be in the window.
        window.Last().Timestamp.Should().Be(messages.Last().Timestamp);
    }

    [Fact]
    public void SelectHistoryForOrchestrator_FiltersEmptyContent()
    {
        var messages = new List<Models.ChatMessage>
        {
            new("user", "hello", DateTimeOffset.UtcNow.AddSeconds(1)),
            new("assistant", string.Empty, DateTimeOffset.UtcNow.AddSeconds(2)),
            new("user", "world", DateTimeOffset.UtcNow.AddSeconds(3))
        };

        var window = ConversationLimits.SelectHistoryForOrchestrator(messages);

        window.Should().HaveCount(2);
        window.Select(m => m.Content).Should().Equal("hello", "world");
    }

    [Fact]
    public void SelectHistoryForOrchestrator_BoundedByCount()
    {
        var messages = Enumerable.Range(0, ConversationLimits.MaxHistoryMessagesForOrchestrator + 25)
            .Select(i => new Models.ChatMessage("user", $"m{i}", DateTimeOffset.UtcNow.AddSeconds(i)))
            .ToList();

        var window = ConversationLimits.SelectHistoryForOrchestrator(messages);

        window.Should().HaveCount(ConversationLimits.MaxHistoryMessagesForOrchestrator);
        // Most recent retained.
        window.Last().Content.Should().Be(messages.Last().Content);
    }
}
