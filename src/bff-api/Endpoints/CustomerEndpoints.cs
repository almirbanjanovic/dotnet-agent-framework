using Contoso.BffApi.Services;

namespace Contoso.BffApi.Endpoints;

// /api/v1/customers/{id}                → fetch a single customer
// /api/v1/customers/{id}/orders         → fetch a customer's orders
//
// Both are simple proxies to the CRM API. Identity propagation happens
// automatically via CustomerHeaderHandler attached to CrmApiClient.

internal static class CustomerEndpoints
{
    public static (RouteHandlerBuilder customer, RouteHandlerBuilder orders) MapCustomerEndpoints(this IEndpointRouteBuilder app)
    {
        var customer = app.MapGet("/customers/{id}", GetCustomerAsync);
        var orders = app.MapGet("/customers/{id}/orders", GetOrdersAsync);
        return (customer, orders);
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

    private static async Task<IResult> ProxyResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
        return Results.Content(payload, contentType, statusCode: (int)response.StatusCode);
    }
}
