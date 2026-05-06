using Contoso.OrchestratorAgent.Models;
using Contoso.OrchestratorAgent.Services;

namespace Contoso.OrchestratorAgent.Endpoints;

// Single endpoint of the orchestrator. Two-step pipeline:
//   1. IntentClassifier asks the Foundry chat model to label the message
//      as CRM or PRODUCT.
//   2. AgentRouter forwards the request (with history) to that specialist
//      and proxies the response back unchanged.
//
// Two flavors:
//   POST /api/v1/chat        — buffered JSON response (legacy / tests)
//   POST /api/v1/chat/stream — Server-Sent Events. Emits a `stage` event
//                              when classification finishes (so the UI can
//                              show "routed to crm-agent") and then
//                              proxies the specialist's SSE stream
//                              (token / tool / done events) verbatim.

internal static class ChatEndpoint
{
    public static IEndpointRouteBuilder MapChatEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/chat", HandleAsync);
        app.MapPost("/api/v1/chat/stream", HandleStreamAsync);
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

    private static async Task HandleStreamAsync(
        ChatRequest request,
        IntentClassifier classifier,
        AgentRouter router,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Contoso.OrchestratorAgent.Endpoints.ChatStream");
        var response = httpContext.Response;
        response.ContentType = "text/event-stream";
        response.Headers["Cache-Control"] = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";

        if (string.IsNullOrWhiteSpace(request.CustomerId) || string.IsNullOrWhiteSpace(request.Message))
        {
            await SseWriter.WriteAsync(response, "error", new { message = "customerId and message are required." }, cancellationToken);
            return;
        }

        try
        {
            await SseWriter.WriteAsync(response, "stage", new { stage = "classifying" }, cancellationToken);

            var intent = await classifier.ClassifyAsync(request.Message, cancellationToken);
            var agentLabel = intent.Equals("PRODUCT", StringComparison.OrdinalIgnoreCase) ? "product" : "crm";

            await SseWriter.WriteAsync(response, "stage", new { stage = "routed", agent = agentLabel }, cancellationToken);

            using var upstream = await router.RouteStreamAsync(intent, request, cancellationToken);
            if (!upstream.IsSuccessStatusCode)
            {
                // Read the upstream body so operators can diagnose, but do NOT
                // proxy it to the BFF / browser — it may contain a JSON error
                // doc with internals, stack frames, or echoes of payload data.
                var body = await upstream.Content.ReadAsStringAsync(cancellationToken);
                var truncated = body is null
                    ? "(empty body)"
                    : body.Length <= 500 ? body : body.Substring(0, 500) + "…";
                logger.LogWarning(
                    "Specialist agent {Agent} returned {StatusCode}. Body (truncated): {Body}",
                    agentLabel, (int)upstream.StatusCode, truncated);
                await SseWriter.WriteAsync(
                    response,
                    "error",
                    new { message = $"Specialist agent returned {(int)upstream.StatusCode}.", agent = agentLabel },
                    cancellationToken);
                return;
            }

            // Pipe the specialist's SSE bytes straight through. Each SSE
            // event is already self-delimited so no parsing is required.
            await using var upstreamStream = await upstream.Content.ReadAsStreamAsync(cancellationToken);
            await upstreamStream.CopyToAsync(response.Body, cancellationToken);
            await response.Body.FlushAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Client disconnected.
        }
        catch (Exception ex)
        {
            // Log full exception for operators — client only sees a sanitized
            // SSE error event below. ex.Message may include payload fragments,
            // file paths, or other internals.
            logger.LogError(ex, "Orchestrator stream failed for customer {CustomerId}", request.CustomerId);
            await SseWriter.WriteAsync(
                response,
                "error",
                new { message = "Specialist agent stream failed.", type = ex.GetType().Name },
                CancellationToken.None);
        }
    }
}
