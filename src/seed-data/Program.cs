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

// Operational account (CRM structured data)
var operationalEndpoint = configuration["COSMOSDB_OPERATIONAL_ENDPOINT"]
    ?? throw new InvalidOperationException("COSMOSDB_OPERATIONAL_ENDPOINT is not set.");
var operationalKey = configuration["COSMOSDB_OPERATIONAL_KEY"]
    ?? throw new InvalidOperationException("COSMOSDB_OPERATIONAL_KEY is not set.");
var operationalDatabase = configuration["COSMOSDB_OPERATIONAL_DATABASE"]
    ?? throw new InvalidOperationException("COSMOSDB_OPERATIONAL_DATABASE is not set.");

// Knowledge account (RAG vector store)
var knowledgeEndpoint = configuration["COSMOSDB_KNOWLEDGE_ENDPOINT"]
    ?? throw new InvalidOperationException("COSMOSDB_KNOWLEDGE_ENDPOINT is not set.");
var knowledgeKey = configuration["COSMOSDB_KNOWLEDGE_KEY"]
    ?? throw new InvalidOperationException("COSMOSDB_KNOWLEDGE_KEY is not set.");
var knowledgeDatabase = configuration["COSMOSDB_KNOWLEDGE_DATABASE"]
    ?? throw new InvalidOperationException("COSMOSDB_KNOWLEDGE_DATABASE is not set.");

// ---------------------------------------------------------------------------
// Resolve data folder paths
// ---------------------------------------------------------------------------
// From bin/Debug/net9.0 → src/seed-data → src → repo root → data/
var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var crmFolder = Path.Combine(repoRoot, "data", "contoso-crm");
var sharePointFolder = Path.Combine(repoRoot, "data", "contoso-sharepoint");

Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine("  Contoso Outdoors — Cosmos DB Seed Tool");
Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine();
Console.WriteLine($"  OpenAI endpoint:         {openAiEndpoint}");
Console.WriteLine($"  Embedding deployment:    {embeddingDeployment}");
Console.WriteLine($"  Operational endpoint:    {operationalEndpoint}");
Console.WriteLine($"  Operational database:    {operationalDatabase}");
Console.WriteLine($"  Knowledge endpoint:      {knowledgeEndpoint}");
Console.WriteLine($"  Knowledge database:      {knowledgeDatabase}");
Console.WriteLine($"  CRM data:                {crmFolder}");
Console.WriteLine($"  SharePoint data:         {sharePointFolder}");
Console.WriteLine();

// ---------------------------------------------------------------------------
// Initialize clients
// ---------------------------------------------------------------------------
var openAiClient = new AzureOpenAIClient(
    new Uri(openAiEndpoint),
    new ApiKeyCredential(openAiApiKey));

var embeddingClient = openAiClient.GetEmbeddingClient(embeddingDeployment);

var cosmosClientOptions = new CosmosClientOptions
{
    SerializerOptions = new CosmosSerializationOptions
    {
        PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
    }
};

var operationalClient = new CosmosClient(operationalEndpoint, operationalKey, cosmosClientOptions);
var knowledgeClient = new CosmosClient(knowledgeEndpoint, knowledgeKey, cosmosClientOptions);

var operationalDb = operationalClient.GetDatabase(operationalDatabase);
var knowledgeDb = knowledgeClient.GetDatabase(knowledgeDatabase);

// Verify connectivity
try
{
    await operationalDb.ReadAsync();
    Console.WriteLine($"  ✓ Connected to operational database '{operationalDatabase}'");
    await knowledgeDb.ReadAsync();
    Console.WriteLine($"  ✓ Connected to knowledge database '{knowledgeDatabase}'");
}
catch (CosmosException ex)
{
    Console.WriteLine($"  ✗ Failed to connect to Cosmos DB: {ex.Message}");
    Console.WriteLine("    Make sure the databases exist and the endpoints/keys are correct.");
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
Console.WriteLine("  Loads customer, order, and product data from CSV files");
Console.WriteLine("  into Cosmos DB containers. Agents query this data using");
Console.WriteLine("  standard SQL queries via MCP tools at runtime.");
Console.WriteLine();

await CrmSeeder.SeedAsync(operationalDb, crmFolder);

Console.WriteLine();

// ---------------------------------------------------------------------------
// Seed unstructured data (SharePoint PDFs → KnowledgeDocuments with vectors)
// ---------------------------------------------------------------------------
Console.WriteLine("───────────────────────────────────────────────────────────");
Console.WriteLine("  Phase 2: Vectorizing documents (SharePoint → RAG store)");
Console.WriteLine("───────────────────────────────────────────────────────────");
Console.WriteLine();
Console.WriteLine("  Extracts text from PDFs, chunks it into ~500-token segments,");
Console.WriteLine("  generates 1536-dim vector embeddings via the embedding model,");
Console.WriteLine("  and stores each chunk + vector in KnowledgeDocuments.");
Console.WriteLine("  This enables semantic search (RAG) at query time — agents");
Console.WriteLine("  find relevant documents by meaning, not keyword matching.");
Console.WriteLine();

await SharePointSeeder.SeedAsync(knowledgeDb, embeddingClient, sharePointFolder);

Console.WriteLine();
Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine("  Seeding complete!");
Console.WriteLine("═══════════════════════════════════════════════════════════");
