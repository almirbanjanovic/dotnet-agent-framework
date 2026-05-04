using Contoso.BffApi.Services;

namespace Contoso.BffApi.Endpoints;

// /api/v1/conversations              → list current customer's conversations
// /api/v1/conversations/{id}         → fetch one (with ownership check)

internal static class ConversationEndpoints
{
    public static (RouteHandlerBuilder list, RouteHandlerBuilder details) MapConversationEndpoints(this IEndpointRouteBuilder app)
    {
        var list = app.MapGet("/conversations", ListAsync);
        var details = app.MapGet("/conversations/{id}", GetAsync);
        return (list, details);
    }

    private static async Task<IResult> ListAsync(
        IConversationStore conversationStore,
        CustomerContext customerContext,
        CancellationToken cancellationToken)
    {
        var customerId = customerContext.GetCustomerId();
        if (string.IsNullOrWhiteSpace(customerId))
        {
            return Results.Unauthorized();
        }

        var conversations = await conversationStore.GetConversationsByCustomerAsync(customerId, cancellationToken);
        return Results.Ok(conversations);
    }

    private static async Task<IResult> GetAsync(
        string id,
        IConversationStore conversationStore,
        CustomerContext customerContext,
        CancellationToken cancellationToken)
    {
        var customerId = customerContext.GetCustomerId();
        if (string.IsNullOrWhiteSpace(customerId))
        {
            return Results.Unauthorized();
        }

        var conversation = await conversationStore.GetConversationAsync(id, cancellationToken);
        if (conversation is null ||
            !string.Equals(conversation.CustomerId, customerId, StringComparison.OrdinalIgnoreCase))
        {
            return Results.NotFound();
        }

        return Results.Ok(conversation);
    }
}
