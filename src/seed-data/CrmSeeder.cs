using System.Globalization;
using System.Text.Json;
using Microsoft.Azure.Cosmos;

namespace seed_data;

/// <summary>
/// Seeds Cosmos DB containers from CSV files in the contoso-crm folder.
/// Each CSV file maps to a Cosmos DB container. Rows are parsed into JSON
/// documents and upserted with the correct partition key.
/// </summary>
public static class CrmSeeder
{
    // Maps CSV filename (without extension) to (container name, partition key property)
    private static readonly Dictionary<string, (string Container, string PartitionKey)> ContainerMap = new()
    {
        ["customers"]         = ("Customers",        "id"),
        ["subscriptions"]     = ("Subscriptions",    "customer_id"),
        ["products"]          = ("Products",         "category"),
        ["promotions"]        = ("Promotions",       "id"),
        ["invoices"]          = ("Invoices",         "subscription_id"),
        ["payments"]          = ("Payments",         "invoice_id"),
        ["orders"]            = ("Orders",           "customer_id"),
        ["support-tickets"]   = ("SupportTickets",   "customer_id"),
        ["data-usage"]        = ("DataUsage",        "subscription_id"),
        ["service-incidents"] = ("ServiceIncidents", "subscription_id"),
        ["security-logs"]     = ("SecurityLogs",     "customer_id"),
    };

    public static async Task SeedAsync(Database database, string crmFolder)
    {
        if (!Directory.Exists(crmFolder))
        {
            Console.WriteLine($"  CRM folder not found: {crmFolder}");
            return;
        }

        var csvFiles = Directory.GetFiles(crmFolder, "*.csv");
        Console.WriteLine($"  Found {csvFiles.Length} CSV files in {Path.GetFileName(crmFolder)}/\n");

        foreach (var csvFile in csvFiles)
        {
            var fileName = Path.GetFileNameWithoutExtension(csvFile);

            if (!ContainerMap.TryGetValue(fileName, out var mapping))
            {
                Console.WriteLine($"  ⚠ Skipping {fileName}.csv — no container mapping defined");
                continue;
            }

            var container = database.GetContainer(mapping.Container);
            var documents = ParseCsv(csvFile);

            int count = 0;
            foreach (var doc in documents)
            {
                // Extract partition key value
                if (!doc.TryGetValue(mapping.PartitionKey, out var pkValue))
                {
                    Console.WriteLine($"  ⚠ Row missing partition key '{mapping.PartitionKey}' in {fileName}.csv, skipping");
                    continue;
                }

                var json = JsonSerializer.Serialize(doc);
                using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

                await container.UpsertItemStreamAsync(stream, new PartitionKey(pkValue?.ToString() ?? ""));
                count++;
            }

            Console.WriteLine($"  ✓ {mapping.Container}: {count} documents upserted");
        }
    }

    private static List<Dictionary<string, object?>> ParseCsv(string filePath)
    {
        var results = new List<Dictionary<string, object?>>();
        var lines = File.ReadAllLines(filePath);

        if (lines.Length < 2) return results;

        var headers = ParseCsvLine(lines[0]);

        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var values = ParseCsvLine(line);
            var doc = new Dictionary<string, object?>();

            for (int j = 0; j < headers.Length && j < values.Length; j++)
            {
                doc[headers[j]] = ConvertValue(values[j]);
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

    private static object? ConvertValue(string value)
    {
        if (string.IsNullOrEmpty(value)) return null;

        // Boolean
        if (value.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
        if (value.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;

        // Integer
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intVal))
            return intVal;

        // Decimal/double
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var dblVal))
            return dblVal;

        // String (default)
        return value;
    }
}
