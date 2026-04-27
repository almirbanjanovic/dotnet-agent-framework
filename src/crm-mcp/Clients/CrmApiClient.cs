using System.Text;
using System.Text.Json;
using Contoso.CrmMcp.Models;
using Microsoft.AspNetCore.WebUtilities;

namespace Contoso.CrmMcp.Clients;

public sealed class CrmApiClient
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public CrmApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<Customer>> GetAllCustomersAsync(CancellationToken ct = default)
    {
        using var response = await _httpClient.GetAsync("/api/v1/customers", ct);
        return await ReadAsync<IReadOnlyList<Customer>>(response, ct);
    }

    public async Task<Customer?> GetCustomerByIdAsync(string id, CancellationToken ct = default)
    {
        using var response = await _httpClient.GetAsync($"/api/v1/customers/{id}", ct);
        return await ReadAsync<Customer>(response, ct);
    }

    public async Task<IReadOnlyList<Order>> GetOrdersByCustomerIdAsync(string customerId, CancellationToken ct = default)
    {
        using var response = await _httpClient.GetAsync($"/api/v1/customers/{customerId}/orders", ct);
        return await ReadAsync<IReadOnlyList<Order>>(response, ct);
    }

    public async Task<Order?> GetOrderByIdAsync(string id, CancellationToken ct = default)
    {
        using var response = await _httpClient.GetAsync($"/api/v1/orders/{id}", ct);
        return await ReadAsync<Order>(response, ct);
    }

    public async Task<IReadOnlyList<OrderItem>> GetOrderItemsByOrderIdAsync(string orderId, CancellationToken ct = default)
    {
        using var response = await _httpClient.GetAsync($"/api/v1/orders/{orderId}/items", ct);
        return await ReadAsync<IReadOnlyList<OrderItem>>(response, ct);
    }

    public async Task<IReadOnlyList<Product>> GetProductsAsync(
        string? query,
        string? category,
        bool? inStockOnly,
        CancellationToken ct = default)
    {
        var queryParams = new Dictionary<string, string?>();

        if (!string.IsNullOrWhiteSpace(query))
        {
            queryParams["query"] = query;
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            queryParams["category"] = category;
        }

        if (inStockOnly.HasValue)
        {
            queryParams["in_stock_only"] = inStockOnly.Value.ToString().ToLowerInvariant();
        }

        var path = queryParams.Count == 0
            ? "/api/v1/products"
            : QueryHelpers.AddQueryString("/api/v1/products", queryParams);

        using var response = await _httpClient.GetAsync(path, ct);
        return await ReadAsync<IReadOnlyList<Product>>(response, ct);
    }

    public async Task<Product?> GetProductByIdAsync(string id, CancellationToken ct = default)
    {
        using var response = await _httpClient.GetAsync($"/api/v1/products/{id}", ct);
        return await ReadAsync<Product>(response, ct);
    }

    public async Task<IReadOnlyList<Promotion>> GetAllPromotionsAsync(CancellationToken ct = default)
    {
        using var response = await _httpClient.GetAsync("/api/v1/promotions", ct);
        return await ReadAsync<IReadOnlyList<Promotion>>(response, ct);
    }

    public async Task<IReadOnlyList<Promotion>> GetEligiblePromotionsAsync(string customerId, CancellationToken ct = default)
    {
        using var response = await _httpClient.GetAsync($"/api/v1/promotions/eligible/{customerId}", ct);
        return await ReadAsync<IReadOnlyList<Promotion>>(response, ct);
    }

    public async Task<IReadOnlyList<SupportTicket>> GetTicketsByCustomerIdAsync(
        string customerId,
        bool openOnly,
        CancellationToken ct = default)
    {
        var path = QueryHelpers.AddQueryString(
            $"/api/v1/customers/{customerId}/tickets",
            new Dictionary<string, string?>
            {
                ["open_only"] = openOnly.ToString().ToLowerInvariant()
            });

        using var response = await _httpClient.GetAsync(path, ct);
        return await ReadAsync<IReadOnlyList<SupportTicket>>(response, ct);
    }

    public async Task<SupportTicket> CreateTicketAsync(CreateTicketRequest request, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(request, s_jsonOptions);
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("/api/v1/tickets", content, ct);
        return await ReadAsync<SupportTicket>(response, ct);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        var content = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"CRM API request failed ({(int)response.StatusCode} {response.ReasonPhrase}). {content}");
        }

        var result = JsonSerializer.Deserialize<T>(content, s_jsonOptions);
        return result ?? throw new InvalidOperationException("CRM API returned an empty response.");
    }
}
