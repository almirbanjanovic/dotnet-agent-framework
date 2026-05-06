using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Contoso.BffApi.Services;

namespace Contoso.BffApi.Endpoints;

// /api/v1/me                            → resolved customer for the signed-in JWT
// /api/v1/customers/{id}                → fetch a single customer
// /api/v1/customers/{id}/orders         → fetch a customer's orders
// /api/v1/products                      → product catalog (search/filter)
// /api/v1/products/{id}                 → single product
// POST /api/v1/orders                   → place an order for the signed-in customer
//
// All proxies to the CRM API. Identity propagation happens automatically via
// CustomerHeaderHandler attached to CrmApiClient.

internal static class CustomerEndpoints
{
    public static (
        RouteHandlerBuilder me,
        RouteHandlerBuilder customer,
        RouteHandlerBuilder orders,
        RouteHandlerBuilder products,
        RouteHandlerBuilder product,
        RouteHandlerBuilder placeOrder) MapCustomerEndpoints(this IEndpointRouteBuilder app)
    {
        var me = app.MapGet("/me", GetMeAsync);
        var customer = app.MapGet("/customers/{id}", GetCustomerAsync);
        var orders = app.MapGet("/customers/{id}/orders", GetOrdersAsync);
        var products = app.MapGet("/products", GetProductsAsync);
        var product = app.MapGet("/products/{id}", GetProductAsync);
        var placeOrder = app.MapPost("/orders", PlaceOrderAsync);
        return (me, customer, orders, products, product, placeOrder);
    }

    private static async Task<IResult> GetMeAsync(
        CustomerContext customerContext,
        CrmApiClient crmApiClient,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Contoso.BffApi.Endpoints.Me");

        var user = httpContext.User;
        var claimsSummary = string.Join(", ", user.Claims
            .Select(c => $"{c.Type}={c.Value}")
            .Take(20));

        var customerId = customerContext.GetCustomerId();
        logger.LogInformation(
            "GET /me — IsAuthenticated={IsAuth}, ResolvedCustomerId={CustomerId}, Claims=[{Claims}]",
            user.Identity?.IsAuthenticated, customerId, claimsSummary);

        if (string.IsNullOrWhiteSpace(customerId))
        {
            return Results.Json(new
            {
                customerId = (string?)null,
                displayName = (string?)null,
                email = (string?)null
            });
        }

        // Best-effort: read display name + email from the JWT for the
        // top-bar "Welcome, Anna" experience even if the CRM record is
        // missing. The CRM customer is the source of truth for ID.
        var displayName = user.FindFirst("name")?.Value
            ?? user.FindFirst(ClaimTypes.Name)?.Value;
        var email = user.FindFirst("preferred_username")?.Value
            ?? user.FindFirst(ClaimTypes.Email)?.Value
            ?? user.FindFirst(ClaimTypes.Upn)?.Value;

        // Try to enrich from CRM (may 404 if the mapping points at a
        // non-existent customer).
        try
        {
            using var response = await crmApiClient.GetCustomerAsync(customerId, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                var root = doc.RootElement;

                var first = root.TryGetProperty("first_name", out var f) ? f.GetString() : null;
                var last = root.TryGetProperty("last_name", out var l) ? l.GetString() : null;
                if (!string.IsNullOrWhiteSpace(first) || !string.IsNullOrWhiteSpace(last))
                {
                    displayName = $"{first} {last}".Trim();
                }
                if (root.TryGetProperty("email", out var e) && e.GetString() is { } crmEmail)
                {
                    email = crmEmail;
                }
            }
        }
        catch
        {
            // Best-effort enrichment; fall back to JWT claims.
        }

        return Results.Json(new
        {
            customerId,
            displayName,
            email
        });
    }

    private static async Task<IResult> GetCustomerAsync(
        string id,
        CrmApiClient crmApiClient,
        CancellationToken cancellationToken)
    {
        using var response = await crmApiClient.GetCustomerAsync(id, cancellationToken);
        return await ProxyResponseAsync(response, cancellationToken);
    }

    private static async Task<IResult> GetOrdersAsync(
        string id,
        CrmApiClient crmApiClient,
        CancellationToken cancellationToken)
    {
        using var response = await crmApiClient.GetCustomerOrdersAsync(id, cancellationToken);
        return await ProxyResponseAsync(response, cancellationToken);
    }

    private static async Task<IResult> GetProductsAsync(
        string? query,
        string? category,
        bool? in_stock_only,
        CrmApiClient crmApiClient,
        CancellationToken cancellationToken)
    {
        using var response = await crmApiClient.GetProductsAsync(query, category, in_stock_only, cancellationToken);
        return await ProxyResponseAsync(response, cancellationToken);
    }

    private static async Task<IResult> GetProductAsync(
        string id,
        CrmApiClient crmApiClient,
        CancellationToken cancellationToken)
    {
        using var response = await crmApiClient.GetProductByIdAsync(id, cancellationToken);
        return await ProxyResponseAsync(response, cancellationToken);
    }

    private static async Task<IResult> PlaceOrderAsync(
        HttpContext httpContext,
        CrmApiClient crmApiClient,
        CancellationToken cancellationToken)
    {
        // Stream the request body straight to the CRM API. The CRM resolves
        // the customer from the X-Customer-Entra-Id header (added by
        // CustomerHeaderHandler), not from the request body — so a malicious
        // client can't place orders for someone else.
        using var reader = new StreamReader(httpContext.Request.Body);
        var json = await reader.ReadToEndAsync(cancellationToken);
        using var content = new StringContent(json, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await crmApiClient.PlaceOrderAsync(content, cancellationToken);
        return await ProxyResponseAsync(response, cancellationToken);
    }

    private static async Task<IResult> ProxyResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
        return Results.Content(payload, contentType, statusCode: (int)response.StatusCode);
    }
}
