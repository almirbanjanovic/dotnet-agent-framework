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

    internal static MarkupString RenderMarkdown(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new MarkupString(string.Empty);
        }

        var rewritten = RewriteImageUrls(content);
        var html = Markdig.Markdown.ToHtml(rewritten, MarkdownPipeline);
        var sanitized = Sanitizer.Sanitize(html);
        return new MarkupString(sanitized);
    }

    internal static string RewriteImageUrls(string markdown)
    {
        return ImageRegex.Replace(markdown, match =>
        {
            var alt = match.Groups["alt"].Value;
            var url = match.Groups["url"].Value.Trim();

            if (Uri.TryCreate(url, UriKind.Absolute, out _) || url.StartsWith("/"))
            {
                return match.Value;
            }

            var fileName = url.Split('?')[0];
            fileName = fileName.Replace("\\", "/");
            var lastSegment = fileName.Split('/').LastOrDefault() ?? fileName;

            return $"![{alt}](/api/v1/images/{lastSegment})";
        });
    }
}
