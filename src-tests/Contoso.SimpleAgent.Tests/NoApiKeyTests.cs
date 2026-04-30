using FluentAssertions;

namespace Contoso.SimpleAgent.Tests;

/// <summary>
/// Architectural fitness function: enforce that no component under
/// <c>src/</c> ever falls back to API-key authentication. Every Azure
/// service call must go through <see cref="Azure.Identity.DefaultAzureCredential"/>
/// so that local devs use their <c>az login</c> token and AKS pods use
/// workload identity. API keys would skip the entire identity story
/// (tenant scoping, RBAC, audit) and shouldn't appear anywhere.
/// </summary>
public class NoApiKeyTests
{
    private static readonly string SrcRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src"));

    [Fact]
    public void NoComponent_References_AzureKeyCredential()
    {
        var violations = Directory
            .EnumerateFiles(SrcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(p => File.ReadAllText(p).Contains("AzureKeyCredential"))
            .Select(p => Path.GetRelativePath(SrcRoot, p))
            .ToArray();

        violations.Should().BeEmpty(
            "API keys are forbidden — every Azure call must use DefaultAzureCredential. "
            + "Files referencing AzureKeyCredential: " + string.Join(", ", violations));
    }

    [Fact]
    public void NoComponent_Reads_Foundry_ApiKey_Setting()
    {
        var violations = Directory
            .EnumerateFiles(SrcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(p =>
            {
                var content = File.ReadAllText(p);
                return content.Contains("Foundry:ApiKey") || content.Contains("\"Foundry__ApiKey");
            })
            .Select(p => Path.GetRelativePath(SrcRoot, p))
            .ToArray();

        violations.Should().BeEmpty(
            "No code may read a Foundry:ApiKey configuration value. "
            + "Files referencing it: " + string.Join(", ", violations));
    }
}
