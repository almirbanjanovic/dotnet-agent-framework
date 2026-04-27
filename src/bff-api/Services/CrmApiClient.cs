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

    public Task<HttpResponseMessage> GetHealthAsync(CancellationToken ct = default)
        => _httpClient.GetAsync("/health", ct);
}
