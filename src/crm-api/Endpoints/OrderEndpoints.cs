using Contoso.CrmApi.Models;
using Contoso.CrmApi.Services;

namespace Contoso.CrmApi.Endpoints;

public static class OrderEndpoints
{
    public static RouteGroupBuilder MapOrderEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1")
            .WithTags("Orders");

        group.MapGet("/orders/{id}", async (string id, ICosmosService cosmos, CancellationToken ct) =>
        {
            var order = await cosmos.GetOrderByIdAsync(id, ct);
            return order is null
                ? Results.Problem(
                    detail: $"Order with ID '{id}' was not found.",
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Not Found")
                : Results.Ok(order);
        })
        .WithName("GetOrderById")
        .WithSummary("Get order by ID (cross-partition query)");

        group.MapGet("/customers/{id}/orders", async (string id, ICosmosService cosmos, CancellationToken ct) =>
        {
            var orders = await cosmos.GetOrdersByCustomerIdAsync(id, ct);
            return Results.Ok(orders);
        })
        .WithName("GetCustomerOrders")
        .WithSummary("Get all orders for a customer");

        group.MapGet("/orders/{id}/items", async (string id, ICosmosService cosmos, CancellationToken ct) =>
        {
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
