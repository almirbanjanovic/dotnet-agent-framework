using System.ClientModel;
using Azure.AI.OpenAI;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using seed_data;

// ---------------------------------------------------------------------------
// Configuration
// ---------------------------------------------------------------------------
var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var openAiEndpoint = configuration["AZURE_OPENAI_ENDPOINT"]
    ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
var openAiApiKey = configuration["AZURE_OPENAI_API_KEY"]
    ?? throw new InvalidOperationException("AZURE_OPENAI_API_KEY is not set.");
var embeddingDeployment = configuration["AZURE_OPENAI_EMBEDDING_DEPLOYMENT"]
    ?? "text-embedding-ada-002";

var cosmosEndpoint = configuration["COSMOSDB_ENDPOINT"]
    ?? throw new InvalidOperationException("COSMOSDB_ENDPOINT is not set.");
var cosmosKey = configuration["COSMOSDB_KEY"]
    ?? throw new InvalidOperationException("COSMOSDB_KEY is not set.");
var cosmosDatabaseName = configuration["COSMOSDB_DATABASE"]
    ?? "contoso";

// ---------------------------------------------------------------------------
// Resolve data folder paths
// ---------------------------------------------------------------------------
// From bin/Debug/net9.0 → src/seed-data → src → repo root → data/
var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var crmFolder = Path.Combine(repoRoot, "data", "contoso-crm");
var sharePointFolder = Path.Combine(repoRoot, "data", "contoso-sharepoint");

Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine("  Contoso Telecom — Cosmos DB Seed Tool");
Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine();
Console.WriteLine($"  OpenAI endpoint:      {openAiEndpoint}");
Console.WriteLine($"  Embedding deployment: {embeddingDeployment}");
Console.WriteLine($"  Cosmos DB endpoint:   {cosmosEndpoint}");
Console.WriteLine($"  Cosmos DB database:   {cosmosDatabaseName}");
Console.WriteLine($"  CRM data:             {crmFolder}");
Console.WriteLine($"  SharePoint data:      {sharePointFolder}");
Console.WriteLine();

// ---------------------------------------------------------------------------
// Initialize clients
// ---------------------------------------------------------------------------
var openAiClient = new AzureOpenAIClient(
    new Uri(openAiEndpoint),
    new ApiKeyCredential(openAiApiKey));

var embeddingClient = openAiClient.GetEmbeddingClient(embeddingDeployment);

var cosmosClient = new CosmosClient(cosmosEndpoint, cosmosKey, new CosmosClientOptions
{
    SerializerOptions = new CosmosSerializationOptions
    {
        PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
    }
});

var database = cosmosClient.GetDatabase(cosmosDatabaseName);

// Verify connectivity
try
{
    await database.ReadAsync();
    Console.WriteLine($"  ✓ Connected to Cosmos DB database '{cosmosDatabaseName}'");
}
catch (CosmosException ex)
{
    Console.WriteLine($"  ✗ Failed to connect to Cosmos DB: {ex.Message}");
    Console.WriteLine("    Make sure the database exists and the endpoint/key are correct.");
    return;
}

Console.WriteLine();

// ---------------------------------------------------------------------------
// Seed structured data (CRM → Cosmos DB containers)
// ---------------------------------------------------------------------------
Console.WriteLine("───────────────────────────────────────────────────────────");
Console.WriteLine("  Phase 1: Seeding structured data (CRM → containers)");
Console.WriteLine("───────────────────────────────────────────────────────────");
Console.WriteLine();

await CrmSeeder.SeedAsync(database, crmFolder);

Console.WriteLine();

// ---------------------------------------------------------------------------
// Seed unstructured data (SharePoint PDFs → KnowledgeDocuments with vectors)
// ---------------------------------------------------------------------------
Console.WriteLine("───────────────────────────────────────────────────────────");
Console.WriteLine("  Phase 2: Vectorizing documents (SharePoint → RAG store)");
Console.WriteLine("───────────────────────────────────────────────────────────");
Console.WriteLine();

await SharePointSeeder.SeedAsync(database, embeddingClient, sharePointFolder);

Console.WriteLine();
Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine("  Seeding complete!");
Console.WriteLine("═══════════════════════════════════════════════════════════");
