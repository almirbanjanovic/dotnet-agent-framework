using FluentAssertions;

namespace Contoso.AppHost.Tests;

public class ProjectRegistrationTests
{
    [Fact]
    public void Program_RegistersAllProjects()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..\\..\\..\\..\\.."));
        var programPath = Path.Combine(repoRoot, "src", "AppHost", "Program.cs");
        var contents = File.ReadAllText(programPath);

        var projectNames = new[]
        {
            "crm-api",
            "crm-mcp",
            "knowledge-mcp",
            "crm-agent",
            "product-agent",
            "orchestrator-agent",
            "bff-api",
            "blazor-ui"
        };

        foreach (var name in projectNames)
        {
            contents.Should().Contain($"\"{name}\"");
        }
    }
}
