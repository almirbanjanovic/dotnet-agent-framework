using FluentAssertions;

namespace Contoso.ProductAgent.Tests;

// Mirrors src/product-agent/Services/GuestId.cs — see that file for why
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

    [Fact]
    public void AnonymousGuardrail_MentionsKeyConstraints()
    {
        // Smoke check the prompt suffix actually carries the rules we
        // rely on: refusal of customer/account tools, instruction to
        // ask for sign-in, and a no-hallucination clause.
        GuestId.AnonymousGuardrail.Should().Contain("guest-");
        GuestId.AnonymousGuardrail.Should().Contain("Refuse");
        GuestId.AnonymousGuardrail.Should().Contain("sign in");
        GuestId.AnonymousGuardrail.Should().Contain("Never invent");
    }
}
