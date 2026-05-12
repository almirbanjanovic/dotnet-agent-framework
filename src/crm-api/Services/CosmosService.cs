using System.Net;
using Contoso.CrmApi.Models;
using Microsoft.Azure.Cosmos;

namespace Contoso.CrmApi.Services;

public sealed class CosmosService : ICosmosService
{
    private readonly Container _customers;
    private readonly Container _orders;
    private readonly Container _orderItems;
    private readonly Container _products;
    private readonly Container _promotions;
    private readonly Container _supportTickets;
    private readonly Database _database;

    private static readonly string[] s_loyaltyTierOrder = ["Bronze", "Silver", "Gold", "Platinum"];

    public CosmosService(CosmosClient cosmosClient, IConfiguration configuration)
    {
        var databaseName = configuration["CosmosDb:DatabaseName"]
            ?? throw new InvalidOperationException("CosmosDb:DatabaseName configuration is required.");

        _database = cosmosClient.GetDatabase(databaseName);
        _customers = _database.GetContainer("Customers");
        _orders = _database.GetContainer("Orders");
        _orderItems = _database.GetContainer("OrderItems");
        _products = _database.GetContainer("Products");
        _promotions = _database.GetContainer("Promotions");
        _supportTickets = _database.GetContainer("SupportTickets");
    }

    // ── Customers ──────────────────────────────────────────────────────────

    public async Task<Customer?> GetCustomerByIdAsync(string id, CancellationToken ct = default)
    {
        return await PointReadAsync<Customer>(_customers, id, new PartitionKey(id), ct);
    }

    // ── Orders ─────────────────────────────────────────────────────────────

    public async Task<Order?> GetOrderByIdAsync(string id, CancellationToken ct = default)
    {
        // Cross-partition query — Orders partitioned by /customer_id but we're querying by order id.
        // Accepted trade-off for small dataset (see decisions.md).
        var query = new QueryDefinition("SELECT * FROM c WHERE c.id = @id")
            .WithParameter("@id", id);

        var results = await ExecuteQueryAsync<Order>(_orders, query, ct: ct);
        return results.Count > 0 ? results[0] : null;
    }

    public async Task<IReadOnlyList<Order>> GetOrdersByCustomerIdAsync(string customerId, CancellationToken ct = default)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.customer_id = @customerId")
            .WithParameter("@customerId", customerId);

        return await ExecuteQueryAsync<Order>(_orders, query, new PartitionKey(customerId), ct);
    }

    public async Task<IReadOnlyList<OrderItem>> GetOrderItemsByOrderIdAsync(string orderId, CancellationToken ct = default)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.order_id = @orderId")
            .WithParameter("@orderId", orderId);

        var items = await ExecuteQueryAsync<OrderItem>(_orderItems, query, new PartitionKey(orderId), ct);

        // Hydrate image_filename from the product catalog. Order items
        // stored in Cosmos don't carry it (it lives on Product), so chat
        // agents would otherwise have to guess and emit 404 image URLs.
        // Distinct lookup so we don't query the same product twice for
        // a multi-quantity order.
        var productIds = items
            .Select(i => i.ProductId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var imageByProductId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var productId in productIds)
        {
            var product = await GetProductByIdAsync(productId, ct);
            if (product is not null && !string.IsNullOrWhiteSpace(product.ImageFilename))
            {
                imageByProductId[productId] = product.ImageFilename;
            }
        }
        foreach (var item in items)
        {
            if (imageByProductId.TryGetValue(item.ProductId, out var filename))
            {
                item.ImageFilename = filename;
            }
        }
        return items;
    }

    public async Task<(Order order, IReadOnlyList<OrderItem> items)> CreateOrderAsync(
        string customerId,
        string? shippingAddress,
        IEnumerable<(string productId, int quantity)> items,
        CancellationToken ct = default)
    {
        var customer = await GetCustomerByIdAsync(customerId, ct)
            ?? throw new InvalidOperationException($"Customer '{customerId}' not found.");

        var resolvedItems = new List<OrderItem>();
        decimal total = 0m;

        foreach (var (productId, quantity) in items)
        {
            if (quantity <= 0)
            {
                throw new InvalidOperationException($"Quantity for '{productId}' must be greater than zero.");
            }

            var product = await GetProductByIdAsync(productId, ct)
                ?? throw new InvalidOperationException($"Product '{productId}' not found.");

            if (!product.InStock)
            {
                throw new InvalidOperationException($"Product '{product.Name}' is out of stock.");
            }

            resolvedItems.Add(new OrderItem
            {
                ProductId = product.Id,
                ProductName = product.Name,
                Quantity = quantity,
                UnitPrice = product.Price,
                ImageFilename = product.ImageFilename
            });

            total += product.Price * quantity;
        }

        if (resolvedItems.Count == 0)
        {
            throw new InvalidOperationException("Order must contain at least one item.");
        }

        var orderId = $"O-{Guid.NewGuid():N}";
        var order = new Order
        {
            Id = orderId,
            CustomerId = customerId,
            OrderDate = DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            Status = "processing",
            TotalAmount = total,
            ShippingAddress = string.IsNullOrWhiteSpace(shippingAddress) ? customer.Address : shippingAddress,
            TrackingNumber = null,
            EstimatedDelivery = DateTime.UtcNow.AddDays(5).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
        };

        await _orders.CreateItemAsync(order, new PartitionKey(customerId), cancellationToken: ct);

        for (var index = 0; index < resolvedItems.Count; index++)
        {
            var item = resolvedItems[index];
            item.Id = $"{orderId}-{index + 1}";
            item.OrderId = orderId;
            await _orderItems.CreateItemAsync(item, new PartitionKey(orderId), cancellationToken: ct);
        }

        return (order, resolvedItems);
    }

    // ── Products ───────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<Product>> GetProductsAsync(
        string? query = null, string? category = null, bool? inStockOnly = null, CancellationToken ct = default)
    {
        var conditions = new List<string>();
        var queryDef = new QueryDefinition("SELECT * FROM c");

        if (!string.IsNullOrWhiteSpace(category))
        {
            conditions.Add("c.category = @category");
        }

        if (inStockOnly == true)
        {
            conditions.Add("c.in_stock = true");
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            conditions.Add("(CONTAINS(LOWER(c.name), LOWER(@query)) OR CONTAINS(LOWER(c.description), LOWER(@query)))");
        }

        var sql = "SELECT * FROM c";
        if (conditions.Count > 0)
        {
            sql += " WHERE " + string.Join(" AND ", conditions);
        }

        queryDef = new QueryDefinition(sql);

        if (!string.IsNullOrWhiteSpace(category))
        {
            queryDef = queryDef.WithParameter("@category", category);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            queryDef = queryDef.WithParameter("@query", query);
        }

        return await ExecuteQueryAsync<Product>(_products, queryDef, ct: ct);
    }

    public async Task<Product?> GetProductByIdAsync(string id, CancellationToken ct = default)
    {
        return await PointReadAsync<Product>(_products, id, new PartitionKey(id), ct);
    }

    // ── Promotions ─────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<Promotion>> GetAllPromotionsAsync(CancellationToken ct = default)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.active = true");
        return await ExecuteQueryAsync<Promotion>(_promotions, query, ct: ct);
    }

    public async Task<IReadOnlyList<Promotion>> GetEligiblePromotionsAsync(string loyaltyTier, CancellationToken ct = default)
    {
        // Get all active promotions and filter by tier hierarchy in-memory.
        // Tier hierarchy: Bronze < Silver < Gold < Platinum.
        // A customer qualifies if their tier is >= the promotion's min_loyalty_tier.
        var allActive = await GetAllPromotionsAsync(ct);
        var customerTierIndex = Array.IndexOf(s_loyaltyTierOrder, loyaltyTier);

        if (customerTierIndex < 0)
        {
            return [];
        }

        return allActive
            .Where(p =>
            {
                var promoTierIndex = Array.IndexOf(s_loyaltyTierOrder, p.MinLoyaltyTier);
                return promoTierIndex >= 0 && customerTierIndex >= promoTierIndex;
            })
            .ToList()
            .AsReadOnly();
    }

    // ── Support Tickets ────────────────────────────────────────────────────

    public async Task<IReadOnlyList<SupportTicket>> GetTicketsByCustomerIdAsync(
        string customerId, bool openOnly = false, CancellationToken ct = default)
    {
        var sql = "SELECT * FROM c WHERE c.customer_id = @customerId";
        if (openOnly)
        {
            sql += " AND c.status = 'open'";
        }

        var query = new QueryDefinition(sql)
            .WithParameter("@customerId", customerId);

        return await ExecuteQueryAsync<SupportTicket>(_supportTickets, query, new PartitionKey(customerId), ct);
    }

    public async Task<SupportTicket> CreateTicketAsync(SupportTicket ticket, CancellationToken ct = default)
    {
        var response = await _supportTickets.CreateItemAsync(
            ticket,
            new PartitionKey(ticket.CustomerId),
            cancellationToken: ct);

        return response.Resource;
    }

    public Task<SupportTicket?> GetTicketByIdAsync(string id, string customerId, CancellationToken ct = default)
        => PointReadAsync<SupportTicket>(_supportTickets, id, new PartitionKey(customerId), ct);

    public async Task<SupportTicket> UpdateTicketAsync(SupportTicket ticket, CancellationToken ct = default)
    {
        // Upsert keeps the call idempotent and avoids a separate Read; the
        // endpoint already loaded the prior record to enforce ownership.
        var response = await _supportTickets.UpsertItemAsync(
            ticket,
            new PartitionKey(ticket.CustomerId),
            cancellationToken: ct);
        return response.Resource;
    }

    // ── Health ──────────────────────────────────────────────────────────────

    public async Task<bool> CheckConnectivityAsync(CancellationToken ct = default)
    {
        try
        {
            await _database.ReadAsync(cancellationToken: ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static async Task<T?> PointReadAsync<T>(
        Container container, string id, PartitionKey partitionKey, CancellationToken ct)
    {
        try
        {
            var response = await container.ReadItemAsync<T>(id, partitionKey, cancellationToken: ct);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }
    }

    private static async Task<IReadOnlyList<T>> ExecuteQueryAsync<T>(
        Container container, QueryDefinition queryDefinition, PartitionKey? partitionKey = null, CancellationToken ct = default)
    {
        var results = new List<T>();
        var requestOptions = partitionKey.HasValue
            ? new QueryRequestOptions { PartitionKey = partitionKey.Value }
            : null;

        using var iterator = container.GetItemQueryIterator<T>(queryDefinition, requestOptions: requestOptions);

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(ct);
            results.AddRange(response);
        }

        return results.AsReadOnly();
    }
}
