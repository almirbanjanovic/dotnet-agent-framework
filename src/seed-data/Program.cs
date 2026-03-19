using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using seed_data;

// ---------------------------------------------------------------------------
// Configuration
// ---------------------------------------------------------------------------
var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

// Azure SQL Database (CRM operational data)
var sqlServerFqdn = configuration["SQL_SERVER_FQDN"]
    ?? throw new InvalidOperationException("SQL_SERVER_FQDN is not set.");
var sqlDatabaseName = configuration["SQL_DATABASE_NAME"]
    ?? throw new InvalidOperationException("SQL_DATABASE_NAME is not set.");
var sqlAccessToken = configuration["SQL_ACCESS_TOKEN"];

string connectionString;
if (!string.IsNullOrEmpty(sqlAccessToken))
{
    // Token-based auth (used when running inside AKS to reach private SQL endpoint)
    connectionString = $"Server={sqlServerFqdn};Database={sqlDatabaseName};Encrypt=True;TrustServerCertificate=False;";
}
else
{
    connectionString = $"Server={sqlServerFqdn};Database={sqlDatabaseName};Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;";
}

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
Console.WriteLine("  Contoso Outdoors — SQL Database Seed Tool");
Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine();
Console.WriteLine($"  SQL Server:    {sqlServerFqdn}");
Console.WriteLine($"  Database:      {sqlDatabaseName}");
Console.WriteLine($"  CRM data:      {crmFolder}");
Console.WriteLine();

// ---------------------------------------------------------------------------
// Verify connectivity
// ---------------------------------------------------------------------------
try
{
    await using var testConnection = new SqlConnection(connectionString);
    if (!string.IsNullOrEmpty(sqlAccessToken))
        testConnection.AccessToken = sqlAccessToken;
    await testConnection.OpenAsync();
    Console.WriteLine($"  ✓ Connected to SQL database '{sqlDatabaseName}'");
}
catch (SqlException ex)
{
    Console.WriteLine($"  ✗ Failed to connect to SQL Database: {ex.Message}");
    Console.WriteLine("    Make sure the database exists and the server/credentials are correct.");
    return;
}

Console.WriteLine();

// ---------------------------------------------------------------------------
// Seed structured data (CRM → SQL tables)
// ---------------------------------------------------------------------------
Console.WriteLine("───────────────────────────────────────────────────────────");
Console.WriteLine("  Seeding structured data (CRM → SQL tables)");
Console.WriteLine("───────────────────────────────────────────────────────────");
Console.WriteLine();
Console.WriteLine("  Creates tables (if not exists) and upserts customer,");
Console.WriteLine("  order, and product data from CSV files into Azure SQL.");
Console.WriteLine("  Agents query this data using standard SQL via MCP tools.");
Console.WriteLine();

await CrmSeeder.SeedAsync(connectionString, crmFolder, sqlAccessToken);

Console.WriteLine();
Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine("  Seeding complete!");
Console.WriteLine("═══════════════════════════════════════════════════════════");
