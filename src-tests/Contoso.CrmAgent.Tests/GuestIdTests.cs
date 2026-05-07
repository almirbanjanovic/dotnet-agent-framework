using FluentAssertions;

namespace Contoso.CrmAgent.Tests;

// Mirrors src/crm-agent/Services/GuestId.cs — see that file for why
// this convention is duplicated per-service rather than shared.
public sealed class GuestIdTests
{
    [Theory]
    [InlineData("guest-abc", true)]
    [InlineData("guest-", true)]
    [InlineData("Guest-abc", false)]
    [InlineData("cust-1", false)]
    [InlineData("101", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsGuest_RecognisesPrefix(string? input, bool expected)
    {
        GuestId.IsGuest(input).Should().Be(expected);
    }
}
