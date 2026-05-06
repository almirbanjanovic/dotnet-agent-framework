using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace Contoso.BffApi.Tests;

// Regression tests for the BFF GET /api/v1/customers/{id}/orders endpoint.
// The CRM API stores orders and order-items in two separate Cosmos
// containers, so a raw proxy returns orders without items and the UI
// renders "Item details unavailable". The BFF must aggregate the two
// calls.
[Collection(nameof(BffApiFactoryCollection))]
public class OrdersEnrichmentTests
{
    [Fact]
    public async Task GetOrders_EnrichesEachOrderWithItemsFromCrmApi()
    {
        // CRM stub: respond differently to /customers/{id}/orders vs
        // /orders/{id}/items. The BFF should fan out to both.
        var ordersJson =
            """
            [
              {"id":"1007","customer_id":"107","status":"shipped","total_amount":42.50,"order_date":"2025-01-15"},
              {"id":"1008","customer_id":"107","status":"delivered","total_amount":99.99,"order_date":"2025-01-20"}
            ]
            """;
        var items1007 =
            """[{"id":"li-1","order_id":"1007","product_id":"p-1","product_name":"Merino Base Layer Top","quantity":1,"unit_price":42.50}]""";
        var items1008 =
            """[{"id":"li-2","order_id":"1008","product_id":"p-2","product_name":"Trail Tent","quantity":1,"unit_price":99.99}]""";

        using var factory = new BffApiWebApplicationFactory(
            crmResponder: req =>
            {
                var path = req.RequestUri!.AbsolutePath;
                if (path == "/api/v1/customers/107/orders")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(ordersJson, Encoding.UTF8, "application/json")
                    };
                }
                if (path == "/api/v1/orders/1007/items")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(items1007, Encoding.UTF8, "application/json")
                    };
                }
                if (path == "/api/v1/orders/1008/items")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(items1008, Encoding.UTF8, "application/json")
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/customers/107/orders");
        request.Headers.Add("X-Customer-Id", "107");
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(body);
        var array = doc.RootElement.EnumerateArray().ToArray();
        array.Should().HaveCount(2);

        // Order 1007 must come back exactly once and carry its items array.
        var order1007 = array.Single(o => o.GetProperty("id").GetString() == "1007");
        order1007.TryGetProperty("items", out var items).Should().BeTrue(
            "the BFF must enrich each order with its items so the UI does not show 'Item details unavailable'");
        items.GetArrayLength().Should().Be(1);
        items[0].GetProperty("product_name").GetString().Should().Be("Merino Base Layer Top");

        var order1008 = array.Single(o => o.GetProperty("id").GetString() == "1008");
        order1008.GetProperty("items").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task GetOrders_ItemsFetchFails_OrderStillRendered()
    {
        // Best-effort enrichment: if items can't be fetched for one order,
        // the order itself must still come back so the UI can render the
        // header (status, total, tracking) instead of the whole list
        // failing.
        var ordersJson =
            """[{"id":"1007","customer_id":"107","status":"shipped","total_amount":42.50,"order_date":"2025-01-15"}]""";

        using var factory = new BffApiWebApplicationFactory(
            crmResponder: req =>
            {
                var path = req.RequestUri!.AbsolutePath;
                if (path == "/api/v1/customers/107/orders")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(ordersJson, Encoding.UTF8, "application/json")
                    };
                }
                // Items endpoint blows up.
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            });
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/customers/107/orders");
        request.Headers.Add("X-Customer-Id", "107");
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var orders = doc.RootElement.EnumerateArray().ToArray();
        orders.Should().HaveCount(1);
        orders[0].GetProperty("id").GetString().Should().Be("1007");
        // The "items" property is omitted when the upstream call fails;
        // the UI's "order.Items?.Count > 0" check then falls back to the
        // (now rare) "Item details unavailable" branch for that one order.
        orders[0].TryGetProperty("items", out _).Should().BeFalse();
    }

    [Fact]
    public async Task GetOrders_EmptyOrdersArray_ReturnsEmptyArray()
    {
        // No orders → no fan-out, no extra calls, just an empty array.
        using var factory = new BffApiWebApplicationFactory(
            crmResponder: _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            });
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/customers/107/orders");
        request.Headers.Add("X-Customer-Id", "107");
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task GetOrders_RouteIdDoesNotMatchAuthenticatedCustomer_ReturnsNotFound()
    {
        // Owner-only authorisation: a signed-in customer can only fetch
        // their OWN orders. Without this gate any authenticated user
        // could enumerate /customers/{id}/orders and read other
        // customers' purchase history. We return 404 (not 403) so an
        // attacker cannot probe which customer ids exist.
        var crmCallCount = 0;
        using var factory = new BffApiWebApplicationFactory(
            crmResponder: _ =>
            {
                Interlocked.Increment(ref crmCallCount);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json")
                };
            });
        var client = factory.CreateClient();

        // Authenticated as 107, asking for 108's orders.
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/customers/108/orders");
        request.Headers.Add("X-Customer-Id", "107");
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        crmCallCount.Should().Be(0, "the BFF must reject the cross-customer request before touching the CRM API");
    }

    [Fact]
    public async Task GetCustomer_RouteIdDoesNotMatchAuthenticatedCustomer_ReturnsNotFound()
    {
        // Same owner-only gate on /customers/{id} (the profile endpoint
        // hit by the BFF /me path indirectly and by direct GET).
        var crmCallCount = 0;
        using var factory = new BffApiWebApplicationFactory(
            crmResponder: _ =>
            {
                Interlocked.Increment(ref crmCallCount);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
            });
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/customers/108");
        request.Headers.Add("X-Customer-Id", "107");
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        crmCallCount.Should().Be(0);
    }
}
