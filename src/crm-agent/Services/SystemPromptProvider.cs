using Microsoft.Extensions.Hosting;

// Loads the CRM Agent's system prompt from `Prompts/system-prompt.md`
// (copied to the build output by the .csproj). One file, one source of
// truth — change the prompt without touching code. The current UTC date
// is stamped at the top on every access so the model doesn't hallucinate
// from its training cutoff when reasoning about return windows etc.

internal sealed class SystemPromptProvider
{
    private readonly string _template;
    private readonly TimeProvider _timeProvider;

    public SystemPromptProvider(IHostEnvironment environment, TimeProvider timeProvider)
    {
        var promptPath = Path.Combine(environment.ContentRootPath, "Prompts", "system-prompt.md");
        _template = File.ReadAllText(promptPath);
        _timeProvider = timeProvider;
    }

    // Computed per access: a fixed TimeProvider in tests yields a
    // deterministic prompt, and a long-running process picks up date
    // rollovers without a restart.
    public string Prompt =>
        $"Today's date is {_timeProvider.GetUtcNow():yyyy-MM-dd} (UTC).\n\n{_template}";
}
