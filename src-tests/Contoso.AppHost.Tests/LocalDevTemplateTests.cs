using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace Contoso.AppHost.Tests;

/// <summary>
/// Architectural fitness function for the local-dev experience.
///
/// Every <c>src/&lt;component&gt;/appsettings.Local.json.template</c> is
/// processed by <c>infra/setup-local.{ps1,sh}</c>, which substitutes
/// placeholders (<c>{{FOUNDRY_PROJECT_ENDPOINT}}</c>, <c>{{TENANT_ID}}</c>, etc.)
/// with values from the per-developer Foundry deployment.
///
/// If a template hardcodes a real Foundry endpoint or tenant ID, every
/// developer who runs <c>setup-local</c> ends up pointing at that real
/// account — auth fails, the local stack collapses, and (worse) sensitive
/// identifiers leak into source control. This test catches that drift
/// before it lands.
/// </summary>
public class LocalDevTemplateTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static readonly Regex GuidRegex = new(
        @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b",
        RegexOptions.Compiled);

    // The new Foundry experience exposes both the account host
    // (cognitiveservices.azure.com) and the project host
    // (services.ai.azure.com). Either one in a template is a leak.
    private static readonly Regex FoundryUrlRegex = new(
        @"https?://[^""]*\.(cognitiveservices\.azure\.com|services\.ai\.azure\.com)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [Fact]
    public void EveryComponent_HasLocalTemplate()
    {
        var srcDir = Path.Combine(RepoRoot, "src");
        var components = new[]
        {
            "crm-api", "crm-mcp", "knowledge-mcp",
            "crm-agent", "product-agent", "orchestrator-agent",
            "bff-api", "blazor-ui", "simple-agent"
        };

        var missing = components
            .Where(c => !File.Exists(Path.Combine(srcDir, c, "appsettings.Local.json.template")))
            .ToArray();

        missing.Should().BeEmpty(
            "every runnable component must ship an appsettings.Local.json.template " +
            "so infra/setup-local can generate its appsettings.Local.json. Missing: " +
            string.Join(", ", missing));
    }

    [Fact]
    public void Templates_DoNotHardcode_FoundryEndpoint()
    {
        var violations = EnumerateTemplates()
            .Select(path => new
            {
                Path = Path.GetRelativePath(RepoRoot, path),
                BadUrl = FoundryUrlRegex.Match(File.ReadAllText(path)).Value
            })
            .Where(x => !string.IsNullOrEmpty(x.BadUrl))
            .Select(x => $"{x.Path}: contains hardcoded Foundry URL '{x.BadUrl}' — use '{{{{FOUNDRY_PROJECT_ENDPOINT}}}}' instead.")
            .ToArray();

        violations.Should().BeEmpty(
            "Foundry endpoints in *.Local.json.template files must be the '{{FOUNDRY_PROJECT_ENDPOINT}}' " +
            "placeholder. setup-local substitutes it with each developer's own deployment. " +
            "Hardcoding a real endpoint forces every developer onto the same Foundry account. " +
            "Violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Templates_DoNotHardcode_TenantId()
    {
        var violations = EnumerateTemplates()
            .Select(path => new
            {
                Path = Path.GetRelativePath(RepoRoot, path),
                BadGuid = ExtractTenantGuid(File.ReadAllText(path))
            })
            .Where(x => x.BadGuid is not null)
            .Select(x => $"{x.Path}: contains hardcoded tenant GUID '{x.BadGuid}' — use '{{{{TENANT_ID}}}}' instead.")
            .ToArray();

        violations.Should().BeEmpty(
            "AzureAd:TenantId in *.Local.json.template files must be the '{{TENANT_ID}}' " +
            "placeholder, not a literal GUID. Violations:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Templates_AreValidJson()
    {
        var violations = new List<string>();

        foreach (var path in EnumerateTemplates())
        {
            var raw = File.ReadAllText(path);
            var stubbed = StubPlaceholders(raw);

            try
            {
                using var _ = JsonDocument.Parse(stubbed);
            }
            catch (JsonException ex)
            {
                violations.Add($"{Path.GetRelativePath(RepoRoot, path)}: {ex.Message}");
            }
        }

        violations.Should().BeEmpty(
            "every *.Local.json.template must parse as valid JSON after placeholder " +
            "substitution. Violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Templates_With_AzureAd_Block_Default_To_EntraEnabled()
    {
        // Both tracks (Local + Full Azure) sign in with real Microsoft Entra ID.
        // Templates with an AzureAd block must default Enabled to true so a
        // freshly-generated appsettings.Local.json exercises the production
        // sign-in flow out of the box. If a template ships Enabled = false,
        // a developer's first run silently falls back to header-based dev
        // auth — defeating the purpose of provisioning the Entra app reg.
        var violations = EnumerateTemplates()
            .Select(path => new
            {
                Path = Path.GetRelativePath(RepoRoot, path),
                Enabled = ExtractAzureAdEnabled(File.ReadAllText(path))
            })
            .Where(x => x.Enabled is false)
            .Select(x => $"{x.Path}: AzureAd:Enabled is false — set to true so MSAL sign-in is the default.")
            .ToArray();

        violations.Should().BeEmpty(
            "Templates that declare an AzureAd block must default to Enabled=true. Violations:"
            + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Templates_With_AzureAd_Block_Use_BffClientId_Placeholder()
    {
        // setup-local substitutes {{BFF_CLIENT_ID}} with the SPA app
        // registration's client ID from the entra Terraform module. A
        // hardcoded GUID forces every developer onto the same app
        // registration and leaks identifiers into source control.
        var violations = EnumerateTemplates()
            .Select(path => new
            {
                Path = Path.GetRelativePath(RepoRoot, path),
                Value = ExtractStringField(File.ReadAllText(path), "BffClientId")
            })
            .Where(x => x.Value is not null
                && !x.Value.Equals("{{BFF_CLIENT_ID}}", StringComparison.Ordinal))
            .Select(x => $"{x.Path}: BffClientId='{x.Value}' — use '{{{{BFF_CLIENT_ID}}}}' placeholder instead.")
            .ToArray();

        violations.Should().BeEmpty(
            "AzureAd:BffClientId in *.Local.json.template files must be the '{{BFF_CLIENT_ID}}' placeholder. Violations:"
            + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void BffApi_Template_Uses_CustomerMap_Json_Placeholder()
    {
        // The BFF maps real Entra UPNs/OIDs to seeded customer IDs
        // through AzureAd:CustomerMap. setup-local builds the map from
        // terraform's customer_map_json output and substitutes it
        // verbatim. A hardcoded {} would silently strand all sign-ins
        // on the OID fallback (no matching customer record).
        var bffTemplate = Path.Combine(RepoRoot, "src", "bff-api", "appsettings.Local.json.template");
        var content = File.ReadAllText(bffTemplate);
        content.Should().Contain("\"CustomerMap\": {{CUSTOMER_MAP_JSON}}",
            "src/bff-api/appsettings.Local.json.template must use the {{CUSTOMER_MAP_JSON}} placeholder.");
    }

    private static IEnumerable<string> EnumerateTemplates()
    {
        var srcDir = Path.Combine(RepoRoot, "src");
        return Directory.EnumerateFiles(srcDir, "appsettings.Local.json.template", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                        !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));
    }

    /// <summary>
    /// Returns the first GUID that appears in the template's <c>AzureAd:TenantId</c>
    /// field (case-insensitive), or <c>null</c> if the field is missing or already a placeholder.
    /// </summary>
    private static string? ExtractTenantGuid(string content)
    {
        // Match: "TenantId": "<value>"
        var match = Regex.Match(content, @"""TenantId""\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups[1].Value;
        var guid = GuidRegex.Match(value);
        return guid.Success ? guid.Value : null;
    }

    /// <summary>
    /// Stub placeholders into JSON-shaped values so the template can be parsed.
    /// Most placeholders sit inside string positions and become "STUB"; the
    /// <c>{{CUSTOMER_MAP_JSON}}</c> placeholder is a full JSON object position
    /// and becomes <c>{}</c>.
    /// </summary>
    private static string StubPlaceholders(string raw)
    {
        var stubbed = raw.Replace("{{CUSTOMER_MAP_JSON}}", "{}");
        return Regex.Replace(stubbed, @"\{\{[A-Z_][A-Z0-9_]*\}\}", "STUB");
    }

    /// <summary>
    /// Returns the boolean value of <c>AzureAd:Enabled</c>, or <c>null</c>
    /// if the template doesn't declare an AzureAd block.
    /// </summary>
    private static bool? ExtractAzureAdEnabled(string content)
    {
        // Only consider Enabled inside an AzureAd block — case-sensitive on the
        // section name to avoid false positives from unrelated sections.
        if (!content.Contains("\"AzureAd\""))
        {
            return null;
        }

        var match = Regex.Match(content, @"""Enabled""\s*:\s*(true|false)", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        return string.Equals(match.Groups[1].Value, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the string value of <paramref name="fieldName"/>, or <c>null</c>
    /// if the field isn't present.
    /// </summary>
    private static string? ExtractStringField(string content, string fieldName)
    {
        var match = Regex.Match(
            content,
            $@"""{Regex.Escape(fieldName)}""\s*:\s*""([^""]*)""");
        return match.Success ? match.Groups[1].Value : null;
    }
}
