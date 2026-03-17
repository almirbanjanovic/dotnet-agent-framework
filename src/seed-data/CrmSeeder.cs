using System.Globalization;
using Microsoft.Data.SqlClient;

namespace seed_data;

/// <summary>
/// Seeds Azure SQL Database tables from CSV files in the contoso-crm folder.
/// Creates tables if they don't exist, then upserts rows using MERGE.
/// </summary>
public static class CrmSeeder
{
    // Maps CSV filename (without extension) to table name and column definitions
    private static readonly Dictionary<string, TableDef> TableMap = new()
    {
        ["customers"] = new("Customers", "id", new[]
        {
            ("id", "VARCHAR(10)", true),
            ("first_name", "VARCHAR(50)", false),
            ("last_name", "VARCHAR(50)", false),
            ("email", "VARCHAR(100)", false),
            ("phone", "VARCHAR(20)", false),
            ("address", "VARCHAR(200)", false),
            ("loyalty_tier", "VARCHAR(20)", false),
            ("account_status", "VARCHAR(20)", false),
            ("created_date", "DATE", false),
            ("entra_id", "VARCHAR(36)", false),
        }),
        ["orders"] = new("Orders", "id", new[]
        {
            ("id", "INT", true),
            ("customer_id", "VARCHAR(10)", false),
            ("order_date", "DATE", false),
            ("status", "VARCHAR(20)", false),
            ("total_amount", "DECIMAL(10,2)", false),
            ("shipping_address", "VARCHAR(200)", false),
            ("tracking_number", "VARCHAR(50)", false),
            ("estimated_delivery", "DATE", false),
        }),
        ["order-items"] = new("OrderItems", "id", new[]
        {
            ("id", "INT", true),
            ("order_id", "INT", false),
            ("product_id", "VARCHAR(10)", false),
            ("product_name", "VARCHAR(100)", false),
            ("quantity", "INT", false),
            ("unit_price", "DECIMAL(10,2)", false),
        }),
        ["products"] = new("Products", "id", new[]
        {
            ("id", "VARCHAR(10)", true),
            ("name", "VARCHAR(100)", false),
            ("category", "VARCHAR(50)", false),
            ("description", "VARCHAR(500)", false),
            ("price", "DECIMAL(10,2)", false),
            ("in_stock", "BIT", false),
            ("rating", "DECIMAL(3,1)", false),
            ("weight_kg", "DECIMAL(5,2)", false),
            ("image_filename", "VARCHAR(100)", false),
        }),
        ["promotions"] = new("Promotions", "id", new[]
        {
            ("id", "VARCHAR(20)", true),
            ("name", "VARCHAR(100)", false),
            ("description", "VARCHAR(500)", false),
            ("discount_percent", "INT", false),
            ("eligible_categories", "VARCHAR(200)", false),
            ("min_loyalty_tier", "VARCHAR(20)", false),
            ("start_date", "DATE", false),
            ("end_date", "DATE", false),
            ("active", "BIT", false),
        }),
        ["support-tickets"] = new("SupportTickets", "id", new[]
        {
            ("id", "VARCHAR(20)", true),
            ("customer_id", "VARCHAR(10)", false),
            ("order_id", "INT", false),
            ("category", "VARCHAR(30)", false),
            ("subject", "VARCHAR(200)", false),
            ("description", "VARCHAR(1000)", false),
            ("status", "VARCHAR(20)", false),
            ("priority", "VARCHAR(20)", false),
            ("opened_at", "DATE", false),
            ("closed_at", "DATE", false),
        }),
    };

    public static async Task SeedAsync(string connectionString, string crmFolder)
    {
        if (!Directory.Exists(crmFolder))
        {
            Console.WriteLine($"  CRM folder not found: {crmFolder}");
            return;
        }

        var csvFiles = Directory.GetFiles(crmFolder, "*.csv");
        Console.WriteLine($"  Found {csvFiles.Length} CSV files in {Path.GetFileName(crmFolder)}/\n");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        // Create tables in dependency order (parents before children)
        var tableOrder = new[] { "customers", "products", "promotions", "orders", "order-items", "support-tickets" };

        foreach (var fileName in tableOrder)
        {
            if (TableMap.TryGetValue(fileName, out var tableDef))
            {
                await CreateTableIfNotExistsAsync(connection, tableDef);
            }
        }

        // Seed data
        foreach (var csvFile in csvFiles)
        {
            var fileName = Path.GetFileNameWithoutExtension(csvFile);

            if (!TableMap.TryGetValue(fileName, out var tableDef))
            {
                Console.WriteLine($"  ⚠ Skipping {fileName}.csv — no table mapping defined");
                continue;
            }

            var rows = ParseCsv(csvFile);
            int count = 0;

            foreach (var row in rows)
            {
                await UpsertRowAsync(connection, tableDef, row);
                count++;
            }

            Console.WriteLine($"  ✓ {tableDef.TableName}: {count} rows upserted");
        }

        // Verify all tables have data
        Console.WriteLine();
        Console.WriteLine("  Verifying seeded data...\n");

        bool allPassed = true;
        foreach (var tableDef in TableMap.Values)
        {
            await using var cmd = new SqlCommand($"SELECT COUNT(*) FROM {tableDef.TableName}", connection);
            var rowCount = (int)(await cmd.ExecuteScalarAsync())!;

            if (rowCount > 0)
            {
                Console.WriteLine($"  ✓ {tableDef.TableName}: {rowCount} rows");
            }
            else
            {
                Console.WriteLine($"  ✗ {tableDef.TableName}: 0 rows — VERIFICATION FAILED");
                allPassed = false;
            }
        }

        if (!allPassed)
        {
            throw new InvalidOperationException("Data verification failed — one or more tables have 0 rows.");
        }

        Console.WriteLine("\n  ✓ All tables verified successfully");
    }

    private static async Task CreateTableIfNotExistsAsync(SqlConnection connection, TableDef tableDef)
    {
        var columns = string.Join(",\n    ",
            tableDef.Columns.Select(c =>
                $"{c.Name} {c.SqlType}{(c.IsPrimaryKey ? " PRIMARY KEY" : "")}"));

        var sql = $"""
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = '{tableDef.TableName}')
            CREATE TABLE {tableDef.TableName} (
                {columns}
            )
            """;

        await using var cmd = new SqlCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task UpsertRowAsync(SqlConnection connection, TableDef tableDef, Dictionary<string, string?> row)
    {
        var columns = tableDef.Columns.Select(c => c.Name).ToList();
        var paramNames = columns.Select(c => $"@{c}").ToList();
        var updateSet = columns.Where(c => c != tableDef.PrimaryKey)
            .Select(c => $"T.{c} = S.{c}").ToList();

        var sql = $"""
            MERGE {tableDef.TableName} AS T
            USING (SELECT {string.Join(", ", paramNames.Select((p, i) => $"{p} AS {columns[i]}"))}) AS S
            ON T.{tableDef.PrimaryKey} = S.{tableDef.PrimaryKey}
            WHEN MATCHED THEN UPDATE SET {string.Join(", ", updateSet)}
            WHEN NOT MATCHED THEN INSERT ({string.Join(", ", columns)}) VALUES ({string.Join(", ", paramNames)});
            """;

        await using var cmd = new SqlCommand(sql, connection);

        foreach (var col in tableDef.Columns)
        {
            var value = row.TryGetValue(col.Name, out var v) ? v : null;
            cmd.Parameters.AddWithValue($"@{col.Name}", ConvertValue(value, col.SqlType) ?? DBNull.Value);
        }

        await cmd.ExecuteNonQueryAsync();
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

    private static object? ConvertValue(string? value, string sqlType)
    {
        if (string.IsNullOrEmpty(value)) return null;

        return sqlType.ToUpperInvariant() switch
        {
            "BIT" => value.Equals("true", StringComparison.OrdinalIgnoreCase),
            "INT" => int.Parse(value, CultureInfo.InvariantCulture),
            var t when t.StartsWith("DECIMAL") =>
                decimal.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture),
            "DATE" => DateTime.Parse(value, CultureInfo.InvariantCulture),
            _ => value,
        };
    }

    private record TableDef(string TableName, string PrimaryKey, (string Name, string SqlType, bool IsPrimaryKey)[] Columns);
}
