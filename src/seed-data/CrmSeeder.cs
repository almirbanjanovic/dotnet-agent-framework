using System.Globalization;
using Microsoft.Azure.Cosmos;

namespace seed_data;

/// <summary>
/// Seeds Cosmos DB containers from CSV files in the contoso-crm folder.
/// Creates containers if they don't exist, then upserts documents.
/// </summary>
public static class CrmSeeder
{
    // Maps CSV filename (without extension) to container name and partition key path
    private static readonly Dictionary<string, ContainerDef> _containerMap = new()
    {
        ["customers"]       = new("Customers",      "/id"),
        ["orders"]          = new("Orders",          "/customer_id"),
        ["order-items"]     = new("OrderItems",      "/order_id"),
        ["products"]        = new("Products",        "/id"),
        ["promotions"]      = new("Promotions",      "/id"),
        ["support-tickets"] = new("SupportTickets",  "/customer_id"),
    };

    // Fields that should be stored as numbers
    private static readonly HashSet<string> _intFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "order_id", "quantity", "discount_percent"
    };

    private static readonly HashSet<string> _decimalFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "total_amount", "unit_price", "price", "rating", "weight_kg"
    };

    private static readonly HashSet<string> _boolFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "in_stock", "active"
    };

    public static async Task SeedAsync(CosmosClient cosmosClient, string databaseName, string crmFolder)
    {
        if (!Directory.Exists(crmFolder))
        {
            Console.WriteLine($"  CRM folder not found: {crmFolder}");
            return;
        }

        var csvFiles = Directory.GetFiles(crmFolder, "*.csv");
        Console.WriteLine($"  Found {csvFiles.Length} CSV files in {Path.GetFileName(crmFolder)}/\n");

        var database = cosmosClient.GetDatabase(databaseName);

        // Ensure containers exist
        foreach (var (_, containerDef) in _containerMap)
        {
            await database.CreateContainerIfNotExistsAsync(containerDef.ContainerName, containerDef.PartitionKeyPath);
        }

        // Seed data in dependency order (parents before children)
        var seedOrder = new[] { "customers", "products", "promotions", "orders", "order-items", "support-tickets" };

        foreach (var fileName in seedOrder)
        {
            var csvFile = csvFiles.FirstOrDefault(f =>
                Path.GetFileNameWithoutExtension(f).Equals(fileName, StringComparison.OrdinalIgnoreCase));

            if (csvFile is null)
            {
                Console.WriteLine($"  ⚠ {fileName}.csv not found, skipping");
                continue;
            }

            if (!_containerMap.TryGetValue(fileName, out var containerDef))
            {
                Console.WriteLine($"  ⚠ Skipping {fileName}.csv — no container mapping defined");
                continue;
            }

            var container = database.GetContainer(containerDef.ContainerName);
            var rows = ParseCsv(csvFile);
            int count = 0;

            foreach (var row in rows)
            {
                // Cosmos DB requires a string "id" field
                if (!row.ContainsKey("id"))
                {
                    Console.WriteLine($"  ⚠ Row in {fileName}.csv missing 'id' field, skipping");
                    continue;
                }

                // Build a typed document for JSON serialization
                var doc = new Dictionary<string, object?>();
                foreach (var (key, value) in row)
                {
                    doc[key] = ConvertValue(key, value);
                }

                // Ensure id is always a string for Cosmos DB
                doc["id"] = row["id"]!.ToString();

                // Extract partition key value
                var pkPath = containerDef.PartitionKeyPath.TrimStart('/');
                var pkValue = doc.TryGetValue(pkPath, out var pk) ? pk?.ToString() ?? "" : "";

                await UpsertWithRetryAsync(container, doc, new PartitionKey(pkValue));
                count++;
            }

            Console.WriteLine($"  ✓ {containerDef.ContainerName}: {count} documents upserted");
        }

        // Verify all containers have data
        Console.WriteLine();
        Console.WriteLine("  Verifying seeded data...\n");

        bool allPassed = true;
        foreach (var containerDef in _containerMap.Values)
        {
            var container = database.GetContainer(containerDef.ContainerName);
            var query = new QueryDefinition("SELECT VALUE COUNT(1) FROM c");
            using var iterator = container.GetItemQueryIterator<int>(query);
            var response = await iterator.ReadNextAsync();
            var rowCount = response.FirstOrDefault();

            if (rowCount > 0)
            {
                Console.WriteLine($"  ✓ {containerDef.ContainerName}: {rowCount} documents");
            }
            else
            {
                Console.WriteLine($"  ✗ {containerDef.ContainerName}: 0 documents — VERIFICATION FAILED");
                allPassed = false;
            }
        }

        if (!allPassed)
        {
            throw new InvalidOperationException("Data verification failed — one or more containers have 0 documents.");
        }

        Console.WriteLine("\n  ✓ All containers verified successfully");
    }

    private static List<Dictionary<string, string?>> ParseCsv(string filePath)
    {
        var results = new List<Dictionary<string, string?>>();
        var lines = File.ReadAllLines(filePath);

        if (lines.Length < 2) return results;

        var headers = ParseCsvLine(lines[0]);

        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var values = ParseCsvLine(line);
            var doc = new Dictionary<string, string?>();

            for (int j = 0; j < headers.Length && j < values.Length; j++)
            {
                doc[headers[j]] = string.IsNullOrEmpty(values[j]) ? null : values[j];
            }

            results.Add(doc);
        }

        return results;
    }

    private static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        bool inQuotes = false;
        var current = new System.Text.StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString().Trim());
        return fields.ToArray();
    }

    private static object? ConvertValue(string fieldName, string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;

        if (_boolFields.Contains(fieldName))
            return value.Equals("true", StringComparison.OrdinalIgnoreCase);

        if (_intFields.Contains(fieldName))
            return int.TryParse(value, CultureInfo.InvariantCulture, out var i) ? i : value;

        if (_decimalFields.Contains(fieldName))
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : value;

        return value;
    }

    private record ContainerDef(string ContainerName, string PartitionKeyPath);

    /// <summary>
    /// Upserts a document with retry for RBAC propagation delays.
    /// Cosmos DB SQL role assignments can take 1-2 minutes to propagate
    /// to the data plane after creation. This retries 403/5302 errors.
    /// </summary>
    private static async Task UpsertWithRetryAsync(
        Container container, Dictionary<string, object?> doc, PartitionKey pk,
        int maxRetries = 12, int delaySeconds = 5)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await container.UpsertItemAsync(doc, pk);
                return;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                if (attempt == maxRetries)
                    throw;

                if (attempt == 1)
                    Console.WriteLine($"\n  ⏳ RBAC not yet active — retrying ({maxRetries * delaySeconds}s max)...");
                else
                    Console.Write(".");

                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
            }
        }
    }

    /// <summary>
    /// Links Entra user object IDs to customer documents in the Customers container.
    /// </summary>
    /// <param name="cosmosClient">Cosmos DB client.</param>
    /// <param name="databaseName">Database name.</param>
    /// <param name="mapping">Semicolon-separated "customer_id=entra_oid" pairs.</param>
    public static async Task LinkEntraIdsAsync(CosmosClient cosmosClient, string databaseName, string mapping)
    {
        var container = cosmosClient.GetContainer(databaseName, "Customers");

        foreach (var pair in mapping.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length != 2) continue;

            var customerId = parts[0].Trim();
            var entraOid = parts[1].Trim();

            try
            {
                var response = await container.ReadItemAsync<Dictionary<string, object?>>(
                    customerId, new PartitionKey(customerId));
                var doc = response.Resource;
                doc["entra_id"] = entraOid;
                await container.UpsertItemAsync(doc, new PartitionKey(customerId));
                Console.WriteLine($"  ✓ Customer {customerId} → {entraOid[..8]}...");
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Console.WriteLine($"  ⚠ Customer {customerId} not found, skipping");
            }
        }
    }
}
