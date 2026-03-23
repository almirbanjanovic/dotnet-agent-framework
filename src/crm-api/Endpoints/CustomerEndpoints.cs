using Contoso.CrmApi.Services;

namespace Contoso.CrmApi.Endpoints;

public static class CustomerEndpoints
{
    public static RouteGroupBuilder MapCustomerEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/customers")
            .WithTags("Customers");

        group.MapGet("/", async (ICosmosService cosmos, CancellationToken ct) =>
        {
            var customers = await cosmos.GetAllCustomersAsync(ct);
            return Results.Ok(customers);
        })
        .WithName("GetAllCustomers")
        .WithSummary("List all customers");

        group.MapGet("/{id}", async (string id, ICosmosService cosmos, CancellationToken ct) =>
        {
            var customer = await cosmos.GetCustomerByIdAsync(id, ct);
            return customer is null
                ? Results.Problem(
                    detail: $"Customer with ID '{id}' was not found.",
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Not Found")
                : Results.Ok(customer);
        })
        .WithName("GetCustomerById")
        .WithSummary("Get customer by ID");

        return group;
    }
}
