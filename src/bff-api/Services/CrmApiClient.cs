namespace Contoso.BffApi.Services;

public sealed class CrmApiClient
{
    private readonly HttpClient _httpClient;

    public CrmApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task<HttpResponseMessage> GetCustomerAsync(string customerId, CancellationToken ct = default)
        => _httpClient.GetAsync($"/api/v1/customers/{customerId}", ct);

    public Task<HttpResponseMessage> GetCustomerOrdersAsync(string customerId, CancellationToken ct = default)
        => _httpClient.GetAsync($"/api/v1/customers/{customerId}/orders", ct);

    public Task<HttpResponseMessage> GetProductsAsync(
        string? query, string? category, bool? inStockOnly, CancellationToken ct = default)
    {
        var queryString = BuildQueryString(("query", query), ("category", category),
            ("in_stock_only", inStockOnly?.ToString().ToLowerInvariant()));
        return _httpClient.GetAsync($"/api/v1/products{queryString}", ct);
    }

    public Task<HttpResponseMessage> GetProductByIdAsync(string id, CancellationToken ct = default)
        => _httpClient.GetAsync($"/api/v1/products/{id}", ct);

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
