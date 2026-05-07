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

        // Anonymous (guest) sessions never reach CRM — they have no
        // customer record to look up, no orders, no profile. Skip the
        // classification round-trip and force PRODUCT directly. The
        // Product Agent is told it's a guest so it withholds CRM tools
        // and asks the visitor to sign in for account questions; CRM
        // Agent independently 403s guest ids as a defense-in-depth.
        var intent = GuestId.IsGuest(request.CustomerId)
            ? "PRODUCT"
            : await classifier.ClassifyAsync(request.Message, cancellationToken);
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

            // Anonymous guests bypass the LLM classifier and go straight
            // to PRODUCT. (See the buffered branch above for the full
            // rationale.) The "classifying" stage event is still emitted
            // so the UI shows the same skeleton; the immediate "routed"
            // event tells the user what happened.
            var intent = GuestId.IsGuest(request.CustomerId)
                ? "PRODUCT"
                : await classifier.ClassifyAsync(request.Message, cancellationToken);
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

            // Bound the entire upstream consumption: a stuck or hostile
            // specialist must not be able to hold a connection open
            // forever or stream unbounded bytes through us. Linked to
            // cancellationToken so client disconnects still short-circuit.
            using var streamTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            streamTimeoutCts.CancelAfter(TimeSpan.FromMinutes(5));
            var streamCt = streamTimeoutCts.Token;

            // Manual copy loop with a per-stream byte cap. CopyToAsync
            // would happily buffer 16 MiB+ if the upstream agent went
            // rogue; this fails fast with a logged warning instead.
            const long MaxStreamTotalBytes = 16L * 1024 * 1024;
            var copyBuffer = new byte[16 * 1024];
            long totalStreamBytes = 0;
            int bytesRead;
            while ((bytesRead = await upstreamStream.ReadAsync(copyBuffer.AsMemory(0, copyBuffer.Length), streamCt)) > 0)
            {
                totalStreamBytes += bytesRead;
                if (totalStreamBytes > MaxStreamTotalBytes)
                {
                    logger.LogWarning(
                        "Specialist agent {Agent} stream exceeded {Cap} bytes for customer {CustomerId}; aborting.",
                        agentLabel, MaxStreamTotalBytes, request.CustomerId);
                    await SseWriter.WriteAsync(
                        response,
                        "error",
                        new { message = "Specialist agent emitted a response larger than the allowed size.", agent = agentLabel },
                        cancellationToken);
                    return;
                }
                await response.Body.WriteAsync(copyBuffer.AsMemory(0, bytesRead), cancellationToken);
                // Flush after each chunk so the BFF (and downstream
                // browser) sees tokens AS THEY ARRIVE rather than after
                // Kestrel's internal buffer fills. Without this, manual
                // copy regresses the streaming UX vs the original
                // CopyToAsync (which had its own implicit buffering but
                // tighter coupling to the source flush cadence).
                await response.Body.FlushAsync(cancellationToken);
            }
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
