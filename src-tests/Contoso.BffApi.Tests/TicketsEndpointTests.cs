using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace Contoso.BffApi.Tests;

// Coverage for the customer-facing GET /api/v1/customers/{id}/tickets
// endpoint added so signed-in customers can see their support tickets in
// the Blazor UI without going through the chat agent. Mirrors the
// owner-only authorisation on /orders.
[Collection(nameof(BffApiFactoryCollection))]
public class TicketsEndpointTests
{
    private const string TicketsJson =
        """
        [
          {
            "id":"t-1","customer_id":"107","order_id":"1007","category":"return",
            "subject":"Refund for tent","description":"Wrong colour",
            "status":"open","priority":"medium",
            "opened_at":"2025-01-15","closed_at":null
          },
          {
            "id":"t-2","customer_id":"107","order_id":null,"category":"general",
            "subject":"Account update","description":"Please change email",
            "status":"closed","priority":"low",
            "opened_at":"2025-01-10","closed_at":"2025-01-12"
          }
        ]
        """;

    [Fact]
    public async Task GetTickets_ProxiesToCrmApi_ForOwningCustomer()
    {
        string? capturedPath = null;
        using var factory = new BffApiWebApplicationFactory(
            crmResponder: req =>
            {
                capturedPath = req.RequestUri!.PathAndQuery;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(TicketsJson, Encoding.UTF8, "application/json")
                };
            });
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/customers/107/tickets");
        request.Headers.Add("X-Customer-Id", "107");
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        capturedPath.Should().Be("/api/v1/customers/107/tickets",
            "no open_only query was supplied, so the BFF must not invent one");

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task GetTickets_PassesOpenOnlyFilter_ToCrmApi()
    {
        string? capturedPath = null;
        using var factory = new BffApiWebApplicationFactory(
            crmResponder: req =>
            {
                capturedPath = req.RequestUri!.PathAndQuery;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json")
                };
            });
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/customers/107/tickets?open_only=true");
        request.Headers.Add("X-Customer-Id", "107");
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        capturedPath.Should().Be("/api/v1/customers/107/tickets?open_only=true",
            "the BFF must propagate open_only verbatim so the CRM API does the filtering");
    }

    [Fact]
    public async Task GetTickets_RouteIdDoesNotMatchAuthenticatedCustomer_ReturnsNotFound()
    {
        // Owner-only authorisation: a signed-in customer can only fetch
        // their OWN tickets. Without this gate any authenticated user
        // could enumerate /customers/{id}/tickets and read other
        // customers' refund / support history. We return 404 (not 403)
        // so an attacker cannot probe which customer ids exist.
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

        // Authenticated as 107, asking for 108's tickets.
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/customers/108/tickets");
        request.Headers.Add("X-Customer-Id", "107");
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        crmCallCount.Should().Be(0,
            "the BFF must reject the cross-customer request before touching the CRM API");
    }

    [Fact]
    public async Task GetTickets_CrmReturnsEmptyArray_BffPassesThrough()
    {
        using var factory = new BffApiWebApplicationFactory(
            crmResponder: _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            });
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/customers/107/tickets");
        request.Headers.Add("X-Customer-Id", "107");
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task GetTickets_CrmReturnsServerError_BffReturns502WithoutLeakingUpstreamBody()
    {
        // Symmetry with the orders endpoint: do NOT echo upstream error
        // bodies (which may contain stack traces / PII) to the browser.
        // Return a generic 502 + RFC7807 problem-detail instead.
        const string sensitiveCrmBody = """{"trace":"NullReferenceException at Cosmos.X.Y","secret":"connection-string-leak"}""";
        using var factory = new BffApiWebApplicationFactory(
            crmResponder: _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent(sensitiveCrmBody, Encoding.UTF8, "application/json")
            });
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/customers/107/tickets");
        request.Headers.Add("X-Customer-Id", "107");
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("NullReferenceException", "the upstream stack trace must not leak to the browser");
        body.Should().NotContain("connection-string-leak");
    }
}
