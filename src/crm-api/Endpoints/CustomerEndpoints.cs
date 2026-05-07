using Contoso.CrmApi.Services;

namespace Contoso.CrmApi.Endpoints;

public static class CustomerEndpoints
{
    public static RouteGroupBuilder MapCustomerEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/customers")
            .WithTags("Customers");

        // NOTE: a `GET /api/v1/customers` (list-all) endpoint used to live
        // here. It was removed because nothing in the public surface
        // legitimately needs to enumerate customers — the MCP
        // `get_all_customers` tool that was its only consumer has also
        // been removed (see src/crm-mcp/Tools/CustomerTools.cs). If a
        // back-office surface ever needs bulk listing, build it as a new
        // endpoint behind real authorization rather than re-exposing this
        // path (which any in-cluster caller could hit anonymously).

        group.MapGet("/{id}", async (
            string id,
            CustomerContext customerContext,
            ICosmosService cosmos,
            CancellationToken ct) =>
        {
            // Defense-in-depth: if the BFF forwarded a customer identity
            // via X-Customer-Entra-Id, only allow reads of that customer's
            // own record. A 404 (rather than 403) is intentional — it
            // matches the BFF's behavior and avoids leaking the existence
            // of other customer IDs. When the header is absent (legacy
            // callers, seed-data, tests), we fall back to the original
            // unauthenticated behavior.
            if (!IsAuthorizedFor(customerContext, id))
            {
                return Results.Problem(
                    detail: $"Customer with ID '{id}' was not found.",
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Not Found");
            }

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

    /// <summary>
    /// Returns true when the request either carries no customer identity
    /// (legacy path) or carries an identity that matches the route's
    /// customer ID. Used as a defense-in-depth check on read endpoints
    /// that take a customer ID in the path.
    /// </summary>
    internal static bool IsAuthorizedFor(CustomerContext context, string routeCustomerId)
    {
        var caller = context.GetCustomerEntraId();
        if (string.IsNullOrWhiteSpace(caller))
        {
            return true;
        }

        return string.Equals(caller, routeCustomerId, StringComparison.OrdinalIgnoreCase);
    }
}
