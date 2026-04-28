using System.Net;
using System.Text.Json;
using Contoso.CrmMcp.Clients;
using Contoso.CrmMcp.Models;
using FluentAssertions;

namespace Contoso.CrmMcp.Tests;

public sealed class CrmApiClientTests
{
    [Fact]
    public async Task GetAllCustomersAsync_ConstructsCorrectUrl()
    {
        var (client, handler) = CreateClient(TestHttpMessageHandler.CreateJson("[]"));

        _ = await client.GetAllCustomersAsync();

        handler.Request!.Method.Should().Be(HttpMethod.Get);
        handler.Request.RequestUri!.PathAndQuery.Should().Be("/api/v1/customers");
    }

    [Fact]
    public async Task GetCustomerByIdAsync_ConstructsCorrectUrl()
    {
        var (client, handler) = CreateClient(TestHttpMessageHandler.CreateJson("{}"));

        _ = await client.GetCustomerByIdAsync("C-100");

        handler.Request!.RequestUri!.PathAndQuery.Should().Be("/api/v1/customers/C-100");
    }

    [Fact]
    public async Task GetOrdersByCustomerIdAsync_ConstructsCorrectUrl()
    {
        var (client, handler) = CreateClient(TestHttpMessageHandler.CreateJson("[]"));

        _ = await client.GetOrdersByCustomerIdAsync("C-200");

        handler.Request!.RequestUri!.PathAndQuery.Should().Be("/api/v1/customers/C-200/orders");
    }

    [Fact]
    public async Task GetOrderByIdAsync_ConstructsCorrectUrl()
    {
        var (client, handler) = CreateClient(TestHttpMessageHandler.CreateJson("{}"));

        _ = await client.GetOrderByIdAsync("O-123");

        handler.Request!.RequestUri!.PathAndQuery.Should().Be("/api/v1/orders/O-123");
    }

    [Fact]
    public async Task GetOrderItemsByOrderIdAsync_ConstructsCorrectUrl()
    {
        var (client, handler) = CreateClient(TestHttpMessageHandler.CreateJson("[]"));

        _ = await client.GetOrderItemsByOrderIdAsync("O-321");

        handler.Request!.RequestUri!.PathAndQuery.Should().Be("/api/v1/orders/O-321/items");
    }

    [Fact]
    public async Task GetProductsAsync_NoFilters_ConstructsBaseUrl()
    {
        var (client, handler) = CreateClient(TestHttpMessageHandler.CreateJson("[]"));

        _ = await client.GetProductsAsync(null, null, null);

        handler.Request!.RequestUri!.PathAndQuery.Should().Be("/api/v1/products");
    }

    [Fact]
    public async Task GetProductsAsync_AllFilters_ConstructsQueryString()
    {
        var (client, handler) = CreateClient(TestHttpMessageHandler.CreateJson("[]"));

        _ = await client.GetProductsAsync("tent", "camping", true);

        handler.Request!.RequestUri!.AbsolutePath.Should().Be("/api/v1/products");
        var query = ParseQuery(handler.Request.RequestUri!);
        query["query"].Should().Be("tent");
        query["category"].Should().Be("camping");
        query["in_stock_only"].Should().Be("true");
    }

    [Fact]
    public async Task GetProductByIdAsync_ConstructsCorrectUrl()
    {
        var (client, handler) = CreateClient(TestHttpMessageHandler.CreateJson("{}"));

        _ = await client.GetProductByIdAsync("P-444");

        handler.Request!.RequestUri!.PathAndQuery.Should().Be("/api/v1/products/P-444");
    }

    [Fact]
    public async Task GetAllPromotionsAsync_ConstructsCorrectUrl()
    {
        var (client, handler) = CreateClient(TestHttpMessageHandler.CreateJson("[]"));

        _ = await client.GetAllPromotionsAsync();

        handler.Request!.RequestUri!.PathAndQuery.Should().Be("/api/v1/promotions");
    }

    [Fact]
    public async Task GetEligiblePromotionsAsync_ConstructsCorrectUrl()
    {
        var (client, handler) = CreateClient(TestHttpMessageHandler.CreateJson("[]"));

        _ = await client.GetEligiblePromotionsAsync("C-300");

        handler.Request!.RequestUri!.PathAndQuery.Should().Be("/api/v1/promotions/eligible/C-300");
    }

    [Fact]
    public async Task GetTicketsByCustomerIdAsync_ConstructsCorrectUrl()
    {
        var (client, handler) = CreateClient(TestHttpMessageHandler.CreateJson("[]"));

        _ = await client.GetTicketsByCustomerIdAsync("C-500", true);

        handler.Request!.RequestUri!.AbsolutePath.Should().Be("/api/v1/customers/C-500/tickets");
        var query = ParseQuery(handler.Request.RequestUri!);
        query["open_only"].Should().Be("true");
    }

    [Fact]
    public async Task CreateTicketAsync_PostsJsonBody()
    {
        string? requestBody = null;
        var (client, handler) = CreateClient(new TestHttpMessageHandler(async (request, _) =>
        {
            requestBody = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            };
        }));

        var ticketRequest = new CreateTicketRequest
        {
            CustomerId = "C-900",
            OrderId = "O-900",
            Category = "returns",
            Priority = "high",
            Subject = "Broken item",
            Description = "The item arrived damaged."
        };

        _ = await client.CreateTicketAsync(ticketRequest);

        handler.Request!.Method.Should().Be(HttpMethod.Post);
        handler.Request.RequestUri!.PathAndQuery.Should().Be("/api/v1/tickets");
        handler.Request.Content!.Headers.ContentType!.MediaType.Should().Be("application/json");
        requestBody.Should().NotBeNull();
        var payload = JsonDocument.Parse(requestBody!);
        payload.RootElement.GetProperty("customer_id").GetString().Should().Be("C-900");
        payload.RootElement.GetProperty("order_id").GetString().Should().Be("O-900");
        payload.RootElement.GetProperty("category").GetString().Should().Be("returns");
    }

    [Fact]
    public async Task ReadAsync_Non2xxResponse_ThrowsHttpRequestException()
    {
        var (client, _) = CreateClient(TestHttpMessageHandler.CreateJson("bad", HttpStatusCode.BadRequest));

        var act = () => client.GetAllCustomersAsync();

        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("CRM API request failed*");
    }

    [Fact]
    public async Task ReadAsync_ValidJson_DeserializesCorrectly()
    {
        var payload = """
            {"id":"C-123","first_name":"Ada","last_name":"Lovelace","email":"ada@example.com","phone":"555",
            "address":"1 Main St","loyalty_tier":"Gold","account_status":"Active","created_date":"2024-01-01"}
            """;
        var (client, _) = CreateClient(TestHttpMessageHandler.CreateJson(payload));

        var customer = await client.GetCustomerByIdAsync("C-123");

        customer.Should().NotBeNull();
        customer!.Id.Should().Be("C-123");
        customer.FirstName.Should().Be("Ada");
        customer.LoyaltyTier.Should().Be("Gold");
    }

    private static (CrmApiClient Client, TestHttpMessageHandler Handler) CreateClient(TestHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };

        return (new CrmApiClient(httpClient), handler);
    }

    private static IReadOnlyDictionary<string, string> ParseQuery(Uri uri)
    {
        return uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                parts => Uri.UnescapeDataString(parts[0]),
                parts => parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty);
    }
}
