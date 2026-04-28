using Contoso.BlazorUi.Services;
using FluentAssertions;

namespace Contoso.BlazorUi.Tests;

public class MarkdownRenderingTests
{
    [Fact]
    public void RewriteImageUrls_FileName_RewritesToApiImageUrl()
    {
        var markdown = "![alt](images/photo.png)";

        var rewritten = ChatMarkdownRenderer.RewriteImageUrls(markdown);

        rewritten.Should().Be("![alt](/api/v1/images/photo.png)");
    }

    [Fact]
    public void RewriteImageUrls_AbsoluteUrl_Unchanged()
    {
        var markdown = "![alt](https://example.com/photo.png)";

        var rewritten = ChatMarkdownRenderer.RewriteImageUrls(markdown);

        rewritten.Should().Be(markdown);
    }

    [Fact]
    public void RenderMarkdown_StripsScriptTags()
    {
        var result = ChatMarkdownRenderer.RenderMarkdown("<script>alert('x')</script>");

        result.Value.Should().NotContainEquivalentOf("<script");
    }

    [Fact]
    public void RenderMarkdown_StripsEventHandlers()
    {
        var result = ChatMarkdownRenderer.RenderMarkdown("<img src=\"x\" onerror=\"alert('x')\" />");

        result.Value.Should().NotContainEquivalentOf("onerror");
    }

    [Fact]
    public void RenderMarkdown_NormalMarkdown_RendersHtml()
    {
        var result = ChatMarkdownRenderer.RenderMarkdown("**Hello**");

        result.Value.Should().Contain("<strong>").And.Contain("Hello");
    }

    [Fact]
    public void RenderMarkdown_EmptyInput_ReturnsEmpty()
    {
        var result = ChatMarkdownRenderer.RenderMarkdown(" ");

        result.Value.Should().BeEmpty();
    }
}
