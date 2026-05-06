using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Contoso.BlazorUi.Models;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace Contoso.BlazorUi.Services;

public sealed class BffApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient httpClient;       // authenticated calls (BFF requires JWT)
    private readonly HttpClient publicHttpClient; // catalog calls (anonymous-friendly endpoints)
    private readonly AuthStateProvider authStateProvider;

    /// <summary>
    /// Production constructor: separate HttpClients for authenticated vs.
    /// public BFF endpoints. The public client must NOT carry the MSAL
    /// auth handler so anonymous catalog browsing works without a token.
    /// </summary>
    public BffApiClient(HttpClient authClient, HttpClient publicClient, AuthStateProvider authStateProvider)
    {
        this.httpClient = authClient;
        this.publicHttpClient = publicClient;
        this.authStateProvider = authStateProvider;
    }

    /// <summary>
    /// Test-friendly constructor: routes both auth and public calls through
    /// the same HttpClient (typically a stub handler that returns canned
    /// responses regardless of bearer token).
    /// </summary>
    public BffApiClient(HttpClient httpClient, AuthStateProvider authStateProvider)
        : this(httpClient, httpClient, authStateProvider) { }

    public async Task<ChatResponse> SendChatAsync(ChatRequest request, CancellationToken ct = default)
    {
        using var httpRequest = CreateRequest(HttpMethod.Post, "/api/v1/chat");
        httpRequest.Content = JsonContent.Create(request, options: JsonOptions);

        using var response = await httpClient.SendAsync(httpRequest, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ChatResponse>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException("Chat response was empty.");
    }

    // Streams Server-Sent Events from the BFF. Each yielded ChatStreamEvent
    // carries the SSE event name + raw data JSON. The caller deserializes
    // the data based on Event using the typed records in
    // Contoso.BlazorUi.Models.ChatStreamEvent.cs.
    //
    // Browser interop: SetBrowserResponseStreamingEnabled(true) tells the
    // WASM HttpClient to expose the response body as a streaming Stream
    // instead of buffering the whole thing — this is what makes tokens
    // visible as they arrive.
    public async IAsyncEnumerable<ChatStreamEvent> SendChatStreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var httpRequest = CreateRequest(HttpMethod.Post, "/api/v1/chat/stream");
        httpRequest.Content = JsonContent.Create(request, options: JsonOptions);
        httpRequest.SetBrowserResponseStreamingEnabled(true);

        using var response = await httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        // SSE state machine (WHATWG-compliant subset):
        //   - Accumulate `data:` lines per event block, joined by '\n'.
        //   - Default event name is "message".
        //   - ':' lines are comments — ignored.
        //   - Blank line dispatches the buffered event.
        //   - Field/value separator is the FIRST ':'; an optional single
        //     space after the colon is stripped.
        //   - id / retry fields are ignored (no reconnect support needed).
        string? eventName = null;
        var dataBuffer = new System.Text.StringBuilder();

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            if (line.Length == 0)
            {
                if (dataBuffer.Length > 0)
                {
                    yield return new ChatStreamEvent(eventName ?? "message", dataBuffer.ToString());
                }
                eventName = null;
                dataBuffer.Clear();
                continue;
            }

            if (line.StartsWith(":", StringComparison.Ordinal))
            {
                continue;
            }

            var colonIdx = line.IndexOf(':');
            string field, value;
            if (colonIdx < 0)
            {
                field = line;
                value = string.Empty;
            }
            else
            {
                field = line.Substring(0, colonIdx);
                value = line.Substring(colonIdx + 1);
                if (value.StartsWith(" ", StringComparison.Ordinal))
                {
                    value = value.Substring(1);
                }
            }

            switch (field)
            {
                case "event":
                    eventName = value;
                    break;
                case "data":
                    if (dataBuffer.Length > 0) dataBuffer.Append('\n');
                    dataBuffer.Append(value);
                    break;
            }
        }

        // Final block without trailing blank line.
        // Match the BFF parser: dispatch when EITHER a named event OR data was
        // accumulated. WHATWG allows event-name-only blocks (e.g. a `done`
        // sentinel without a body).
        if (eventName is not null || dataBuffer.Length > 0)
        {
            yield return new ChatStreamEvent(eventName ?? "message", dataBuffer.ToString());
        }
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

        // Catalog browsing is anonymous-friendly: send via the public
        // client so no Authorization header is attached. Authenticated
        // visitors still hit the same BFF endpoint without a token because
        // products are not per-customer data.
        using var httpRequest = CreateRequest(HttpMethod.Get, url);
        using var response = await publicHttpClient.SendAsync(httpRequest, ct);
        response.EnsureSuccessStatusCode();

        var products = await response.Content.ReadFromJsonAsync<IReadOnlyList<Product>>(JsonOptions, ct);
        return products ?? Array.Empty<Product>();
    }

    public async Task<Product?> GetProductAsync(string id, CancellationToken ct = default)
    {
        using var httpRequest = CreateRequest(HttpMethod.Get, $"/api/v1/products/{id}");
        using var response = await publicHttpClient.SendAsync(httpRequest, ct);
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

    // Returns the BFF host (no trailing slash) so callers that build their
    // own /api/v1/images/... URLs — most importantly the chat markdown
    // renderer — can produce absolute, cross-origin URLs that resolve to
    // the BFF rather than the WASM host. Without this, <img src="/api/v1/
    // images/foo.png"> resolves to the UI host (e.g. localhost:5008) and
    // returns 404 because that route only exists on the BFF (5007).
    public string BffBaseUrl =>
        httpClient.BaseAddress?.ToString().TrimEnd('/') ?? string.Empty;

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
