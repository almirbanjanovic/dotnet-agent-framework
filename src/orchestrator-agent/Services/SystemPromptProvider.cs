namespace Contoso.OrchestratorAgent.Services;

internal sealed class SystemPromptProvider
{
    public SystemPromptProvider(IHostEnvironment environment)
    {
        var promptPath = Path.Combine(environment.ContentRootPath, "Prompts", "system-prompt.md");
        Prompt = File.ReadAllText(promptPath);
    }

    public string Prompt { get; }
}
