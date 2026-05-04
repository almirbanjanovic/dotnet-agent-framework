using System.Text.RegularExpressions;
using FluentAssertions;

namespace Contoso.AppHost.Tests;

/// <summary>
/// Architectural fitness functions that mechanically catch the four bug
/// classes that account for ~90% of the issues found in the eight
/// production-readiness audit passes:
///
///   1. Chart hardcoded values drifting from the Terraform truth source
///      (e.g. <c>Search__IndexName: "products"</c> when Terraform creates
///      <c>knowledge-documents-index</c>).
///   2. Chart <c>secretRefs.keys[].key</c> referencing a Key Vault secret
///      that is not bootstrapped into the in-cluster <c>keyvault-secrets</c>
///      Kubernetes Secret (silent <c>InvalidOperationException</c> on pod
///      start).
///   3. Code reading <c>configuration["X:Y"] ?? throw</c> for a config key
///      that no chart in the repository provides — meaning the pod will
///      crash on first request the moment that code path is exercised.
///   4. Chart values intentionally set to a sentinel placeholder
///      (<c>https://override-via-helm-set</c>) without a matching
///      <c>--set config.X=...</c> step in the corresponding GitHub Actions
///      deploy workflow.
///
/// These tests are pure repo-text scans — no Azure auth, no live cluster
/// access, no YAML library. They intentionally use simple regex parsers
/// because the chart / tfvars / workflow files are highly regular and the
/// alternative (a full YAML/HCL pipeline) would be over-engineered for a
/// fitness check.
///
/// If any of these tests fails, **fix the underlying drift**. Do not adjust
/// the assertion table to silence the failure unless the truth source
/// itself has legitimately moved.
/// </summary>
public class FitnessTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    /// <summary>
    /// Services that ship a Helm chart and therefore must be scanned. The
    /// other top-level src/ projects (AppHost, ServiceDefaults, seed-data,
    /// config-sync, simple-agent) are local-dev / one-shot tools and are
    /// out of scope for these production-deploy invariants.
    /// </summary>
    private static readonly string[] ChartedServices =
    {
        "bff-api",
        "blazor-ui",
        "crm-agent",
        "crm-api",
        "crm-mcp",
        "knowledge-mcp",
        "orchestrator-agent",
        "product-agent",
    };

    /// <summary>
    /// Services whose required config comes from
    /// <c>wwwroot/appsettings.json</c> (baked into the static SPA at
    /// docker-build time via envsubst), NOT from Kubernetes pod env vars.
    /// For these services the Helm chart correctly provides nothing
    /// because the browser, not the pod, reads the config — so they are
    /// excluded from <see cref="ChartProvides_EveryRequiredConfigKey_ReadByService"/>
    /// and validated separately by
    /// <see cref="BlazorWasmTemplate_ProvidesEveryRequiredBrowserConfigKey"/>.
    /// </summary>
    private static readonly HashSet<string> BrowserConfigServices = new(StringComparer.Ordinal)
    {
        "blazor-ui",
    };

    // ---------------------------------------------------------------- 1 ---

    /// <summary>
    /// Truth table for chart values that name an Azure resource. Each entry
    /// pins one chart key to the literal value that Terraform actually
    /// creates. The <c>Source</c> field documents WHERE the expected value
    /// is canonically defined so a future maintainer can update both sides
    /// in lockstep.
    ///
    /// Add a new row here every time a chart hardcodes a name that comes
    /// from infra. Drift will fail the test.
    /// </summary>
    private static readonly (string ChartRelPath, string Key, string ExpectedValue, string Source)[] HardcodedAssertions =
    {
        ("src/knowledge-mcp/chart/values.yaml", "Search__IndexName",
            "knowledge-documents-index",
            "var.search_index_name + \"-index\" (infra/terraform/dev.tfvars + modules/knowledge-source/v1/outputs.tf)"),

        ("src/bff-api/chart/values.yaml", "CosmosDb__AgentsDatabase",
            "agents",
            "var.cosmos_agents_database_name (infra/terraform/dev.tfvars)"),

        ("src/bff-api/chart/values.yaml", "CosmosDb__AgentsContainer",
            "conversations",
            "module.cosmosdb_agents.containers.conversations.name (infra/terraform/main.tf)"),

        ("src/crm-api/chart/values.yaml", "CosmosDb__DatabaseName",
            "contoso-crm",
            "var.cosmos_crm_database_name (infra/terraform/dev.tfvars)"),
    };

    [Fact]
    public void ChartHardcodedConfig_MatchesTerraformTruth()
    {
        var failures = new List<string>();

        foreach (var (chartRel, key, expected, source) in HardcodedAssertions)
        {
            var chartPath = Path.Combine(RepoRoot, chartRel.Replace('/', Path.DirectorySeparatorChar));
            File.Exists(chartPath).Should().BeTrue($"chart values file {chartRel} should exist");

            var actual = ReadChartConfigValue(chartPath, key);
            if (actual is null)
            {
                failures.Add($"{chartRel}: key '{key}' is missing from the `config:` block (expected '{expected}', source: {source})");
                continue;
            }

            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                failures.Add($"{chartRel}: '{key}' = '{actual}' but truth source ({source}) says '{expected}'");
            }
        }

        failures.Should().BeEmpty(
            "chart hardcoded values must match the Terraform truth source. " +
            "Either fix the chart, or — if Terraform has legitimately changed — update the assertion table in FitnessTests." +
            Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    // ---------------------------------------------------------------- 2 ---

    [Fact]
    public void ChartSecretRefs_AreAllBootstrappedIntoK8sSecret()
    {
        var k8sSecretsPath = Path.Combine(RepoRoot, "infra", "terraform", "k8s-secrets.tf");
        File.Exists(k8sSecretsPath).Should().BeTrue("infra/terraform/k8s-secrets.tf should exist");

        var bootstrappedKeys = ReadK8sSecretKeys(k8sSecretsPath);
        bootstrappedKeys.Should().NotBeEmpty("the keyvault_secret_keys list should contain at least one entry");

        var failures = new List<string>();

        foreach (var service in ChartedServices)
        {
            var chartPath = Path.Combine(RepoRoot, "src", service, "chart", "values.yaml");
            if (!File.Exists(chartPath))
            {
                continue;
            }

            foreach (var (key, lineNumber) in ReadChartSecretRefKeys(chartPath))
            {
                if (!bootstrappedKeys.Contains(key))
                {
                    failures.Add($"src/{service}/chart/values.yaml:{lineNumber} references secret key '{key}' which is NOT in keyvault_secret_keys (infra/terraform/k8s-secrets.tf). The pod will start with an empty env var.");
                }
            }
        }

        failures.Should().BeEmpty(
            "every chart secretRef key must appear in infra/terraform/k8s-secrets.tf's keyvault_secret_keys list. " +
            "Add the missing key(s) there (and ensure module.keyvault_secrets writes them in main.tf)." +
            Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    // ---------------------------------------------------------------- 3 ---

    /// <summary>
    /// Code reads of the form <c>configuration["X:Y"] ?? throw ...</c> are
    /// hard requirements: the pod will crash on the first request that
    /// reaches that line if the env var <c>X__Y</c> is not set. This test
    /// asserts that every such hard requirement is satisfied by the
    /// service's chart, either via the static <c>config:</c> map or via a
    /// <c>secretRefs.keys[].envVar</c> mapping.
    /// </summary>
    [Fact]
    public void ChartProvides_EveryRequiredConfigKey_ReadByService()
    {
        var failures = new List<string>();

        foreach (var service in ChartedServices)
        {
            // Browser-served SPAs read config from wwwroot/appsettings.json
            // (rendered at docker-build time), not from pod env vars. They
            // are validated separately by
            // BlazorWasmTemplate_ProvidesEveryRequiredBrowserConfigKey.
            if (BrowserConfigServices.Contains(service))
            {
                continue;
            }

            var serviceDir = Path.Combine(RepoRoot, "src", service);
            var chartPath = Path.Combine(serviceDir, "chart", "values.yaml");
            if (!File.Exists(chartPath))
            {
                continue;
            }

            var requiredEnvVars = CollectRequiredConfigKeys(serviceDir)
                .Select(ToEnvVarName)
                .ToHashSet(StringComparer.Ordinal);

            var providedEnvVars = ReadChartProvidedEnvVars(chartPath);

            foreach (var required in requiredEnvVars.OrderBy(x => x, StringComparer.Ordinal))
            {
                if (!providedEnvVars.Contains(required))
                {
                    failures.Add($"src/{service}: code reads required config '{ToConfigKey(required)}' (env '{required}') but src/{service}/chart/values.yaml does not provide it via `config:` or `secretRefs:`. Pod will throw InvalidOperationException at startup or first call.");
                }
            }
        }

        failures.Should().BeEmpty(
            "every `configuration[\"X:Y\"] ?? throw` in a charted service's source code must be satisfied by that service's chart. " +
            "Add the missing key(s) to chart/values.yaml under `config:` (for static values) or `secretRefs:` (for KV-backed values)." +
            Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    // ---------------------------------------------------------------- 4 ---

    /// <summary>
    /// Sentinel chart values are intentionally invalid placeholders that
    /// MUST be overridden at deploy time (e.g.
    /// <c>BlazorUi__Origin: "https://override-via-helm-set"</c>). For each
    /// sentinel, both the per-service deploy workflow AND the unified
    /// "deploy-all-services" workflow must inject the real value via
    /// <c>--set config.&lt;Key&gt;=...</c>.
    /// </summary>
    [Fact]
    public void SentinelChartValues_AreOverriddenInDeployWorkflows()
    {
        const string Sentinel = "override-via-helm-set";

        var failures = new List<string>();
        var allServicesPath = Path.Combine(RepoRoot, ".github", "workflows", "deploy-all-services.yml");
        File.Exists(allServicesPath).Should().BeTrue("deploy-all-services.yml should exist");
        var allServicesText = File.ReadAllText(allServicesPath);

        foreach (var service in ChartedServices)
        {
            var chartPath = Path.Combine(RepoRoot, "src", service, "chart", "values.yaml");
            if (!File.Exists(chartPath))
            {
                continue;
            }

            foreach (var (configKey, lineNumber) in FindSentinelKeys(chartPath, Sentinel))
            {
                // The per-service workflow MUST contain a `--set ...config.<key>=` line.
                var perServiceWorkflow = Path.Combine(RepoRoot, ".github", "workflows", $"deploy-{service}.yml");
                if (!File.Exists(perServiceWorkflow))
                {
                    failures.Add($"src/{service}/chart/values.yaml:{lineNumber} has sentinel '{configKey}' but per-service workflow .github/workflows/deploy-{service}.yml does not exist");
                    continue;
                }

                var perServiceText = File.ReadAllText(perServiceWorkflow);
                var setPattern = new Regex($@"--set\s+[""']?config\.{Regex.Escape(configKey)}\s*=", RegexOptions.IgnoreCase);

                if (!setPattern.IsMatch(perServiceText))
                {
                    failures.Add($"src/{service}/chart/values.yaml:{lineNumber} has sentinel '{configKey}' but .github/workflows/deploy-{service}.yml has no `--set config.{configKey}=...` step");
                }

                if (!setPattern.IsMatch(allServicesText))
                {
                    failures.Add($"src/{service}/chart/values.yaml:{lineNumber} has sentinel '{configKey}' but .github/workflows/deploy-all-services.yml has no `--set config.{configKey}=...` step");
                }
            }
        }

        failures.Should().BeEmpty(
            "every chart value set to the 'override-via-helm-set' sentinel must be overridden by both the per-service AND the deploy-all-services workflow. " +
            "Either add the `--set config.<Key>=...` step, or remove the sentinel and put the real value in chart/values.yaml." +
            Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    // ---------------------------------------------------------------- 5 ---

    /// <summary>
    /// Companion to test #3 for browser-served WebAssembly SPAs. Every
    /// <c>configuration["X:Y"] ?? throw</c> read in such a service must
    /// have a corresponding leaf key in <c>wwwroot/appsettings.json.template</c>
    /// so the deploy workflow's envsubst step can write the runtime value
    /// into the static asset before docker build.
    /// </summary>
    [Fact]
    public void BlazorWasmTemplate_ProvidesEveryRequiredBrowserConfigKey()
    {
        var failures = new List<string>();

        foreach (var service in BrowserConfigServices)
        {
            var serviceDir = Path.Combine(RepoRoot, "src", service);
            var templatePath = Path.Combine(serviceDir, "wwwroot", "appsettings.json.template");

            if (!File.Exists(templatePath))
            {
                failures.Add($"src/{service}: declared as a browser-config service but wwwroot/appsettings.json.template is missing");
                continue;
            }

            var templateText = File.ReadAllText(templatePath);

            foreach (var configKey in CollectRequiredConfigKeys(serviceDir).Distinct(StringComparer.Ordinal))
            {
                // The template is JSON with nested objects; checking for
                // the quoted leaf key name is loose but catches the common
                // "forgot to add the field to the template" mistake.
                var leaf = configKey.Split(':').Last();
                if (!templateText.Contains($"\"{leaf}\"", StringComparison.Ordinal))
                {
                    failures.Add($"src/{service}: code requires config '{configKey}' (?? throw) but wwwroot/appsettings.json.template has no \"{leaf}\" entry. Browser will receive an empty value and the SPA will throw at startup.");
                }
            }
        }

        failures.Should().BeEmpty(
            "every required config read in a browser-config service must have a matching leaf key in wwwroot/appsettings.json.template. " +
            "Add the missing entry to the template (and ensure the deploy workflow exports the corresponding env var before envsubst)." +
            Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    // -------------------------------------------------------- helpers ----

    /// <summary>
    /// Reads the literal value of one key under the top-level
    /// <c>config:</c> block. Returns null if the key is not present.
    /// Lines inside a multi-line block (commented lines, secretRefs, etc.)
    /// are skipped because the regex anchors on a 2-space indent.
    /// </summary>
    private static string? ReadChartConfigValue(string chartPath, string key)
    {
        var lines = File.ReadAllLines(chartPath);
        var inConfig = false;
        var pattern = new Regex($@"^\s{{2}}{Regex.Escape(key)}\s*:\s*""?([^""#]*?)""?\s*(#.*)?$");

        foreach (var line in lines)
        {
            if (line.StartsWith("config:", StringComparison.Ordinal))
            {
                inConfig = true;
                continue;
            }

            // Any new top-level block ends the config: section.
            if (inConfig && line.Length > 0 && !char.IsWhiteSpace(line[0]) && !line.StartsWith("#", StringComparison.Ordinal))
            {
                inConfig = false;
            }

            if (!inConfig) continue;

            var match = pattern.Match(line);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the set of Key Vault secret names declared in
    /// <c>infra/terraform/k8s-secrets.tf</c> inside the
    /// <c>keyvault_secret_keys = [...]</c> local.
    /// </summary>
    private static HashSet<string> ReadK8sSecretKeys(string k8sSecretsPath)
    {
        var text = File.ReadAllText(k8sSecretsPath);
        var listMatch = Regex.Match(text,
            @"keyvault_secret_keys\s*=\s*\[(?<body>[^\]]*)\]",
            RegexOptions.Singleline);

        if (!listMatch.Success)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var body = listMatch.Groups["body"].Value;
        return new HashSet<string>(
            Regex.Matches(body, @"""([^""]+)""")
                .Select(m => m.Groups[1].Value),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Yields every secret key declared under any <c>secretRefs:</c> block
    /// in a chart values file, with its 1-based line number for error
    /// reporting.
    /// </summary>
    private static IEnumerable<(string Key, int LineNumber)> ReadChartSecretRefKeys(string chartPath)
    {
        var lines = File.ReadAllLines(chartPath);
        var inSecretRefs = false;
        var keyPattern = new Regex(@"^\s+-\s+key:\s*(\S+)\s*$");

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (line.StartsWith("secretRefs:", StringComparison.Ordinal))
            {
                inSecretRefs = true;
                continue;
            }

            // A new top-level block ends secretRefs.
            if (inSecretRefs && line.Length > 0 && !char.IsWhiteSpace(line[0]) && !line.StartsWith("#", StringComparison.Ordinal))
            {
                inSecretRefs = false;
            }

            if (!inSecretRefs) continue;

            var match = keyPattern.Match(line);
            if (match.Success)
            {
                yield return (match.Groups[1].Value, i + 1);
            }
        }
    }

    /// <summary>
    /// Returns the set of env var names a chart provides to its pods,
    /// gathered from both the <c>config:</c> map (top-level keys) and the
    /// <c>secretRefs.keys[].envVar</c> values.
    /// </summary>
    private static HashSet<string> ReadChartProvidedEnvVars(string chartPath)
    {
        var provided = new HashSet<string>(StringComparer.Ordinal);
        var lines = File.ReadAllLines(chartPath);

        var inConfig = false;
        var inSecretRefs = false;

        var configKeyPattern = new Regex(@"^\s{2}([A-Za-z][A-Za-z0-9._]*)\s*:");
        var envVarPattern = new Regex(@"^\s+envVar:\s*(\S+)\s*$");

        foreach (var line in lines)
        {
            if (line.StartsWith("config:", StringComparison.Ordinal))
            {
                inConfig = true;
                inSecretRefs = false;
                continue;
            }
            if (line.StartsWith("secretRefs:", StringComparison.Ordinal))
            {
                inSecretRefs = true;
                inConfig = false;
                continue;
            }
            if (line.Length > 0 && !char.IsWhiteSpace(line[0]) && !line.StartsWith("#", StringComparison.Ordinal))
            {
                inConfig = false;
                inSecretRefs = false;
            }

            if (inConfig)
            {
                var m = configKeyPattern.Match(line);
                if (m.Success)
                {
                    provided.Add(m.Groups[1].Value);
                }
            }
            else if (inSecretRefs)
            {
                var m = envVarPattern.Match(line);
                if (m.Success)
                {
                    provided.Add(m.Groups[1].Value);
                }
            }
        }

        return provided;
    }

    /// <summary>
    /// Scans every C# file under a service directory (excluding bin/obj)
    /// for required config reads of the form
    /// <c>configuration["X:Y"] ?? throw ...</c> and yields the captured
    /// <c>X:Y</c> keys.
    /// </summary>
    private static IEnumerable<string> CollectRequiredConfigKeys(string serviceDir)
    {
        // Match a configuration[ "X:Y" ] indexer that is followed (within a
        // bounded window, before any semicolon) by `?? throw`. The bounded
        // [^;]{0,200} keeps us from accidentally pairing one config read
        // with an unrelated throw later in the file.
        var pattern = new Regex(
            @"[Cc]onfiguration\s*\[\s*""([^""]+)""\s*\][^;]{0,200}?\?\?\s*throw",
            RegexOptions.Singleline);

        foreach (var csFile in Directory.EnumerateFiles(serviceDir, "*.cs", SearchOption.AllDirectories))
        {
            if (csFile.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                csFile.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(csFile);
            foreach (Match m in pattern.Matches(text))
            {
                var key = m.Groups[1].Value;
                // Skip section-less keys (no colon → not a chart config).
                if (key.Contains(':', StringComparison.Ordinal))
                {
                    yield return key;
                }
            }
        }
    }

    /// <summary>
    /// Yields every key in a chart's <c>config:</c> block whose value
    /// contains the sentinel marker, with its 1-based line number.
    /// </summary>
    private static IEnumerable<(string Key, int LineNumber)> FindSentinelKeys(string chartPath, string sentinel)
    {
        var lines = File.ReadAllLines(chartPath);
        var inConfig = false;
        var pattern = new Regex(@"^\s{2}([A-Za-z][A-Za-z0-9._]*)\s*:\s*""?([^""#]*)""?");

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (line.StartsWith("config:", StringComparison.Ordinal))
            {
                inConfig = true;
                continue;
            }
            if (inConfig && line.Length > 0 && !char.IsWhiteSpace(line[0]) && !line.StartsWith("#", StringComparison.Ordinal))
            {
                inConfig = false;
            }

            if (!inConfig) continue;

            var m = pattern.Match(line);
            if (m.Success && m.Groups[2].Value.Contains(sentinel, StringComparison.Ordinal))
            {
                yield return (m.Groups[1].Value, i + 1);
            }
        }
    }

    /// <summary>
    /// .NET configuration convention: <c>Section:Key</c> in code becomes
    /// <c>Section__Key</c> as an environment variable.
    /// </summary>
    private static string ToEnvVarName(string configKey) => configKey.Replace(":", "__");

    private static string ToConfigKey(string envVar) => envVar.Replace("__", ":");
}
