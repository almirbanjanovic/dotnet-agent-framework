using Contoso.OrchestratorAgent.Models;
using Contoso.OrchestratorAgent.Services;

namespace Contoso.OrchestratorAgent.Endpoints;

// Single endpoint of the orchestrator. Two-step pipeline:
//   1. IntentClassifier asks the Foundry chat model to label the message
//      as CRM or PRODUCT.
//   2. AgentRouter forwards the request (with history) to that specialist
//      and proxies the response back unchanged.

internal static class ChatEndpoint
{
    public static IEndpointRouteBuilder MapChatEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/chat", HandleAsync);
        return app;
    }

    private static async Task<IResult> HandleAsync(
        ChatRequest request,
        IntentClassifier classifier,
        AgentRouter router,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerId) || string.IsNullOrWhiteSpace(request.Message))
        {
            return Results.BadRequest(new { error = "customerId and message are required." });
        }

        var intent = await classifier.ClassifyAsync(request.Message, cancellationToken);
        var result = await router.RouteAsync(intent, request, cancellationToken);

        if (string.IsNullOrWhiteSpace(result.Payload))
        {
            return Results.StatusCode(result.StatusCode);
        }

        return Results.Content(result.Payload, "application/json", statusCode: result.StatusCode);
    }
}
