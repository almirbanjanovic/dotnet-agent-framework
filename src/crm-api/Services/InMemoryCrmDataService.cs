using System.Collections.Concurrent;
using Contoso.CrmApi.Models;
using Microsoft.Extensions.Configuration;

namespace Contoso.CrmApi.Services;

public sealed class InMemoryCrmDataService : ICosmosService
{
    private readonly ConcurrentDictionary<string, Customer> _customers;
    private readonly ConcurrentDictionary<string, Order> _orders;
    private readonly ConcurrentDictionary<string, OrderItem> _orderItems;
    private readonly ConcurrentDictionary<string, Product> _products;
    private readonly ConcurrentDictionary<string, Promotion> _promotions;
    private readonly ConcurrentDictionary<string, SupportTicket> _supportTickets;

    // Atomic counter for next order ID so concurrent CreateOrderAsync calls
    // never collide. Initialized from the max existing numeric ID at startup
    // (anything not parseable contributes 0). Using Interlocked.Increment
    // guarantees uniqueness without locking.
    private long _nextOrderId;

    private static readonly string[] s_loyaltyTierOrder = ["Bronze", "Silver", "Gold", "Platinum"];

    public InMemoryCrmDataService(IConfiguration configuration)
    {
        var dataPath = configuration["CrmData:Path"];
        if (string.IsNullOrWhiteSpace(dataPath))
        {
            dataPath = Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "data",
                "contoso-crm");
        }

        dataPath = Path.GetFullPath(dataPath);

        _customers = new ConcurrentDictionary<string, Customer>(
            CsvDataLoader.LoadCustomers(Path.Combine(dataPath, "customers.csv"))
                .ToDictionary(c => c.Id));
        _orders = new ConcurrentDictionary<string, Order>(
            CsvDataLoader.LoadOrders(Path.Combine(dataPath, "orders.csv"))
                .ToDictionary(o => o.Id));
        _orderItems = new ConcurrentDictionary<string, OrderItem>(
            CsvDataLoader.LoadOrderItems(Path.Combine(dataPath, "order-items.csv"))
                .ToDictionary(i => i.Id));
        _products = new ConcurrentDictionary<string, Product>(
            CsvDataLoader.LoadProducts(Path.Combine(dataPath, "products.csv"))
                .ToDictionary(p => p.Id));
        _promotions = new ConcurrentDictionary<string, Promotion>(
            CsvDataLoader.LoadPromotions(Path.Combine(dataPath, "promotions.csv"))
                .ToDictionary(p => p.Id));
        _supportTickets = new ConcurrentDictionary<string, SupportTicket>(
            CsvDataLoader.LoadSupportTickets(Path.Combine(dataPath, "support-tickets.csv"))
                .ToDictionary(t => t.Id));

        // Seed the order-id counter from existing data so the first new
        // order is strictly greater than any seeded order. Non-numeric IDs
        // contribute 0 (so a real numeric ID always wins).
        var maxExistingOrderId = _orders.Keys
            .Select(id => long.TryParse(id, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : 0L)
            .DefaultIfEmpty(1000L)
            .Max();
        _nextOrderId = maxExistingOrderId;
    }

    // ── Customers ──────────────────────────────────────────────────────────

    public Task<Customer?> GetCustomerByIdAsync(string id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _customers.TryGetValue(id, out var customer);
        return Task.FromResult(customer);
    }

    // ── Orders ─────────────────────────────────────────────────────────────

    public Task<Order?> GetOrderByIdAsync(string id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _orders.TryGetValue(id, out var order);
        return Task.FromResult(order);
    }

    public Task<IReadOnlyList<Order>> GetOrdersByCustomerIdAsync(string customerId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<Order> results = _orders.Values
            .Where(order => string.Equals(order.CustomerId, customerId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return Task.FromResult(results);
    }

    public Task<IReadOnlyList<OrderItem>> GetOrderItemsByOrderIdAsync(string orderId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // Project each row with image_filename hydrated from the product
        // catalog. Without this, agents have to guess the filename from
        // the product name and end up emitting 404 image URLs in chat
        // (see: "merino-wool-base-layer-top.png" vs the actual file
        // "merino-base-layer-top.png").
        IReadOnlyList<OrderItem> results = _orderItems.Values
            .Where(item => string.Equals(item.OrderId, orderId, StringComparison.OrdinalIgnoreCase))
            .Select(item => new OrderItem
            {
                Id = item.Id,
                OrderId = item.OrderId,
                ProductId = item.ProductId,
                ProductName = item.ProductName,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                ImageFilename = _products.TryGetValue(item.ProductId, out var product)
                    ? product.ImageFilename
                    : string.Empty
            })
            .ToList();
        return Task.FromResult(results);
    }

    public Task<(Order order, IReadOnlyList<OrderItem> items)> CreateOrderAsync(
        string customerId,
        string? shippingAddress,
        IEnumerable<(string productId, int quantity)> items,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!_customers.TryGetValue(customerId, out var customer))
        {
            throw new InvalidOperationException($"Customer '{customerId}' not found.");
        }

        var resolvedItems = new List<OrderItem>();
        decimal total = 0m;

        foreach (var (productId, quantity) in items)
        {
            if (quantity <= 0)
            {
                throw new InvalidOperationException($"Quantity for '{productId}' must be greater than zero.");
            }

            if (!_products.TryGetValue(productId, out var product))
            {
                throw new InvalidOperationException($"Product '{productId}' not found.");
            }

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

        var nextNumeric = System.Threading.Interlocked.Increment(ref _nextOrderId);
        var orderId = nextNumeric.ToString(System.Globalization.CultureInfo.InvariantCulture);

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

        _orders[orderId] = order;

        for (var index = 0; index < resolvedItems.Count; index++)
        {
            var item = resolvedItems[index];
            item.Id = $"{orderId}-{index + 1}";
            item.OrderId = orderId;
            _orderItems[item.Id] = item;
        }

        return Task.FromResult<(Order, IReadOnlyList<OrderItem>)>((order, resolvedItems));
    }

    public Task<Order> UpdateOrderAsync(Order order, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(order.Id))
        {
            throw new InvalidOperationException("Order.Id is required for update.");
        }
        // Authoritative replace mirrors UpdateTicketAsync.
        _orders[order.Id] = order;
        return Task.FromResult(order);
    }

    // ── Products ───────────────────────────────────────────────────────────

    public Task<IReadOnlyList<Product>> GetProductsAsync(
        string? query = null, string? category = null, bool? inStockOnly = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IEnumerable<Product> results = _products.Values;

        if (!string.IsNullOrWhiteSpace(category))
        {
            results = results.Where(product =>
                string.Equals(product.Category, category, StringComparison.OrdinalIgnoreCase));
        }

        if (inStockOnly == true)
        {
            results = results.Where(product => product.InStock);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var normalizedQuery = query.Trim();
            results = results.Where(product =>
                product.Name.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || product.Description.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase));
        }

        return Task.FromResult((IReadOnlyList<Product>)results.ToList());
    }

    public Task<Product?> GetProductByIdAsync(string id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _products.TryGetValue(id, out var product);
        return Task.FromResult(product);
    }

    // ── Promotions ─────────────────────────────────────────────────────────

    public Task<IReadOnlyList<Promotion>> GetAllPromotionsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<Promotion> results = _promotions.Values
            .Where(promo => promo.Active)
            .ToList();
        return Task.FromResult(results);
    }

    public async Task<IReadOnlyList<Promotion>> GetEligiblePromotionsAsync(string loyaltyTier, CancellationToken ct = default)
    {
        var active = await GetAllPromotionsAsync(ct);
        var customerTierIndex = Array.IndexOf(s_loyaltyTierOrder, loyaltyTier);
        if (customerTierIndex < 0)
        {
            return [];
        }

        return active
            .Where(promo =>
            {
                var promoTierIndex = Array.IndexOf(s_loyaltyTierOrder, promo.MinLoyaltyTier);
                return promoTierIndex >= 0 && customerTierIndex >= promoTierIndex;
            })
            .ToList()
            .AsReadOnly();
    }

    // ── Support Tickets ────────────────────────────────────────────────────

    public Task<IReadOnlyList<SupportTicket>> GetTicketsByCustomerIdAsync(
        string customerId, bool openOnly = false, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IEnumerable<SupportTicket> results = _supportTickets.Values
            .Where(ticket => string.Equals(ticket.CustomerId, customerId, StringComparison.OrdinalIgnoreCase));

        if (openOnly)
        {
            results = results.Where(ticket =>
                string.Equals(ticket.Status, "open", StringComparison.OrdinalIgnoreCase));
        }

        return Task.FromResult((IReadOnlyList<SupportTicket>)results.ToList());
    }

    public Task<SupportTicket> CreateTicketAsync(SupportTicket ticket, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var created = new SupportTicket
        {
            // Honour the caller-supplied id when present (the endpoint
            // generates `ST-{Guid:N}` so both backends agree). Fall back
            // to a fresh id only for legacy callers that still pass an
            // empty Id — mirrors what Cosmos would reject server-side.
            Id = string.IsNullOrWhiteSpace(ticket.Id) ? $"ST-{Guid.NewGuid():N}" : ticket.Id,
            CustomerId = ticket.CustomerId,
            OrderId = ticket.OrderId,
            Category = ticket.Category,
            Subject = ticket.Subject,
            Description = ticket.Description,
            Status = ticket.Status,
            Priority = ticket.Priority,
            OpenedAt = ticket.OpenedAt,
            ClosedAt = ticket.ClosedAt
        };

        _supportTickets[created.Id] = created;
        return Task.FromResult(created);
    }

    public Task<SupportTicket?> GetTicketByIdAsync(string id, string customerId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_supportTickets.TryGetValue(id, out var ticket) &&
            string.Equals(ticket.CustomerId, customerId, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<SupportTicket?>(ticket);
        }
        return Task.FromResult<SupportTicket?>(null);
    }

    public Task<SupportTicket?> GetTicketByIdInternalAsync(string id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // No owner check — this overload exists so service callbacks
        // (fraud-workflow) can update a ticket without an end-user
        // customer context. The endpoint that consumes it is gated at
        // the network boundary, NOT by header validation.
        _supportTickets.TryGetValue(id, out var ticket);
        return Task.FromResult(ticket);
    }

    public Task<SupportTicket> UpdateTicketAsync(SupportTicket ticket, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // Authoritative replace — the endpoint is responsible for loading,
        // mutating, and passing the full record. We don’t merge here.
        _supportTickets[ticket.Id] = ticket;
        return Task.FromResult(ticket);
    }

    // ── Health ──────────────────────────────────────────────────────────────

    public Task<bool> CheckConnectivityAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(true);
    }
}
