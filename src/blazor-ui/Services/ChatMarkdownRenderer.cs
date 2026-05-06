using System.Text.RegularExpressions;
using Ganss.Xss;
using Markdig;
using Microsoft.AspNetCore.Components;

namespace Contoso.BlazorUi.Services;

internal static class ChatMarkdownRenderer
{
    private static readonly MarkdownPipeline MarkdownPipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    private static readonly HtmlSanitizer Sanitizer = new();

    private static readonly Regex ImageRegex =
        new("!\\[(?<alt>[^\\]]*)\\]\\((?<url>[^)]+)\\)", RegexOptions.Compiled);

    // Path prefix the BFF uses to serve product images. Any markdown image
    // URL that lands on this prefix — whether emitted by the agent as a
    // bare filename, a relative path, or an absolute path — must be
    // rewritten to point at the BFF host so it works cross-origin.
    private const string BffImagePathPrefix = "/api/v1/images/";

    internal static MarkupString RenderMarkdown(string content, string? bffBaseUrl = null)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new MarkupString(string.Empty);
        }

        var rewritten = RewriteImageUrls(content, bffBaseUrl);
        var html = Markdig.Markdown.ToHtml(rewritten, MarkdownPipeline);
        var sanitized = Sanitizer.Sanitize(html);
        return new MarkupString(sanitized);
    }

    internal static string RewriteImageUrls(string markdown, string? bffBaseUrl = null)
    {
        // Trim the trailing slash so we always concatenate base + path with
        // exactly one '/' between them.
        var prefix = string.IsNullOrWhiteSpace(bffBaseUrl)
            ? string.Empty
            : bffBaseUrl.TrimEnd('/');

        return ImageRegex.Replace(markdown, match =>
        {
            var alt = match.Groups["alt"].Value;
            var url = match.Groups["url"].Value.Trim();

            // Already a fully-qualified URL (https://cdn.example.com/...)
            // — leave it alone. The agent or knowledge source intentionally
            // pointed at an external image.
            if (Uri.TryCreate(url, UriKind.Absolute, out _))
            {
                return match.Value;
            }

            // Absolute path. If it looks like a BFF image route, host-shift
            // it onto the BFF; otherwise leave it (some other in-app path).
            // Case-insensitive comparison — ASP.NET Core routing matches
            // /API/V1/IMAGES/foo.png the same as /api/v1/images/foo.png.
            if (url.StartsWith("/"))
            {
                if (prefix.Length > 0 && url.StartsWith(BffImagePathPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return $"![{alt}]({prefix}{url})";
                }
                return match.Value;
            }

            // Bare filename or relative path — the agent prompt instructs
            // "Reference product images as ![ProductName](imageFilename.png)".
            // Strip any querystring / fragment, normalise separators, take
            // the last path segment, and URL-escape so spaces or other
            // reserved characters in a filename don't produce a malformed
            // URL on the BFF.
            var fileName = url.Split('?', '#')[0];
            fileName = fileName.Replace("\\", "/");
            var lastSegment = fileName.Split('/').LastOrDefault() ?? fileName;
            var escaped = Uri.EscapeDataString(lastSegment);

            return $"![{alt}]({prefix}{BffImagePathPrefix}{escaped})";
        });
    }
}
