using System.Linq;
using FluentAssertions;

namespace Contoso.CrmAgent.Tests;

// Locks down the SSE `tool_result` preview contract emitted by the CRM
// agent's ChatEndpoint:
//   * MaxPreviewChars = 512.
//   * String results pass through verbatim (no extra JSON quoting).
//   * Object results are JSON-serialized.
//   * Truncation never slices through a UTF-16 surrogate pair.
//   * Render never throws; serialization failures degrade to a
//     parenthesized marker string.
public class ToolResultPreviewTests
{
    [Fact]
    public void Render_NullResult_ReturnsPlaceholderNotTruncated()
    {
        var (preview, truncated) = ToolResultPreview.Render(null);

        preview.Should().Be("(no result)");
        truncated.Should().BeFalse();
    }

    [Fact]
    public void Render_ShortString_PassesThroughUnchanged()
    {
        var (preview, truncated) = ToolResultPreview.Render("Hello, world!");

        preview.Should().Be("Hello, world!");
        truncated.Should().BeFalse();
    }

    [Fact]
    public void Render_LongString_TruncatesAndFlags()
    {
        var input = new string('A', 800);

        var (preview, truncated) = ToolResultPreview.Render(input);

        truncated.Should().BeTrue();
        preview.Length.Should().Be(512);
    }

    [Fact]
    public void Render_StringEndingInHighSurrogateAtCut_BacksOffOneChar()
    {
        // U+1F600 (😀) is a surrogate pair (D83D DE00). Build a string
        // where the cut at index 512 lands on the HIGH surrogate, so the
        // truncator must back off one char to avoid emitting a lone
        // high surrogate.
        var prefix = new string('A', 511);
        var input = prefix + "\uD83D\uDE00" + new string('B', 100);
        // Length so far: 511 + 2 + 100 = 613. At index 512 we have the
        // high surrogate (input[511]==prefix end, input[512]=='\uDE00')...
        // Wait: zero-indexed prefix occupies [0..510]; input[511]=='\uD83D'.
        // The naive cut takes Substring(0, 512) which keeps input[0..511]
        // — the lone HIGH surrogate '\uD83D'. Our guard backs off to 511.

        var (preview, truncated) = ToolResultPreview.Render(input);

        truncated.Should().BeTrue();
        // Length must be 511 (clean break before the surrogate pair).
        preview.Length.Should().Be(511);
        // Last char must NOT be a lone high surrogate.
        char.IsHighSurrogate(preview[^1]).Should().BeFalse();
    }

    [Fact]
    public void Render_StringEndingInCompleteSurrogatePairBeforeCut_StaysIntact()
    {
        // Surrogate pair fully INSIDE the kept range — must not be touched.
        var prefix = new string('A', 510);
        var input = prefix + "\uD83D\uDE00" + new string('B', 100);
        // Pair occupies [510..511]; cut at 512 is past the pair. Output
        // length should be 512 with the pair intact.

        var (preview, truncated) = ToolResultPreview.Render(input);

        truncated.Should().BeTrue();
        preview.Length.Should().Be(512);
        // Last two chars are the intact pair.
        preview[^2].Should().Be('\uD83D');
        preview[^1].Should().Be('\uDE00');
    }

    [Fact]
    public void Render_DictionaryResult_SerializesAsJson()
    {
        var result = new Dictionary<string, object?>
        {
            ["orderId"] = "ORD-123",
            ["status"] = "Shipped",
            ["amount"] = 49.99m,
        };

        var (preview, truncated) = ToolResultPreview.Render(result);

        truncated.Should().BeFalse();
        // No specific shape assertions — STJ ordering for IDictionary is
        // documented as insertion order. Key/value substrings are enough
        // to confirm we're emitting JSON, not ToString().
        preview.Should().Contain("\"orderId\":\"ORD-123\"");
        preview.Should().Contain("\"status\":\"Shipped\"");
        preview.Should().Contain("\"amount\":49.99");
    }

    [Fact]
    public void Render_LongJsonObject_TruncatedFlagIsTrue()
    {
        // 60 entries × ~20 chars each ⇒ well over 512 chars of JSON.
        var result = Enumerable.Range(0, 60)
            .ToDictionary(i => $"k{i}", i => (object?)$"value-{i:D3}");

        var (preview, truncated) = ToolResultPreview.Render(result);

        truncated.Should().BeTrue();
        preview.Length.Should().BeLessThanOrEqualTo(512);
    }

    [Fact]
    public void Render_EmptyString_PassesThroughUnchanged()
    {
        var (preview, truncated) = ToolResultPreview.Render(string.Empty);

        preview.Should().Be(string.Empty);
        truncated.Should().BeFalse();
    }
}
