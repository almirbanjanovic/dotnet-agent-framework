namespace Contoso.OrchestratorAgent.Services;

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

    // Stamps today's UTC date at the top of the prompt so the model doesn't
    // hallucinate from its training cutoff (e.g. answering "today is
    // April 6, 2026" when it's actually June). Computed per access so a fixed
    // TimeProvider in tests yields a deterministic prompt and so a long-
    // running process picks up date rollovers without a restart.
    public string Prompt =>
        $"Today's date is {_timeProvider.GetUtcNow():yyyy-MM-dd} (UTC).\n\n{_template}";
}
