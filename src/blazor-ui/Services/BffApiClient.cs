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
