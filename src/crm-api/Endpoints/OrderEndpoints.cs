using Contoso.CrmApi.Models;
using Contoso.CrmApi.Services;

namespace Contoso.CrmApi.Endpoints;

public static class OrderEndpoints
{
    public static RouteGroupBuilder MapOrderEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1")
            .WithTags("Orders");

        group.MapGet("/orders/{id}", async (
            string id,
            CustomerContext customerContext,
            ICosmosService cosmos,
            CancellationToken ct) =>
        {
            var order = await cosmos.GetOrderByIdAsync(id, ct);
            if (order is null)
            {
                return Results.Problem(
                    detail: $"Order with ID '{id}' was not found.",
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Not Found");
            }

            // Defense-in-depth: when the BFF forwards X-Customer-Entra-Id,
            // refuse to read another customer's order. 404 (not 403) so
            // we don't leak the existence of order IDs that belong to
            // someone else — important because order IDs are sequential
            // and easily enumerable.
            if (!CustomerEndpoints.IsAuthorizedFor(customerContext, order.CustomerId))
            {
                return Results.Problem(
                    detail: $"Order with ID '{id}' was not found.",
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Not Found");
            }

            return Results.Ok(order);
        })
        .WithName("GetOrderById")
        .WithSummary("Get order by ID (cross-partition query)");

        group.MapGet("/customers/{id}/orders", async (
            string id,
            CustomerContext customerContext,
            ICosmosService cosmos,
            CancellationToken ct) =>
        {
            // Defense-in-depth: when the BFF forwards X-Customer-Entra-Id,
            // refuse to read another customer's orders. 404 (not 403) so
            // we don't leak the existence of other customer IDs.
            if (!CustomerEndpoints.IsAuthorizedFor(customerContext, id))
            {
                return Results.Ok(Array.Empty<Order>());
            }

            var orders = await cosmos.GetOrdersByCustomerIdAsync(id, ct);
            return Results.Ok(orders);
        })
        .WithName("GetCustomerOrders")
        .WithSummary("Get all orders for a customer");

        group.MapGet("/orders/{id}/items", async (
            string id,
            CustomerContext customerContext,
            ICosmosService cosmos,
            CancellationToken ct) =>
        {
            // Defense-in-depth: load the parent order to find its owner,
            // then refuse to leak items from another customer's order.
            // 404 (not 403) so we don't leak the existence of order IDs.
            var order = await cosmos.GetOrderByIdAsync(id, ct);
            if (order is null
                || !CustomerEndpoints.IsAuthorizedFor(customerContext, order.CustomerId))
            {
                return Results.Problem(
                    detail: $"Order with ID '{id}' was not found.",
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Not Found");
            }

            var items = await cosmos.GetOrderItemsByOrderIdAsync(id, ct);
            return Results.Ok(items);
        })
        .WithName("GetOrderItems")
        .WithSummary("Get line items for an order");

        // Place order. Customer identity is taken from the CustomerContext
        // (X-Customer-Entra-Id header set by the BFF after JWT validation),
        // never from the request body — clients can only place orders for
        // themselves.
        group.MapPost("/orders", async (
            CreateOrderRequest request,
            CustomerContext customerContext,
            ICosmosService cosmos,
            CancellationToken ct) =>
        {
            var customerId = customerContext.GetCustomerEntraId();
            if (string.IsNullOrWhiteSpace(customerId))
            {
                return Results.Problem(
                    detail: "No customer identity resolved from the request.",
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Unauthorized");
            }

            if (request.Items is null || request.Items.Count == 0)
            {
                return Results.Problem(
                    detail: "Order must contain at least one item.",
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Bad Request");
            }

            // Bound the order so a malformed or hostile request cannot
            // wedge Cosmos write throughput. 100 line items is well above
            // any realistic outdoor-gear basket; a request beyond that is
            // either a bug or an attack.
            const int MaxOrderLineItems = 100;
            if (request.Items.Count > MaxOrderLineItems)
            {
                return Results.Problem(
                    detail: $"Order may contain at most {MaxOrderLineItems} line items.",
                    statusCode: StatusCodes.Status413PayloadTooLarge,
                    title: "Payload Too Large");
            }

            // shipping_address is free-form text; a multi-MB string would
            // also blow the Cosmos doc limit. 500 chars is roughly 7 lines
            // of address, far above any real address.
            const int MaxShippingAddressLength = 500;
            if (!string.IsNullOrEmpty(request.ShippingAddress)
                && request.ShippingAddress.Length > MaxShippingAddressLength)
            {
                return Results.Problem(
                    detail: $"shipping_address exceeds the maximum {MaxShippingAddressLength} characters.",
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Bad Request");
            }

            // Per-item bounds: a non-positive or absurd quantity is a
            // bug or an attack; reject before we waste a Cosmos write.
            const int MaxQuantityPerItem = 1000;
            foreach (var item in request.Items)
            {
                if (string.IsNullOrWhiteSpace(item.ProductId))
                {
                    return Results.Problem(
                        detail: "Each order item must include a product_id.",
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Bad Request");
                }
                if (item.Quantity <= 0 || item.Quantity > MaxQuantityPerItem)
                {
                    return Results.Problem(
                        detail: $"Item quantity must be between 1 and {MaxQuantityPerItem}.",
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Bad Request");
                }
            }

            try
            {
                var (order, items) = await cosmos.CreateOrderAsync(
                    customerId,
                    request.ShippingAddress,
                    request.Items.Select(i => (i.ProductId, i.Quantity)),
                    ct);

                return Results.Created($"/api/v1/orders/{order.Id}", new
                {
                    order,
                    items
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Bad Request");
            }
        })
        .WithName("PlaceOrder")
        .WithSummary("Place a new order for the authenticated customer");

        return group;
    }
}
