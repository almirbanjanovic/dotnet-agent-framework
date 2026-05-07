using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Contoso.CrmApi.Tests;

public class OrderEndpointBoundsTests : IClassFixture<CrmApiWebApplicationFactory>
{
    private const string CustomerHeader = "X-Customer-Entra-Id";
    private const string SeededCustomerId = "101"; // From customers.csv
    private const string SeededProductId = "P001"; // From products.csv

    private readonly HttpClient _client;

    public OrderEndpointBoundsTests(CrmApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add(CustomerHeader, SeededCustomerId);
    }

    [Fact]
    public async Task PlaceOrder_TooManyLineItems_Returns413()
    {
        // Bound: 100 line items max. 101 items must trip the guard before
        // any Cosmos write happens.
        var items = Enumerable.Range(0, 101)
            .Select(i => new { product_id = SeededProductId, quantity = 1 })
            .ToArray();

        var response = await _client.PostAsJsonAsync(
            "/api/v1/orders",
            new { shipping_address = "1 Main St, Anywhere", items });

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task PlaceOrder_NegativeQuantity_Returns400()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/orders",
            new
            {
                shipping_address = "1 Main St, Anywhere",
                items = new[] { new { product_id = SeededProductId, quantity = -5 } }
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PlaceOrder_AbsurdQuantity_Returns400()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/orders",
            new
            {
                shipping_address = "1 Main St, Anywhere",
                items = new[] { new { product_id = SeededProductId, quantity = 1_000_000 } }
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PlaceOrder_BlankProductId_Returns400()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/orders",
            new
            {
                shipping_address = "1 Main St, Anywhere",
                items = new[] { new { product_id = string.Empty, quantity = 1 } }
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PlaceOrder_OversizeShippingAddress_Returns400()
    {
        var huge = new string('x', 1001);

        var response = await _client.PostAsJsonAsync(
            "/api/v1/orders",
            new
            {
                shipping_address = huge,
                items = new[] { new { product_id = SeededProductId, quantity = 1 } }
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
