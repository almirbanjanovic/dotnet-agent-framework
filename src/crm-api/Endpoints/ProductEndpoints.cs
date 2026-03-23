using Contoso.CrmApi.Services;

namespace Contoso.CrmApi.Endpoints;

public static class ProductEndpoints
{
    public static RouteGroupBuilder MapProductEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/products")
            .WithTags("Products");

        group.MapGet("/", async (
            string? query,
            string? category,
            bool? in_stock_only,
            ICosmosService cosmos,
            CancellationToken ct) =>
        {
            var products = await cosmos.GetProductsAsync(query, category, in_stock_only, ct);
            return Results.Ok(products);
        })
        .WithName("GetProducts")
        .WithSummary("Search or list products with optional filters");

        group.MapGet("/{id}", async (string id, ICosmosService cosmos, CancellationToken ct) =>
        {
            var product = await cosmos.GetProductByIdAsync(id, ct);
            return product is null
                ? Results.Problem(
                    detail: $"Product with ID '{id}' was not found.",
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Not Found")
                : Results.Ok(product);
        })
        .WithName("GetProductById")
        .WithSummary("Get product by ID");

        return group;
    }
}
