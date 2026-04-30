using System.Xml.Linq;
using FluentAssertions;

namespace Contoso.AppHost.Tests;

/// <summary>
/// Architectural fitness function — enforces the project edict:
///
///   "All components in src/ must be completely self-contained.
///    Zero project-to-project references are permitted, except
///    AppHost (Aspire orchestrator) which references each service
///    so they can all be launched with a single 'dotnet run'."
///
/// If this test fails, do not work around it by modifying the rule.
/// Inline the shared code into each consumer instead.
/// </summary>
public class ComponentIndependenceTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    /// <summary>
    /// AppHost is the only allowed exception — it must reference each runnable
    /// project so Aspire's source generator can produce typed `Projects.X`
    /// handles.
    /// </summary>
    private const string AppHostProjectName = "Contoso.AppHost.csproj";

    [Fact]
    public void NoComponentInSrc_HasProjectReferences_ExceptAppHost()
    {
        var srcDir = Path.Combine(RepoRoot, "src");
        Directory.Exists(srcDir).Should().BeTrue($"expected src/ at {srcDir}");

        var violations = new List<string>();

        foreach (var csproj in Directory.EnumerateFiles(srcDir, "*.csproj", SearchOption.AllDirectories))
        {
            // Skip build artifacts.
            if (csproj.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                csproj.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            // AppHost is the explicitly allowed exception.
            if (Path.GetFileName(csproj).Equals(AppHostProjectName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var doc = XDocument.Load(csproj);
            var refs = doc.Descendants("ProjectReference")
                .Select(r => r.Attribute("Include")?.Value ?? "(unknown)")
                .ToArray();

            if (refs.Length > 0)
            {
                var relative = Path.GetRelativePath(RepoRoot, csproj);
                violations.Add($"{relative} -> {string.Join(", ", refs)}");
            }
        }

        violations.Should().BeEmpty(
            "every project under src/ must be self-contained (zero ProjectReferences). " +
            "AppHost is the only allowed exception. " +
            "If you need shared code, inline it into each consumer instead. " +
            "Violations: " + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }
}
