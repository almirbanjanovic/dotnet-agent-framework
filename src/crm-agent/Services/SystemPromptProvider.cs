using Microsoft.Extensions.Hosting;

// Loads the CRM Agent's system prompt from `Prompts/system-prompt.md`
// (copied to the build output by the .csproj). One file, one source of
// truth — change the prompt without touching code.

internal sealed class SystemPromptProvider
{
    public SystemPromptProvider(IHostEnvironment environment)
    {
        var promptPath = Path.Combine(environment.ContentRootPath, "Prompts", "system-prompt.md");
        Prompt = File.ReadAllText(promptPath);
    }

    public string Prompt { get; }
}
