using System.Net.Http.Json;

namespace Contoso.BffApi.Services;

public sealed class CrmApiClient
{
    private readonly HttpClient _httpClient;

    public CrmApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task<HttpResponseMessage> GetCustomerAsync(string customerId, CancellationToken ct = default)
        => _httpClient.GetAsync($"/api/v1/customers/{Uri.EscapeDataString(customerId)}", ct);

    public Task<HttpResponseMessage> GetCustomerOrdersAsync(string customerId, CancellationToken ct = default)
        => _httpClient.GetAsync($"/api/v1/customers/{Uri.EscapeDataString(customerId)}/orders", ct);

    public Task<HttpResponseMessage> GetCustomerTicketsAsync(string customerId, bool? openOnly = null, CancellationToken ct = default)
    {
        // open_only is the same query-param the CRM API accepts; null = omit.
        var qs = openOnly is null ? string.Empty : $"?open_only={(openOnly.Value ? "true" : "false")}";
        return _httpClient.GetAsync($"/api/v1/customers/{Uri.EscapeDataString(customerId)}/tickets{qs}", ct);
    }

    public Task<HttpResponseMessage> UpdateTicketStatusAsync(
        string ticketId, string customerId, string status, CancellationToken ct = default)
    {
        // Body carries customer_id as the test/no-header fallback. The CRM
        // API still prefers the X-Customer-Entra-Id header (forwarded by
        // the BFF's CustomerHeaderHandler) when present, so a malicious
        // body cannot mutate someone else's ticket.
        var content = JsonContent.Create(new
        {
            status,
            customer_id = customerId
        });
        return _httpClient.PatchAsync($"/api/v1/tickets/{Uri.EscapeDataString(ticketId)}", content, ct);
    }

    public Task<HttpResponseMessage> GetOrderItemsAsync(string orderId, CancellationToken ct = default)
        => _httpClient.GetAsync($"/api/v1/orders/{Uri.EscapeDataString(orderId)}/items", ct);

    public Task<HttpResponseMessage> GetProductsAsync(
        string? query, string? category, bool? inStockOnly, CancellationToken ct = default)
    {
        var queryString = BuildQueryString(("query", query), ("category", category),
            ("in_stock_only", inStockOnly?.ToString().ToLowerInvariant()));
        return _httpClient.GetAsync($"/api/v1/products{queryString}", ct);
    }

    public Task<HttpResponseMessage> GetProductByIdAsync(string id, CancellationToken ct = default)
        => _httpClient.GetAsync($"/api/v1/products/{Uri.EscapeDataString(id)}", ct);

    public Task<HttpResponseMessage> PlaceOrderAsync(HttpContent body, CancellationToken ct = default)
        => _httpClient.PostAsync("/api/v1/orders", body, ct);

    public Task<HttpResponseMessage> GetHealthAsync(CancellationToken ct = default)
        => _httpClient.GetAsync("/health", ct);

    private static string BuildQueryString(params (string key, string? value)[] parameters)
    {
        var present = parameters.Where(p => !string.IsNullOrWhiteSpace(p.value)).ToArray();
        if (present.Length == 0)
        {
            return string.Empty;
        }

        var pairs = present.Select(p =>
            $"{Uri.EscapeDataString(p.key)}={Uri.EscapeDataString(p.value!)}");
        return "?" + string.Join("&", pairs);
    }
}
