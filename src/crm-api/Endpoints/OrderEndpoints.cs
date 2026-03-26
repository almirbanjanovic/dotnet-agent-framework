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

        return group;
    }
}
