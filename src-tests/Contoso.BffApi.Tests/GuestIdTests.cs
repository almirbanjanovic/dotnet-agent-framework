using Contoso.BffApi.Services;
using FluentAssertions;

namespace Contoso.BffApi.Tests;

public class GuestIdTests
{
    [Theory]
    [InlineData("guest-abc12345", true)]
    [InlineData("guest-", true)]               // prefix-only is still a guest
    [InlineData("Guest-abc", false)]            // case-sensitive
    [InlineData("cust-1", false)]
    [InlineData("101", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsGuest_RecognisesPrefix(string? input, bool expected)
    {
        GuestId.IsGuest(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("ABCDEFGH", "guest-ABCDEFGH")]
    [InlineData("a-b_c-12345", "guest-a-b_c-12345")]
    [InlineData("ABCDEFGHIJKLMNOP", "guest-ABCDEFGHIJKLMNOP")]
    public void FromHeader_WrapsValidTokens(string token, string expected)
    {
        GuestId.FromHeader(token).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("short")]                        // too short (<8 chars)
    [InlineData("abc def 12")]                   // contains space
    [InlineData("abc;def;12")]                   // contains semicolon (header injection attempt)
    [InlineData("abc\rdef\n12")]                 // CRLF (header injection)
    public void FromHeader_RejectsMalformed(string? token)
    {
        GuestId.FromHeader(token).Should().BeNull();
    }

    [Fact]
    public void FromHeader_RejectsTooLong()
    {
        // 129 chars — over the 128 cap.
        var token = new string('a', 129);
        GuestId.FromHeader(token).Should().BeNull();
    }
}
