using System.Globalization;
using System.Text;
using Contoso.CrmApi.Models;

namespace Contoso.CrmApi.Services;

public static class CsvDataLoader
{
    public static List<Customer> LoadCustomers(string path) =>
        LoadCsv(path, fields => new Customer
        {
            Id = GetField(fields, 0),
            FirstName = GetField(fields, 1),
            LastName = GetField(fields, 2),
            Email = GetField(fields, 3),
            Phone = GetField(fields, 4),
            Address = GetField(fields, 5),
            LoyaltyTier = GetField(fields, 6),
            AccountStatus = GetField(fields, 7),
            CreatedDate = GetField(fields, 8)
        });

    public static List<Order> LoadOrders(string path) =>
        LoadCsv(path, fields => new Order
        {
            Id = GetField(fields, 0),
            CustomerId = GetField(fields, 1),
            OrderDate = GetField(fields, 2),
            Status = GetField(fields, 3),
            TotalAmount = ParseDecimal(GetField(fields, 4)),
            ShippingAddress = GetField(fields, 5),
            TrackingNumber = GetOptionalField(fields, 6),
            EstimatedDelivery = GetOptionalField(fields, 7)
        });

    public static List<OrderItem> LoadOrderItems(string path) =>
        LoadCsv(path, fields => new OrderItem
        {
            Id = GetField(fields, 0),
            OrderId = GetField(fields, 1),
            ProductId = GetField(fields, 2),
            ProductName = GetField(fields, 3),
            Quantity = ParseInt(GetField(fields, 4)),
            UnitPrice = ParseDecimal(GetField(fields, 5))
        });

    public static List<Product> LoadProducts(string path) =>
        LoadCsv(path, fields => new Product
        {
            Id = GetField(fields, 0),
            Name = GetField(fields, 1),
            Category = GetField(fields, 2),
            Description = GetField(fields, 3),
            Price = ParseDecimal(GetField(fields, 4)),
            InStock = ParseBool(GetField(fields, 5)),
            Rating = ParseDouble(GetField(fields, 6)),
            WeightKg = ParseDouble(GetField(fields, 7)),
            ImageFilename = GetField(fields, 8)
        });

    public static List<Promotion> LoadPromotions(string path) =>
        LoadCsv(path, fields => new Promotion
        {
            Id = GetField(fields, 0),
            Name = GetField(fields, 1),
            Description = GetField(fields, 2),
            DiscountPercent = ParseInt(GetField(fields, 3)),
            EligibleCategories = GetField(fields, 4),
            MinLoyaltyTier = GetField(fields, 5),
            StartDate = GetField(fields, 6),
            EndDate = GetField(fields, 7),
            Active = ParseBool(GetField(fields, 8))
        });

    public static List<SupportTicket> LoadSupportTickets(string path) =>
        LoadCsv(path, fields => new SupportTicket
        {
            Id = GetField(fields, 0),
            CustomerId = GetField(fields, 1),
            OrderId = GetOptionalField(fields, 2),
            Category = GetField(fields, 3),
            Subject = GetField(fields, 4),
            Description = GetField(fields, 5),
            Status = GetField(fields, 6),
            Priority = GetField(fields, 7),
            OpenedAt = GetField(fields, 8),
            ClosedAt = GetOptionalField(fields, 9)
        });

    private static List<T> LoadCsv<T>(string path, Func<IReadOnlyList<string>, T> map)
    {
        var items = new List<T>();
        using var reader = new StreamReader(path);
        _ = reader.ReadLine();

        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var fields = ParseCsvLine(line);
            items.Add(map(fields));
        }

        return items;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    builder.Append('"');
                    i++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (ch == ',' && !inQuotes)
            {
                fields.Add(builder.ToString().Trim());
                builder.Clear();
                continue;
            }

            builder.Append(ch);
        }

        fields.Add(builder.ToString().Trim());
        return fields;
    }

    private static string GetField(IReadOnlyList<string> fields, int index) =>
        index < fields.Count ? fields[index] : string.Empty;

    private static string? GetOptionalField(IReadOnlyList<string> fields, int index)
    {
        if (index >= fields.Count)
        {
            return null;
        }

        var value = fields[index];
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool ParseBool(string value) =>
        bool.Parse(value);

    private static int ParseInt(string value) =>
        int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);

    private static decimal ParseDecimal(string value) =>
        decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);

    private static double ParseDouble(string value) =>
        double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
}
