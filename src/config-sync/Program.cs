using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;

// ---------------------------------------------------------------------------
// Config Sync — pulls secrets from Azure Key Vault into src/appsettings.json
// ---------------------------------------------------------------------------
// Usage:
//   dotnet run -- <key-vault-uri>
//   dotnet run -- https://kv-agentic-ai-001.vault.azure.net/
//
// Authenticates via DefaultAzureCredential (az login locally, managed identity on AKS).
// Writes values to ../appsettings.json so all apps under src/ can use them.
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
Console.WriteLine("  Config Sync — Key Vault → appsettings.json");
Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine();
Console.WriteLine($"  Key Vault: {keyVaultUri}");
Console.WriteLine($"  Auth:      DefaultAzureCredential (az login)");
Console.WriteLine();

// Map Key Vault secret names (hyphens) to appsettings.json keys (underscores)
var secretMapping = new Dictionary<string, string>
{
    ["AZURE-OPENAI-ENDPOINT"]              = "AZURE_OPENAI_ENDPOINT",
    ["AZURE-OPENAI-API-KEY"]               = "AZURE_OPENAI_API_KEY",
    ["AZURE-OPENAI-DEPLOYMENT-NAME"]       = "AZURE_OPENAI_DEPLOYMENT_NAME",
    ["AZURE-OPENAI-EMBEDDING-DEPLOYMENT"]  = "AZURE_OPENAI_EMBEDDING_DEPLOYMENT",
    ["COSMOSDB-AGENTS-ENDPOINT"]           = "COSMOSDB_AGENTS_ENDPOINT",
    ["COSMOSDB-AGENTS-KEY"]                = "COSMOSDB_AGENTS_KEY",
    ["COSMOSDB-AGENTS-DATABASE"]           = "COSMOSDB_AGENTS_DATABASE",
    ["SQL-SERVER-FQDN"]                    = "SQL_SERVER_FQDN",
    ["SQL-DATABASE-NAME"]                  = "SQL_DATABASE_NAME",
    ["SQL-ADMIN-LOGIN"]                    = "SQL_ADMIN_LOGIN",
    ["SQL-ADMIN-PASSWORD"]                 = "SQL_ADMIN_PASSWORD",
    ["STORAGE-IMAGES-ENDPOINT"]            = "STORAGE_IMAGES_ENDPOINT",
    ["STORAGE-IMAGES-ACCOUNT-NAME"]        = "STORAGE_IMAGES_ACCOUNT_NAME",
    ["STORAGE-IMAGES-CONTAINER"]           = "STORAGE_IMAGES_CONTAINER",
    ["STORAGE-IMAGES-KEY"]                 = "STORAGE_IMAGES_KEY",
    ["SEARCH-ENDPOINT"]                    = "SEARCH_ENDPOINT",
    ["SEARCH-ADMIN-KEY"]                   = "SEARCH_ADMIN_KEY",
    ["SEARCH-INDEX-NAME"]                  = "SEARCH_INDEX_NAME",
    ["ENTRA-BFF-CLIENT-ID"]                = "AzureAd__ClientId",
    ["ENTRA-BFF-CLIENT-SECRET"]            = "AzureAd__ClientSecret",
    ["ENTRA-TENANT-ID"]                    = "AzureAd__TenantId",
    ["ENTRA-BFF-HOSTNAME"]                 = "BFF_HOSTNAME",
};

// Connect to Key Vault
var client = new SecretClient(new Uri(keyVaultUri), new DefaultAzureCredential());

// Read secrets
var settings = new JsonObject();
int found = 0;

foreach (var (secretName, configKey) in secretMapping)
{
    try
    {
        var secret = await client.GetSecretAsync(secretName);
        settings[configKey] = secret.Value.Value;
        Console.WriteLine($"  ✓ {secretName} → {configKey}");
        found++;
    }
    catch (Azure.RequestFailedException ex) when (ex.Status == 404)
    {
        Console.WriteLine($"  ⚠ {secretName} — not found in Key Vault, skipping");
        settings[configKey] = "";
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  ✗ {secretName} — error: {ex.Message}");
        settings[configKey] = "";
    }
}

// Write to appsettings.json (in the src/ folder, one level up from config-sync/)
var appSettingsPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "appsettings.json"));

var json = settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
await File.WriteAllTextAsync(appSettingsPath, json);

Console.WriteLine();
Console.WriteLine($"  Wrote {found}/{secretMapping.Count} secrets to {appSettingsPath}");
Console.WriteLine();
Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine("  Done! Apps can now read from appsettings.json.");
Console.WriteLine("═══════════════════════════════════════════════════════════");
