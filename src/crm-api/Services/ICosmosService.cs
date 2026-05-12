using Contoso.CrmApi.Models;

namespace Contoso.CrmApi.Services;

public interface ICosmosService
{
    // Customers — note: there is intentionally no GetAllCustomersAsync.
    // Enumerating the customer table has no legitimate per-customer use
    // case and is a textbook exfiltration vector if exposed via an MCP
    // tool. Re-add behind real authorization (admin role, separate
    // endpoint) if a back-office surface ever needs it.
    Task<Customer?> GetCustomerByIdAsync(string id, CancellationToken ct = default);

    // Orders
    Task<Order?> GetOrderByIdAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<Order>> GetOrdersByCustomerIdAsync(string customerId, CancellationToken ct = default);
    Task<IReadOnlyList<OrderItem>> GetOrderItemsByOrderIdAsync(string orderId, CancellationToken ct = default);
    Task<(Order order, IReadOnlyList<OrderItem> items)> CreateOrderAsync(
        string customerId,
        string? shippingAddress,
        IEnumerable<(string productId, int quantity)> items,
        CancellationToken ct = default);

    // Products
    Task<IReadOnlyList<Product>> GetProductsAsync(string? query = null, string? category = null, bool? inStockOnly = null, CancellationToken ct = default);
    Task<Product?> GetProductByIdAsync(string id, CancellationToken ct = default);

    // Promotions
    Task<IReadOnlyList<Promotion>> GetAllPromotionsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Promotion>> GetEligiblePromotionsAsync(string loyaltyTier, CancellationToken ct = default);

    // Support Tickets
    Task<IReadOnlyList<SupportTicket>> GetTicketsByCustomerIdAsync(string customerId, bool openOnly = false, CancellationToken ct = default);
    Task<SupportTicket?> GetTicketByIdAsync(string id, string customerId, CancellationToken ct = default);

    // Internal lookup that bypasses the owner check. Reserved for
    // service callbacks (fraud-workflow → /internal/tickets/{id}/...)
    // where the calling service authoritatively knows the ticket id but
    // does NOT have an end-user customer context. Do NOT call this from
    // any endpoint that accepts customer-supplied input.
    Task<SupportTicket?> GetTicketByIdInternalAsync(string id, CancellationToken ct = default);

    Task<SupportTicket> CreateTicketAsync(SupportTicket ticket, CancellationToken ct = default);
    Task<SupportTicket> UpdateTicketAsync(SupportTicket ticket, CancellationToken ct = default);

    // Health
    Task<bool> CheckConnectivityAsync(CancellationToken ct = default);
}
