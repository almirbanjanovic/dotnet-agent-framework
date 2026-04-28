using Contoso.OrchestratorAgent.Services;
using FluentAssertions;
using NSubstitute;

namespace Contoso.OrchestratorAgent.Tests;

public sealed class IntentClassifierTests
{
    [Fact]
    public async Task ClassifyAsync_ReturnsCrm_WhenResponseIsCrm()
    {
        var classifier = CreateClassifier("CRM");

        var intent = await classifier.ClassifyAsync("order status", CancellationToken.None);

        intent.Should().Be("CRM");
    }

    [Fact]
    public async Task ClassifyAsync_ReturnsProduct_WhenResponseIsProduct()
    {
        var classifier = CreateClassifier("PRODUCT");

        var intent = await classifier.ClassifyAsync("need gear", CancellationToken.None);

        intent.Should().Be("PRODUCT");
    }

    [Fact]
    public async Task ClassifyAsync_DefaultsToCrm_WhenResponseIsGibberish()
    {
        var classifier = CreateClassifier("???");

        var intent = await classifier.ClassifyAsync("hello", CancellationToken.None);

        intent.Should().Be("CRM");
    }

    [Fact]
    public async Task ClassifyAsync_IsCaseInsensitive()
    {
        var classifier = CreateClassifier("product");

        var intent = await classifier.ClassifyAsync("looking for boots", CancellationToken.None);

        intent.Should().Be("PRODUCT");
    }

    [Fact]
    public async Task ClassifyAsync_MatchesSubstring()
    {
        var classifier = CreateClassifier("This is a PRODUCT request.");

        var intent = await classifier.ClassifyAsync("promo details", CancellationToken.None);

        intent.Should().Be("PRODUCT");
    }

    private static IntentClassifier CreateClassifier(string response)
    {
        var client = Substitute.For<IIntentClassifierClient>();
        client.RunAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));
        return new IntentClassifier(client);
    }
}
