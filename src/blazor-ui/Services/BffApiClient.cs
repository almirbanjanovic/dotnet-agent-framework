using System.Net.Http.Json;
using System.Text.Json;
using Contoso.BlazorUi.Models;

namespace Contoso.BlazorUi.Services;

public sealed class BffApiClient(HttpClient httpClient, AuthStateProvider authStateProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<ChatResponse> SendChatAsync(ChatRequest request, CancellationToken ct = default)
    {
        using var httpRequest = CreateRequest(HttpMethod.Post, "/api/v1/chat");
        httpRequest.Content = JsonContent.Create(request, options: JsonOptions);

        using var response = await httpClient.SendAsync(httpRequest, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ChatResponse>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException("Chat response was empty.");
    }

    public async Task<MeResponse> GetMeAsync(CancellationToken ct = default)
    {
        using var httpRequest = CreateRequest(HttpMethod.Get, "/api/v1/me");
        using var response = await httpClient.SendAsync(httpRequest, ct);
        response.EnsureSuccessStatusCode();

        var me = await response.Content.ReadFromJsonAsync<MeResponse>(JsonOptions, ct);
        return me ?? new MeResponse();
    }

    public async Task<Customer> GetCustomerAsync(string customerId, CancellationToken ct = default)
    {
        using var httpRequest = CreateRequest(HttpMethod.Get, $"/api/v1/customers/{customerId}");
        using var response = await httpClient.SendAsync(httpRequest, ct);
        response.EnsureSuccessStatusCode();

        var customer = await response.Content.ReadFromJsonAsync<Customer>(JsonOptions, ct);
        return customer ?? throw new InvalidOperationException("Customer response was empty.");
    }

    public async Task<IReadOnlyList<Order>> GetOrdersAsync(string customerId, CancellationToken ct = default)
    {
        using var httpRequest = CreateRequest(HttpMethod.Get, $"/api/v1/customers/{customerId}/orders");
        using var response = await httpClient.SendAsync(httpRequest, ct);
        response.EnsureSuccessStatusCode();

        var orders = await response.Content.ReadFromJsonAsync<IReadOnlyList<Order>>(JsonOptions, ct);
        return orders ?? Array.Empty<Order>();
    }

    public async Task<IReadOnlyList<Product>> GetProductsAsync(
        string? category = null,
        string? query = null,
        bool inStockOnly = false,
        CancellationToken ct = default)
    {
        var url = "/api/v1/products";
        var queryParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(category))
        {
            queryParts.Add($"category={Uri.EscapeDataString(category)}");
        }
        if (!string.IsNullOrWhiteSpace(query))
        {
            queryParts.Add($"query={Uri.EscapeDataString(query)}");
        }
        if (inStockOnly)
        {
            queryParts.Add("in_stock_only=true");
        }
        if (queryParts.Count > 0)
        {
            url += "?" + string.Join("&", queryParts);
        }

        using var httpRequest = CreateRequest(HttpMethod.Get, url);
        using var response = await httpClient.SendAsync(httpRequest, ct);
        response.EnsureSuccessStatusCode();

        var products = await response.Content.ReadFromJsonAsync<IReadOnlyList<Product>>(JsonOptions, ct);
        return products ?? Array.Empty<Product>();
    }

    public async Task<Product?> GetProductAsync(string id, CancellationToken ct = default)
    {
        using var httpRequest = CreateRequest(HttpMethod.Get, $"/api/v1/products/{id}");
        using var response = await httpClient.SendAsync(httpRequest, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Product>(JsonOptions, ct);
    }

    public async Task<PlaceOrderResponse> PlaceOrderAsync(PlaceOrderRequest request, CancellationToken ct = default)
    {
        using var httpRequest = CreateRequest(HttpMethod.Post, "/api/v1/orders");
        httpRequest.Content = JsonContent.Create(request, options: JsonOptions);

        using var response = await httpClient.SendAsync(httpRequest, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Failed to place order ({(int)response.StatusCode}): {error}");
        }

        var result = await response.Content.ReadFromJsonAsync<PlaceOrderResponse>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException("Order response was empty.");
    }

    public string GetImageUrl(string? filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            return string.Empty;
        }

        var baseAddress = httpClient.BaseAddress?.ToString().TrimEnd('/') ?? string.Empty;
        return $"{baseAddress}/api/v1/images/{Uri.EscapeDataString(filename)}";
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string uri)
    {
        var request = new HttpRequestMessage(method, uri);
        var customerId = authStateProvider.CustomerId;
        if (!string.IsNullOrWhiteSpace(customerId))
        {
            request.Headers.Add("X-Customer-Id", customerId);
        }

        return request;
    }
}
