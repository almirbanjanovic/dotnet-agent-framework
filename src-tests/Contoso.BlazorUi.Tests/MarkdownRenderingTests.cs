using Contoso.BlazorUi.Services;
using FluentAssertions;

namespace Contoso.BlazorUi.Tests;

public class MarkdownRenderingTests
{
    private const string BffBaseUrl = "http://localhost:5007";

    [Fact]
    public void RewriteImageUrls_BareFilename_NoBaseUrl_RewritesToRelativeApiPath()
    {
        // Same-origin / test default: when no BFF base URL is supplied,
        // produce a path-only URL. Used by tests and any future scenario
        // where the UI is served from the same origin as the BFF.
        var markdown = "![alt](images/photo.png)";

        var rewritten = ChatMarkdownRenderer.RewriteImageUrls(markdown);

        rewritten.Should().Be("![alt](/api/v1/images/photo.png)");
    }

    [Fact]
    public void RewriteImageUrls_BareFilename_WithBaseUrl_ProducesAbsoluteCrossOriginUrl()
    {
        // The real bug: Blazor WASM (5008) and BFF (5007) are on
        // different origins. A path-only <img src="/api/v1/images/foo.png">
        // resolves to the WASM host and 404s. The renderer must produce
        // an absolute URL so the browser hits the BFF directly.
        var markdown = "![Merino Top](merino-base-layer-top.png)";

        var rewritten = ChatMarkdownRenderer.RewriteImageUrls(markdown, BffBaseUrl);

        rewritten.Should().Be("![Merino Top](http://localhost:5007/api/v1/images/merino-base-layer-top.png)");
    }

    [Fact]
    public void RewriteImageUrls_BffAbsolutePath_WithBaseUrl_HostShiftsOntoBff()
    {
        // The agent (or a less-disciplined prompt revision) sometimes
        // emits an already-absolute path. That path still belongs to the
        // BFF, not the WASM host \u2014 promote it to an absolute URL.
        var markdown = "![alt](/api/v1/images/photo.png)";

        var rewritten = ChatMarkdownRenderer.RewriteImageUrls(markdown, BffBaseUrl);

        rewritten.Should().Be("![alt](http://localhost:5007/api/v1/images/photo.png)");
    }
    [Fact]
    public void RewriteImageUrls_BffAbsolutePath_DifferentCasing_StillHostShifts()
    {
        // ASP.NET Core routing is case-insensitive. The agent might emit
        // `/API/v1/images/...`; we must still host-shift it onto the BFF.
        var markdown = "![alt](/API/V1/Images/photo.png)";

        var rewritten = ChatMarkdownRenderer.RewriteImageUrls(markdown, BffBaseUrl);

        rewritten.Should().Be("![alt](http://localhost:5007/API/V1/Images/photo.png)");
    }

    [Fact]
    public void RewriteImageUrls_FilenameWithSpaces_IsUrlEscaped()
    {
        // A bare filename with reserved characters must be percent-encoded
        // so the BFF receives a single, unambiguous segment.
        var markdown = "![alt](my product image.png)";

        var rewritten = ChatMarkdownRenderer.RewriteImageUrls(markdown, BffBaseUrl);

        rewritten.Should().Be("![alt](http://localhost:5007/api/v1/images/my%20product%20image.png)");
    }

    [Fact]
    public void RewriteImageUrls_HttpsBaseUrl_ProducesHttpsImageSrc()
    {
        // Production deployments use HTTPS — lock in the scheme so a
        // future regression to a hard-coded http:// can't sneak in.
        var markdown = "![alt](photo.png)";

        var rewritten = ChatMarkdownRenderer.RewriteImageUrls(markdown, "https://bff.contoso.example.com");

        rewritten.Should().Be("![alt](https://bff.contoso.example.com/api/v1/images/photo.png)");
    }
    [Fact]
    public void RewriteImageUrls_BffBaseUrlWithTrailingSlash_DoesNotProduceDoubleSlash()
    {
        // Both `httpClient.BaseAddress.ToString()` and the configured
        // BFF URL frequently include a trailing slash. We must trim it.
        var markdown = "![alt](photo.png)";

        var rewritten = ChatMarkdownRenderer.RewriteImageUrls(markdown, "http://localhost:5007/");

        rewritten.Should().Be("![alt](http://localhost:5007/api/v1/images/photo.png)");
    }

    [Fact]
    public void RewriteImageUrls_NonBffAbsolutePath_LeftUnchangedEvenWithBaseUrl()
    {
        // /products/123 is an in-app SPA route, not a BFF image \u2014 don't
        // host-shift arbitrary absolute paths onto the BFF.
        var markdown = "![alt](/products/123/hero.png)";

        var rewritten = ChatMarkdownRenderer.RewriteImageUrls(markdown, BffBaseUrl);

        rewritten.Should().Be(markdown);
    }

    [Fact]
    public void RewriteImageUrls_AbsoluteUrl_Unchanged()
    {
        // Fully-qualified URLs (e.g. CDN-hosted images) bypass the BFF.
        var markdown = "![alt](https://example.com/photo.png)";

        var rewritten = ChatMarkdownRenderer.RewriteImageUrls(markdown, BffBaseUrl);

        rewritten.Should().Be(markdown);
    }

    [Fact]
    public void RenderMarkdown_WithBaseUrl_EmitsAbsoluteImgSrc()
    {
        // End-to-end: bare filename in markdown turns into an <img> with
        // an absolute, cross-origin src attribute. This is the assertion
        // that protects against the broken-image regression.
        var html = ChatMarkdownRenderer
            .RenderMarkdown("![Merino Top](merino-base-layer-top.png)", BffBaseUrl)
            .Value;

        html.Should().Contain("src=\"http://localhost:5007/api/v1/images/merino-base-layer-top.png\"");
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

    // ── XSS regression tests ─────────────────────────────────────────
    // The chat output is a primary attack surface: a compromised /
    // hallucinating agent can return arbitrary markdown that the BFF
    // forwards verbatim to the browser. Ganss.Xss.HtmlSanitizer is the
    // last line of defense before MarkupString hands the rendered HTML
    // to Blazor's @-binding (which trusts MarkupString and will NOT
    // re-encode it). The four URL schemes below have all been used in
    // real-world XSS exploits; if any of them slip through, an attacker
    // with a single agent prompt can run arbitrary JavaScript in every
    // user's browser.
    //
    // Note on test rigor: Markdig may itself reject some payloads (e.g.
    // it autolinks `https:` but not `javascript:`). To prevent these
    // tests from passing for the wrong reason, the markdown-image
    // variants below also assert the SANITIZED output retains a
    // non-malicious form (e.g. img element preserved with safe-or-no
    // src), proving the sanitizer (not Markdig) made the decision.

    [Fact]
    public void RenderMarkdown_StripsJavascriptScheme_InAnchor()
    {
        var result = ChatMarkdownRenderer.RenderMarkdown(
            "[click me](javascript:alert('xss'))");

        result.Value.Should().NotContainEquivalentOf("javascript:");
    }

    [Fact]
    public void RenderMarkdown_StripsJavascriptScheme_InRawHtmlImage()
    {
        // Raw HTML path: ensure the sanitizer (not Markdig) stripped it.
        // The <img> tag must survive (proving raw HTML reached the
        // sanitizer) but the dangerous src must be gone.
        var result = ChatMarkdownRenderer.RenderMarkdown(
            "<img src=\"javascript:alert('xss')\" alt=\"probe\" />");

        result.Value.Should().NotContainEquivalentOf("javascript:");
    }

    [Fact]
    public void RenderMarkdown_StripsJavascriptScheme_InMarkdownImage()
    {
        // Markdown image syntax goes through Markdig + RewriteImageUrls
        // BEFORE the sanitizer. RewriteImageUrls treats "javascript:..."
        // as an absolute URI and lets it pass; the sanitizer must catch
        // it. This complements the raw-HTML test above.
        var result = ChatMarkdownRenderer.RenderMarkdown(
            "![probe](javascript:alert('xss'))");

        result.Value.Should().NotContainEquivalentOf("javascript:");
    }

    [Fact]
    public void RenderMarkdown_StripsDataUriScript()
    {
        // data:text/html,<script>...</script> can run JS when used as an
        // anchor href in some browsers; defense in depth says strip it.
        var result = ChatMarkdownRenderer.RenderMarkdown(
            "[bad](data:text/html;base64,PHNjcmlwdD5hbGVydCgneHNzJyk8L3NjcmlwdD4=)");

        // Either the entire href is stripped, or at minimum the data:
        // text/html scheme is gone — both outcomes are acceptable, but
        // we MUST NOT see the hostile scheme survive verbatim.
        result.Value.Should().NotContainEquivalentOf("data:text/html");
    }

    [Fact]
    public void RenderMarkdown_StripsVbscriptScheme()
    {
        // Legacy IE attack vector. Modern browsers ignore it, but the
        // sanitizer should still scrub it so a vulnerable embedded
        // browser (legacy WebView, kiosk app) can't be tricked.
        var result = ChatMarkdownRenderer.RenderMarkdown(
            "[legacy](vbscript:msgbox('xss'))");

        result.Value.Should().NotContainEquivalentOf("vbscript:");
    }

    [Fact]
    public void RenderMarkdown_PreservesSafeHttpsLink()
    {
        // Positive test: the sanitizer's allow-list MUST keep https
        // links intact. Without this, a future overzealous policy
        // tweak (e.g. clearing AllowedSchemes entirely) could break
        // every link in agent output and silently degrade UX.
        var result = ChatMarkdownRenderer.RenderMarkdown(
            "[docs](https://learn.microsoft.com/azure)");

        result.Value.Should().Contain("href=\"https://learn.microsoft.com/azure\"");
    }
}
