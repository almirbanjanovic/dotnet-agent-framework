using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        CustomerContext customerContext,
        CancellationToken cancellationToken)
    {
        if (!IsAuthorizedForCustomer(id, customerContext))
        {
            return Results.NotFound();
        }

        using var response = await crmApiClient.GetCustomerAsync(id, cancellationToken);
        return await ProxyResponseAsync(response, cancellationToken);
    }

    private static async Task<IResult> GetOrdersAsync(
        string id,
        CrmApiClient crmApiClient,
        CustomerContext customerContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        // Authorization gate: the route id must match the authenticated
        // customer. Without this, any signed-in user could enumerate
        // /customers/{id}/orders and read other customers' purchase
        // history. We return 404 (not 403) so an attacker can't probe
        // which customer ids exist.
        if (!IsAuthorizedForCustomer(id, customerContext))
        {
            return Results.NotFound();
        }

        // The CRM API stores orders and order-items in two separate Cosmos
        // containers (Orders is partitioned by /customer_id; OrderItems by
        // /order_id), so a single request returns orders WITHOUT items. The
        // UI needs the line items to render each order card meaningfully —
        // without enrichment it falls into the "Item details unavailable"
        // branch. This is the BFF's reason for existing: aggregate the two
        // calls so the browser only sees one round-trip.
        var logger = loggerFactory.CreateLogger("Contoso.BffApi.Endpoints.Orders");

        using var ordersResp = await crmApiClient.GetCustomerOrdersAsync(id, cancellationToken);
        if (!ordersResp.IsSuccessStatusCode)
        {
            // Do NOT echo the upstream body to the browser — it may contain
            // internal exception details, stack frames, or PII. Same policy
            // as the chat endpoint's BFF→orchestrator path.
            logger.LogWarning(
                "CRM API returned {StatusCode} for orders of customer {CustomerId}.",
                (int)ordersResp.StatusCode, id);
            return Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Failed to load orders.",
                detail: "The CRM service returned an error. Please try again.");
        }

        var ordersPayload = await ordersResp.Content.ReadAsStringAsync(cancellationToken);
        JsonNode? ordersNode;
        try
        {
            ordersNode = JsonNode.Parse(ordersPayload);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "CRM API returned non-JSON for orders of customer {CustomerId}; passing through.", id);
            return Results.Content(ordersPayload, "application/json", statusCode: (int)ordersResp.StatusCode);
        }

        if (ordersNode is not JsonArray ordersArray || ordersArray.Count == 0)
        {
            return Results.Content(ordersPayload, "application/json", statusCode: (int)ordersResp.StatusCode);
        }

        // Collect order ids in document order so we can correlate the
        // parallel item-fetches back to their orders. Use a tolerant
        // extractor: a single malformed entry must not turn the whole
        // history into a 500.
        var orderIds = new List<string>(ordersArray.Count);
        foreach (var node in ordersArray)
        {
            if (TryGetStringProperty(node, "id", out var orderId))
            {
                orderIds.Add(orderId);
            }
        }

        // DoS cap: never enrich more than 100 orders. The Blazor UI shows
        // the full list of orders (header info), but only the most recent
        // 100 carry items. Without this an upstream bug or a compromised
        // customer with thousands of historical orders could trigger a
        // 5000-call fan-out per page load.
        const int MaxOrdersToEnrich = 100;
        var orderIdsToEnrich = orderIds.Count <= MaxOrdersToEnrich
            ? orderIds
            : orderIds.GetRange(0, MaxOrdersToEnrich);

        // Fan-out item fetches in parallel. Capped to 5 in-flight to avoid
        // hammering the CRM API for customers with large order histories.
        // Best-effort: if a single order's items can't be loaded the order
        // still renders (without items) instead of failing the whole list.
        var itemsByOrderId = new ConcurrentDictionary<string, JsonNode?>();
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = 5
        };
        await Parallel.ForEachAsync(orderIdsToEnrich, parallelOptions, async (orderId, ct) =>
        {
            try
            {
                using var itemsResp = await crmApiClient.GetOrderItemsAsync(orderId, ct);
                if (!itemsResp.IsSuccessStatusCode)
                {
                    logger.LogWarning(
                        "CRM API returned {StatusCode} for items of order {OrderId}; rendering order without items.",
                        (int)itemsResp.StatusCode, orderId);
                    return;
                }
                var itemsPayload = await itemsResp.Content.ReadAsStringAsync(ct);
                itemsByOrderId[orderId] = JsonNode.Parse(itemsPayload);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch items for order {OrderId}; rendering order without items.", orderId);
            }
        });

        // Merge items into the orders array. JsonNode parents are exclusive,
        // so DeepClone before assigning into a different parent.
        foreach (var node in ordersArray)
        {
            if (!TryGetStringProperty(node, "id", out var orderId) || node is not JsonObject obj)
            {
                continue;
            }
            if (itemsByOrderId.TryGetValue(orderId, out var items) && items is not null)
            {
                obj["items"] = items.DeepClone();
            }
        }

        return Results.Content(ordersArray.ToJsonString(), "application/json", statusCode: 200);
    }

    // Centralised, tolerant string-property extractor for JsonNode. Returns
    // false (rather than throwing) when the property is missing, null, or
    // not a JSON string — lets the BFF aggregation skip malformed entries
    // instead of 500-ing the whole response.
    private static bool TryGetStringProperty(JsonNode? node, string name, out string value)
    {
        value = string.Empty;
        if (node is not JsonObject obj ||
            !obj.TryGetPropertyValue(name, out var prop) ||
            prop is not JsonValue jv ||
            !jv.TryGetValue<string>(out var raw) ||
            string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }
        value = raw;
        return true;
    }

    // Owner-only access policy: a logged-in customer can only read their
    // OWN /customers/{id}/* resources. Without this gate any signed-in
    // user could enumerate other customers' data by guessing the id
    // segment of the URL.
    private static bool IsAuthorizedForCustomer(string routeCustomerId, CustomerContext customerContext)
    {
        var authenticatedId = customerContext.GetCustomerId();
        if (string.IsNullOrWhiteSpace(authenticatedId) || string.IsNullOrWhiteSpace(routeCustomerId))
        {
            return false;
        }
        return string.Equals(authenticatedId, routeCustomerId, StringComparison.OrdinalIgnoreCase);
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
