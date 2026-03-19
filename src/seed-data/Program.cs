using Microsoft.Azure.Cosmos;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using seed_data;

// ---------------------------------------------------------------------------
// Configuration
// ---------------------------------------------------------------------------
var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

// Cosmos DB (CRM operational data)
var cosmosEndpoint = configuration["COSMOSDB_CRM_ENDPOINT"]
    ?? throw new InvalidOperationException("COSMOSDB_CRM_ENDPOINT is not set.");
var databaseName = configuration["COSMOSDB_CRM_DATABASE"]
    ?? throw new InvalidOperationException("COSMOSDB_CRM_DATABASE is not set.");

// ---------------------------------------------------------------------------
// Resolve data folder paths
// ---------------------------------------------------------------------------
// CRM_DATA_PATH overrides for containerized/in-cluster execution
var crmFolder = configuration["CRM_DATA_PATH"];
if (string.IsNullOrEmpty(crmFolder))
{
    // From bin/Debug/net9.0 → src/seed-data → src → repo root → data/
    var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    crmFolder = Path.Combine(repoRoot, "data", "contoso-crm");
}

Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine("  Contoso Outdoors — Cosmos DB Seed Tool");
Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine();
Console.WriteLine($"  Cosmos DB:     {cosmosEndpoint}");
Console.WriteLine($"  Database:      {databaseName}");
Console.WriteLine($"  CRM data:      {crmFolder}");
Console.WriteLine();

// ---------------------------------------------------------------------------
// Create Cosmos DB client
// ---------------------------------------------------------------------------
CosmosClient cosmosClient;
try
{
    var cosmosOptions = new CosmosClientOptions
    {
        UseSystemTextJsonSerializerWithOptions = new System.Text.Json.JsonSerializerOptions(),
    };

    cosmosClient = new CosmosClient(cosmosEndpoint, new DefaultAzureCredential(), cosmosOptions);

    var db = cosmosClient.GetDatabase(databaseName);
    await db.ReadAsync();
    Console.WriteLine($"  ✓ Connected to Cosmos DB database '{databaseName}'");
}
catch (Exception ex)
{
    Console.WriteLine($"  ✗ Failed to connect to Cosmos DB: {ex.Message}");
    Console.WriteLine("    Make sure the account/database exists and credentials are correct.");
    Environment.Exit(1);
}

Console.WriteLine();

// ---------------------------------------------------------------------------
// Seed structured data (CRM → Cosmos DB containers)
// ---------------------------------------------------------------------------
Console.WriteLine("───────────────────────────────────────────────────────────");
Console.WriteLine("  Seeding structured data (CRM → Cosmos DB containers)");
Console.WriteLine("───────────────────────────────────────────────────────────");
Console.WriteLine();
Console.WriteLine("  Creates containers (if not exist) and upserts customer,");
Console.WriteLine("  order, and product data from CSV files into Cosmos DB.");
Console.WriteLine("  Agents query this data via MCP tools.");
Console.WriteLine();

await CrmSeeder.SeedAsync(cosmosClient, databaseName, crmFolder);

// ---------------------------------------------------------------------------
// Link Entra user IDs to Customers (optional — Phase 7)
// ---------------------------------------------------------------------------
// ENTRA_MAPPING is a semicolon-separated list of "customer_id=entra_oid" pairs
// e.g. "101=abc-123;102=def-456"
var entraMapping = configuration["ENTRA_MAPPING"];
if (!string.IsNullOrEmpty(entraMapping))
{
    Console.WriteLine();
    Console.WriteLine("───────────────────────────────────────────────────────────");
    Console.WriteLine("  Linking Entra user IDs to Customers");
    Console.WriteLine("───────────────────────────────────────────────────────────");
    Console.WriteLine();

    await CrmSeeder.LinkEntraIdsAsync(cosmosClient, databaseName, entraMapping);
}

Console.WriteLine();
Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine("  Seeding complete!");
Console.WriteLine("═══════════════════════════════════════════════════════════");
