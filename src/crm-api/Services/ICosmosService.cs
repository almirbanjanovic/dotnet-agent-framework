using Contoso.CrmApi.Models;

namespace Contoso.CrmApi.Services;

public interface ICosmosService
{
    // Customers
    Task<IReadOnlyList<Customer>> GetAllCustomersAsync(CancellationToken ct = default);
    Task<Customer?> GetCustomerByIdAsync(string id, CancellationToken ct = default);

    // Orders
    Task<Order?> GetOrderByIdAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<Order>> GetOrdersByCustomerIdAsync(string customerId, CancellationToken ct = default);
    Task<IReadOnlyList<OrderItem>> GetOrderItemsByOrderIdAsync(string orderId, CancellationToken ct = default);

    // Products
    Task<IReadOnlyList<Product>> GetProductsAsync(string? query = null, string? category = null, bool? inStockOnly = null, CancellationToken ct = default);
    Task<Product?> GetProductByIdAsync(string id, CancellationToken ct = default);

    // Promotions
    Task<IReadOnlyList<Promotion>> GetAllPromotionsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Promotion>> GetEligiblePromotionsAsync(string loyaltyTier, CancellationToken ct = default);

    // Support Tickets
    Task<IReadOnlyList<SupportTicket>> GetTicketsByCustomerIdAsync(string customerId, bool openOnly = false, CancellationToken ct = default);
    Task<SupportTicket> CreateTicketAsync(SupportTicket ticket, CancellationToken ct = default);

    // Health
    Task<bool> CheckConnectivityAsync(CancellationToken ct = default);
}
