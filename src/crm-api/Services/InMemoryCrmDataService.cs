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
    }

    // ── Customers ──────────────────────────────────────────────────────────

    public Task<IReadOnlyList<Customer>> GetAllCustomersAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<Customer> results = _customers.Values.ToList();
        return Task.FromResult(results);
    }

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
        IReadOnlyList<OrderItem> results = _orderItems.Values
            .Where(item => string.Equals(item.OrderId, orderId, StringComparison.OrdinalIgnoreCase))
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
                UnitPrice = product.Price
            });

            total += product.Price * quantity;
        }

        if (resolvedItems.Count == 0)
        {
            throw new InvalidOperationException("Order must contain at least one item.");
        }

        var nextNumeric = _orders.Keys
            .Select(id => int.TryParse(id, out var parsed) ? parsed : 0)
            .DefaultIfEmpty(1000)
            .Max() + 1;
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
            Id = $"ST-{Guid.NewGuid():N}",
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

    // ── Health ──────────────────────────────────────────────────────────────

    public Task<bool> CheckConnectivityAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(true);
    }
}
