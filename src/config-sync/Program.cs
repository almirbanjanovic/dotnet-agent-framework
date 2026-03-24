using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;

// ---------------------------------------------------------------------------
// Config Sync — pulls secrets from Azure Key Vault into per-component appsettings.Development.json
// ---------------------------------------------------------------------------
// Usage:
//   dotnet run -- <key-vault-uri>
//   dotnet run -- https://kv-agentic-ai-001.vault.azure.net/
//
// Authenticates via DefaultAzureCredential (az login locally, managed identity on AKS).
// Writes a SEPARATE appsettings.Development.json for each component under src/<component>/.
//
// Key Vault naming convention: PascalCase--Hierarchy (double-hyphen = .NET : separator)
//   e.g., CosmosDb--CrmEndpoint → { "CosmosDb": { "CrmEndpoint": "" } }
//
// Each component's manifest maps KV secrets to local config keys, allowing
// the same KV secret (e.g., CosmosDb--CrmEndpoint) to appear as a different
// local key per component (e.g., CosmosDb:Endpoint in crm-api).
// ---------------------------------------------------------------------------

if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
{
    Console.WriteLine("Usage: dotnet run -- <key-vault-uri>");
    Console.WriteLine("  Example: dotnet run -- https://kv-agentic-ai-001.vault.azure.net/");
    Console.WriteLine();
    Console.WriteLine("  You can find the Key Vault URI with: terraform output keyvault_uri");
    return;
}

var keyVaultUri = args[0].Trim();

Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine("  Config Sync — Key Vault → per-component appsettings.Development.json");
Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine();
Console.WriteLine($"  Key Vault: {keyVaultUri}");
Console.WriteLine($"  Auth:      DefaultAzureCredential (az login)");
Console.WriteLine();

// ── Component Manifest ────────────────────────────────────────────────────
// Maps each component to its required Key Vault secrets.
// Format: (kvSecret, configKey) — configKey uses : notation for nesting.
var componentManifest = new Dictionary<string, (string KvSecret, string ConfigKey)[]>
{
    ["crm-api"] =
    [
        ("CosmosDb--CrmEndpoint",       "CosmosDb:Endpoint"),
        ("CosmosDb--CrmDatabase",       "CosmosDb:DatabaseName"),
        ("AzureAd--TenantId",           "AzureAd:TenantId"),
    ],
    ["crm-mcp"] =
    [
        ("CrmApi--BaseUrl",             "CrmApi:BaseUrl"),
        ("AzureAd--TenantId",           "AzureAd:TenantId"),
    ],
    ["knowledge-mcp"] =
    [
        ("Search--Endpoint",            "Search:Endpoint"),
        ("Search--IndexName",           "Search:IndexName"),
        ("Storage--ImagesEndpoint",     "Storage:ImagesEndpoint"),
        ("Storage--ImagesAccountName",  "Storage:ImagesAccountName"),
        ("Storage--ImagesContainer",    "Storage:ImagesContainer"),
        ("AzureAd--TenantId",           "AzureAd:TenantId"),
    ],
    ["crm-agent"] =
    [
        ("AzureOpenAi--Endpoint",       "AzureOpenAi:Endpoint"),
        ("AzureOpenAi--DeploymentName", "AzureOpenAi:DeploymentName"),
        ("CrmMcp--BaseUrl",             "CrmMcp:BaseUrl"),
        ("AzureAd--TenantId",           "AzureAd:TenantId"),
    ],
    ["product-agent"] =
    [
        ("AzureOpenAi--Endpoint",       "AzureOpenAi:Endpoint"),
        ("AzureOpenAi--DeploymentName", "AzureOpenAi:DeploymentName"),
        ("KnowledgeMcp--BaseUrl",       "KnowledgeMcp:BaseUrl"),
        ("CrmMcp--BaseUrl",             "CrmMcp:BaseUrl"),
        ("AzureAd--TenantId",           "AzureAd:TenantId"),
    ],
    ["orchestrator-agent"] =
    [
        ("AzureOpenAi--Endpoint",       "AzureOpenAi:Endpoint"),
        ("AzureOpenAi--DeploymentName", "AzureOpenAi:DeploymentName"),
        ("CrmAgent--BaseUrl",           "CrmAgent:BaseUrl"),
        ("ProductAgent--BaseUrl",       "ProductAgent:BaseUrl"),
        ("AzureAd--TenantId",           "AzureAd:TenantId"),
    ],
    ["bff-api"] =
    [
        ("Orchestrator--BaseUrl",       "Orchestrator:BaseUrl"),
        ("CosmosDb--AgentsEndpoint",    "CosmosDb:AgentsEndpoint"),
        ("CosmosDb--AgentsDatabase",    "CosmosDb:AgentsDatabase"),
        ("Storage--ImagesEndpoint",     "Storage:ImagesEndpoint"),
        ("Storage--ImagesAccountName",  "Storage:ImagesAccountName"),
        ("Storage--ImagesContainer",    "Storage:ImagesContainer"),
        ("AzureAd--TenantId",           "AzureAd:TenantId"),
        ("AzureAd--BffClientId",        "AzureAd:BffClientId"),
        ("Bff--Hostname",               "Bff:Hostname"),
    ],
    ["blazor-ui"] =
    [
        ("Bff--BaseUrl",                "Bff:BaseUrl"),
        ("AzureAd--BffClientId",        "AzureAd:BffClientId"),
        ("AzureAd--TenantId",           "AzureAd:TenantId"),
    ],
};

// ── Collect unique KV secrets ─────────────────────────────────────────────
var allSecretNames = componentManifest.Values
    .SelectMany(entries => entries.Select(e => e.KvSecret))
    .Distinct()
    .OrderBy(n => n)
    .ToList();

var credential = new DefaultAzureCredential();
var client = new SecretClient(new Uri(keyVaultUri), credential);

// ── Fetch secrets from Key Vault ──────────────────────────────────────────
var secretValues = new Dictionary<string, string>();
var found = 0;

Console.WriteLine("  Fetching secrets from Key Vault...");
Console.WriteLine();

foreach (var secretName in allSecretNames)
{
    try
    {
        var secret = await client.GetSecretAsync(secretName);
        secretValues[secretName] = secret.Value.Value;
        Console.WriteLine($"  ✓ {secretName}");
        found++;
    }
    catch (Azure.RequestFailedException ex) when (ex.Status == 404)
    {
        Console.WriteLine($"  ⚠ {secretName} — not found, skipping");
        secretValues[secretName] = "";
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  ✗ {secretName} — error: {ex.Message}");
        secretValues[secretName] = "";
    }
}

Console.WriteLine();
Console.WriteLine($"  Fetched {found}/{allSecretNames.Count} secrets");
Console.WriteLine();

// ── Write per-component appsettings.json ──────────────────────────────────
// Resolve src/ directory (config-sync sits at src/config-sync/)
var srcDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

Console.WriteLine("  Writing per-component appsettings.Development.json files...");
Console.WriteLine();

foreach (var (component, entries) in componentManifest)
{
    var componentDir = Path.Combine(srcDir, component);
    if (!Directory.Exists(componentDir))
    {
        Console.WriteLine($"  ⚠ {component}/ — directory not found, skipping");
        continue;
    }

    var root = new JsonObject();
    foreach (var (kvSecret, configKey) in entries)
    {
        var value = secretValues.GetValueOrDefault(kvSecret, "");
        SetNestedValue(root, configKey, value);
    }

    var outputPath = Path.Combine(componentDir, "appsettings.Development.json");
    var json = root.ToJsonString(jsonOptions);
    await File.WriteAllTextAsync(outputPath, json + Environment.NewLine);
    Console.WriteLine($"  ✓ {component}/appsettings.Development.json ({entries.Length} keys)");
}

Console.WriteLine();
Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine("  Done! Each component has its own appsettings.Development.json.");
Console.WriteLine("═══════════════════════════════════════════════════════════");

// ── Helper: set a nested value in a JsonObject using : notation ───────────
static void SetNestedValue(JsonObject root, string configKey, string value)
{
    var segments = configKey.Split(':');
    var current = root;

    for (var i = 0; i < segments.Length - 1; i++)
    {
        if (current[segments[i]] is not JsonObject child)
        {
            child = new JsonObject();
            current[segments[i]] = child;
        }

        current = child;
    }

    current[segments[^1]] = value;
}
