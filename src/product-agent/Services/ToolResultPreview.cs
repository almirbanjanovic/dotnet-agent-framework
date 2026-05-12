using System.Text.Json;

// Renders a Microsoft.Extensions.AI FunctionResultContent.Result into a
// short UTF-8 preview suitable for streaming over SSE to the browser.
//
// Hard caps:
//   * MaxPreviewChars  — bounds the per-event payload so a tool that
//     returns megabytes of JSON cannot fill the SSE pipe / browser DOM.
//   * Render never throws — serialization failures degrade to the
//     ToString() form so the timeline always shows SOMETHING.
//
// Inlined into each agent project (intentional duplicate of the file in
// crm-agent/Services) per the repo HARD RULE: zero ProjectReference
// across src/ projects, no shared "Common" library.
internal static class ToolResultPreview
{
    private const int MaxPreviewChars = 512;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    public static (string Preview, bool Truncated) Render(object? result)
    {
        if (result is null)
        {
            return ("(no result)", false);
        }

        string text;
        try
        {
            text = result is string s
                ? s
                : JsonSerializer.Serialize(result, JsonOptions);
        }
        catch (Exception ex)
        {
            text = $"(serialization failed: {ex.GetType().Name})";
        }

        if (text.Length <= MaxPreviewChars)
        {
            return (text, false);
        }

        // Avoid slicing in the middle of a UTF-16 surrogate pair, which
        // would yield a lone high surrogate at the boundary. JSON
        // serialization would then encode it as \uFFFD, but it's cleaner
        // to back off one char and emit a clean break.
        var cut = MaxPreviewChars;
        if (char.IsHighSurrogate(text[cut - 1]))
        {
            cut -= 1;
        }
        return (text.Substring(0, cut), true);
    }
}
