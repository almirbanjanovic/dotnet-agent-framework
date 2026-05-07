using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Contoso.BffApi.Models;
using Contoso.BffApi.Services;

namespace Contoso.BffApi.Endpoints;

// POST /api/v1/chat — buffered chat endpoint (legacy / tests).
// POST /api/v1/chat/stream — Server-Sent Events stream that pipes the
//   orchestrator's events (stage, token, tool, error) through to the
//   browser and emits a `conversation` event up-front + a `done` event
//   once the assistant message has been persisted.
//
// Flow (both):
//   1. Resolve customer ID from CustomerContext (header in dev, JWT in prod).
//   2. Load or create the conversation.
//   3. Snapshot prior turns so the orchestrator gets history, then persist
//      the new user message.
//   4. Forward to the orchestrator.
//   5. Persist the assembled assistant reply (even partial, on disconnect).

internal static class ChatEndpoint
{
    // DoS guards — assistant streams in normal use are <50 KB and <20 tool
    // calls. Caps stop a runaway agent (infinite tool loop, prompt-injection)
    // from OOMing the BFF.
    private const int MaxAssembledChars = 256 * 1024;   // 256 KB of text
    private const int MaxAssembledToolCalls = 100;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static RouteGroupBuilder MapChatEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/chat");
        group.MapPost("", HandleAsync);
        group.MapPost("/stream", HandleStreamAsync);
        return group;
    }

    private static async Task<IResult> HandleAsync(
        ChatRequest request,
        IConversationStore conversationStore,
        OrchestratorClient orchestratorClient,
        CustomerContext customerContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Contoso.BffApi.Endpoints.Chat");

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return Results.BadRequest(new { error = "message is required." });
        }

        if (ConversationLimits.ExceedsMessageLimit(request.Message))
        {
            // 413 Payload Too Large — we deliberately don't echo the
            // attempted size to keep the error generic.
            return Results.Problem(
                detail: "Message exceeds the maximum allowed size.",
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: "Payload Too Large");
        }

        var customerId = customerContext.GetCustomerId();
        if (string.IsNullOrWhiteSpace(customerId))
        {
            return Results.Unauthorized();
        }

        Conversation? conversation;
        if (string.IsNullOrWhiteSpace(request.ConversationId))
        {
            conversation = await conversationStore.CreateConversationAsync(customerId, cancellationToken);
        }
        else
        {
            conversation = await conversationStore.GetConversationAsync(request.ConversationId, cancellationToken);
            if (conversation is null ||
                !string.Equals(conversation.CustomerId, customerId, StringComparison.OrdinalIgnoreCase))
            {
                return Results.NotFound();
            }
        }

        // Snapshot prior turns BEFORE appending the new user message so the
        // orchestrator sees history as context, not duplicated alongside the
        // current turn. SelectHistoryForOrchestrator enforces both a count
        // bound and a UTF-8 byte budget so a few large messages can't blow
        // the LLM context window.
        var historyForOrchestrator = ConversationLimits
            .SelectHistoryForOrchestrator(conversation.Messages)
            .Select(m => new OrchestratorHistoryMessage(m.Role, m.Content))
            .ToArray();

        await conversationStore.AddMessageAsync(
            conversation.Id,
            new ChatMessage("user", request.Message, DateTimeOffset.UtcNow),
            cancellationToken);

        HttpResponseMessage? response = null;
        string payload;
        try
        {
            response = await orchestratorClient.SendAsync(
                customerId,
                request.Message,
                historyForOrchestrator,
                cancellationToken);
            payload = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Orchestrator call failed for customer {CustomerId}", customerId);
            response?.Dispose();
            // Return a generic message — ex.Message can carry payload fragments,
            // host names, or other internals that should not flow to a browser.
            return Results.Json(
                new
                {
                    error = ex.GetType().Name,
                    message = "The AI agent is currently unavailable. Please try again.",
                    conversationId = conversation.Id
                },
                statusCode: StatusCodes.Status502BadGateway);
        }

        try
        {
            if (!response.IsSuccessStatusCode)
            {
                // Truncate the body for the log line — the upstream payload may
                // contain user-message echoes, addresses, or other PII. Full body
                // is still emitted at Debug if the operator opts in.
                var truncated = payload is null
                    ? "(empty body)"
                    : payload.Length <= 500 ? payload : payload.Substring(0, 500) + "…";
                logger.LogWarning(
                    "Orchestrator returned {StatusCode} for customer {CustomerId}. Body (truncated): {Body}",
                    (int)response.StatusCode, customerId, truncated);
                logger.LogDebug(
                    "Full orchestrator body for {CustomerId}: {FullBody}",
                    customerId, payload);

                // Do NOT echo the upstream body to the browser — it may contain
                // internal exception details from the orchestrator.
                return Results.Json(
                    new
                    {
                        error = "OrchestratorError",
                        message = $"The AI agent returned {(int)response.StatusCode}. Please try again.",
                        conversationId = conversation.Id
                    },
                    statusCode: StatusCodes.Status502BadGateway);
            }

            AgentChatResponse? agentResponse = null;
            try
            {
                agentResponse = JsonSerializer.Deserialize<AgentChatResponse>(payload, JsonOptions);
            }
            catch (JsonException)
            {
                // Orchestrator returned 200 with non-JSON — surface the raw payload as the assistant reply.
            }

            var assistantMessage = agentResponse?.Response ?? payload ?? string.Empty;
            var toolCalls = agentResponse?.ToolCalls ?? Array.Empty<ToolCallInfo>();

            await conversationStore.AddMessageAsync(
                conversation.Id,
                new ChatMessage("assistant", assistantMessage, DateTimeOffset.UtcNow),
                cancellationToken);

            return Results.Ok(new ChatResponse(conversation.Id, assistantMessage, toolCalls));
        }
        finally
        {
            response.Dispose();
        }
    }

    private static async Task HandleStreamAsync(
        ChatRequest request,
        IConversationStore conversationStore,
        OrchestratorClient orchestratorClient,
        CustomerContext customerContext,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Contoso.BffApi.Endpoints.ChatStream");
        var response = httpContext.Response;
        response.ContentType = "text/event-stream";
        response.Headers["Cache-Control"] = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            await SseWriter.WriteAsync(response, "error", new { message = "message is required." }, cancellationToken);
            return;
        }

        if (ConversationLimits.ExceedsMessageLimit(request.Message))
        {
            response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            await SseWriter.WriteAsync(
                response,
                "error",
                new { message = "Message exceeds the maximum allowed size." },
                cancellationToken);
            return;
        }

        var customerId = customerContext.GetCustomerId();
        if (string.IsNullOrWhiteSpace(customerId))
        {
            await SseWriter.WriteAsync(response, "error", new { message = "Unauthorized." }, cancellationToken);
            return;
        }

        Conversation? conversation;
        if (string.IsNullOrWhiteSpace(request.ConversationId))
        {
            conversation = await conversationStore.CreateConversationAsync(customerId, cancellationToken);
        }
        else
        {
            conversation = await conversationStore.GetConversationAsync(request.ConversationId, cancellationToken);
            if (conversation is null ||
                !string.Equals(conversation.CustomerId, customerId, StringComparison.OrdinalIgnoreCase))
            {
                await SseWriter.WriteAsync(response, "error", new { message = "Conversation not found." }, cancellationToken);
                return;
            }
        }

        var historyForOrchestrator = ConversationLimits
            .SelectHistoryForOrchestrator(conversation.Messages)
            .Select(m => new OrchestratorHistoryMessage(m.Role, m.Content))
            .ToArray();

        await conversationStore.AddMessageAsync(
            conversation.Id,
            new ChatMessage("user", request.Message, DateTimeOffset.UtcNow),
            cancellationToken);

        // Tell the client which conversation this stream belongs to BEFORE
        // contacting the orchestrator so a slow agent doesn't delay the
        // first byte the browser sees.
        await SseWriter.WriteAsync(response, "conversation", new { conversationId = conversation.Id }, cancellationToken);

        HttpResponseMessage? upstream = null;
        var assembled = new StringBuilder();
        var assembledToolCalls = new List<ToolCallInfo>();
        var assistantPersisted = false;

        try
        {
            upstream = await orchestratorClient.StreamAsync(
                customerId,
                request.Message,
                historyForOrchestrator,
                cancellationToken);

            if (!upstream.IsSuccessStatusCode)
            {
                var body = await upstream.Content.ReadAsStringAsync(cancellationToken);
                // Truncate the upstream body for the warning log — same
                // PII risk as the buffered branch (see HandleAsync above).
                var truncated = body is null
                    ? "(empty body)"
                    : body.Length <= 500 ? body : body.Substring(0, 500) + "…";
                logger.LogWarning(
                    "Orchestrator returned {StatusCode} for customer {CustomerId}. Body (truncated): {Body}",
                    (int)upstream.StatusCode, customerId, truncated);
                Activity.Current?.SetStatus(ActivityStatusCode.Error, $"Orchestrator {(int)upstream.StatusCode}");
                // Do NOT echo the upstream body to the browser — same leak
                // class as the buffered /chat path. Status code is enough.
                await SseWriter.WriteAsync(
                    response,
                    "error",
                    new { message = $"The AI agent returned {(int)upstream.StatusCode}. Please try again." },
                    cancellationToken);
                return;
            }

            await using var upstreamStream = await upstream.Content.ReadAsStreamAsync(cancellationToken);

            // Bound the entire upstream consumption: a stuck or hostile
            // orchestrator must not be able to hold a connection open
            // forever or stream unbounded bytes through us. Linked to
            // cancellationToken so client disconnects still short-circuit.
            using var streamTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            streamTimeoutCts.CancelAfter(TimeSpan.FromMinutes(5));
            var streamCt = streamTimeoutCts.Token;

            // Per-event payload cap — an event larger than this is almost
            // always a runaway model; we treat it as a stream-fatal error.
            const int MaxEventDataBytes = 256 * 1024;
            // Per-stream total cap — defends against a slow drip of legit-
            // sized events that collectively exhaust memory or bandwidth.
            const long MaxStreamTotalBytes = 16L * 1024 * 1024;

            // Wrap the upstream stream so even StreamReader.ReadLineAsync
            // (which has no built-in per-line cap) cannot allocate past
            // MaxStreamTotalBytes worth of buffer for a single hostile
            // line without a newline.
            await using var boundedStream = new BoundedReadStream(upstreamStream, MaxStreamTotalBytes);
            using var reader = new StreamReader(boundedStream);

            long totalStreamBytes = 0;

            // SSE state machine (WHATWG-compliant subset):
            //   - Accumulate `data:` lines per event block, joined by '\n'.
            //   - Default event name is "message" if no `event:` line was seen.
            //   - Lines starting with ':' are comments — ignored.
            //   - Blank line dispatches the accumulated event.
            //   - Each line is forwarded to the client verbatim (preserving SSE
            //     framing) EXCEPT lines belonging to a `done` event — BFF
            //     emits its own `done` after persistence is durable.
            string? eventName = null;
            var dataBuffer = new StringBuilder();
            var blockLines = new List<string>(8);

            string? line;
            while ((line = await reader.ReadLineAsync(streamCt)) is not null)
            {
                // Account for the line plus the implicit newline. Char count
                // is a UTF-8-pessimistic upper bound (1 char ≥ 1 byte).
                totalStreamBytes += line.Length + 1;
                if (totalStreamBytes > MaxStreamTotalBytes)
                {
                    logger.LogWarning(
                        "Orchestrator stream exceeded {Cap} bytes for customer {CustomerId}; aborting.",
                        MaxStreamTotalBytes, customerId);
                    await SseWriter.WriteAsync(
                        response,
                        "error",
                        new { message = "Upstream response exceeded the maximum allowed size." },
                        cancellationToken);
                    return;
                }

                if (line.Length == 0)
                {
                    // Dispatch the buffered event block.
                    await DispatchBlockAsync(
                        eventName,
                        dataBuffer,
                        blockLines,
                        response,
                        assembled,
                        assembledToolCalls,
                        cancellationToken);
                    eventName = null;
                    dataBuffer.Clear();
                    blockLines.Clear();
                    continue;
                }

                blockLines.Add(line);

                if (line.StartsWith(":", StringComparison.Ordinal))
                {
                    // SSE comment — ignored.
                    continue;
                }

                var colonIdx = line.IndexOf(':');
                string field, value;
                if (colonIdx < 0)
                {
                    field = line;
                    value = string.Empty;
                }
                else
                {
                    field = line.Substring(0, colonIdx);
                    value = line.Substring(colonIdx + 1);
                    if (value.StartsWith(" ", StringComparison.Ordinal))
                    {
                        value = value.Substring(1);
                    }
                }

                switch (field)
                {
                    case "event":
                        eventName = value;
                        break;
                    case "data":
                        if (dataBuffer.Length > 0) dataBuffer.Append('\n');
                        dataBuffer.Append(value);
                        if (dataBuffer.Length > MaxEventDataBytes)
                        {
                            logger.LogWarning(
                                "Orchestrator emitted SSE event > {Cap} bytes for customer {CustomerId}; aborting.",
                                MaxEventDataBytes, customerId);
                            await SseWriter.WriteAsync(
                                response,
                                "error",
                                new { message = "Upstream emitted an event larger than the allowed size." },
                                cancellationToken);
                            return;
                        }
                        break;
                    // id / retry are ignored — we don't support reconnect IDs.
                }
            }

            // Final block without trailing blank line.
            if (eventName is not null || dataBuffer.Length > 0)
            {
                await DispatchBlockAsync(
                    eventName,
                    dataBuffer,
                    blockLines,
                    response,
                    assembled,
                    assembledToolCalls,
                    cancellationToken);
            }

            // Set the flag BEFORE awaiting the persist so a partial-success +
            // exception in PersistAssistantAsync (e.g. store flushes the write
            // but throws on completion) does not let the catch block double-
            // write the same message.
            assistantPersisted = true;
            try
            {
                await PersistAssistantAsync(conversationStore, conversation.Id, assembled, cancellationToken);
            }
            catch
            {
                assistantPersisted = false;
                throw;
            }

            await SseWriter.WriteAsync(
                response,
                "done",
                new { conversationId = conversation.Id, toolCalls = assembledToolCalls },
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Client disconnected. Still persist whatever we managed to
            // accumulate so the conversation history doesn't end with an
            // orphan user turn.
            if (!assistantPersisted && assembled.Length > 0)
            {
                try
                {
                    await PersistAssistantAsync(conversationStore, conversation.Id, assembled, CancellationToken.None);
                }
                catch (Exception persistEx)
                {
                    logger.LogWarning(persistEx, "Failed to persist partial assistant message after client disconnect.");
                }
            }
        }
        catch (Exception ex)
        {
            // Use the exception type name (not message) for the span status —
            // ex.Message can contain payload fragments / PII that would then
            // be exported to APM and visible to anyone with trace-read access.
            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            logger.LogError(ex, "Orchestrator stream failed for customer {CustomerId}", customerId);

            // Same as above — preserve any partial reply before reporting.
            if (!assistantPersisted && assembled.Length > 0)
            {
                try
                {
                    await PersistAssistantAsync(conversationStore, conversation.Id, assembled, CancellationToken.None);
                }
                catch (Exception persistEx)
                {
                    logger.LogWarning(persistEx, "Failed to persist partial assistant message during error recovery.");
                }
            }

            await SseWriter.WriteAsync(
                response,
                "error",
                new { message = "The AI agent is currently unavailable. Please try again.", type = ex.GetType().Name },
                CancellationToken.None);
        }
        finally
        {
            upstream?.Dispose();
        }
    }

    // Dispatches one SSE event block: forward the original lines to the
    // client (unless it's a `done` block, which BFF synthesises itself
    // after persistence) and accumulate the data into `assembled` /
    // `assembledToolCalls` if it's a `token` / `tool` event.
    private static async Task DispatchBlockAsync(
        string? eventName,
        StringBuilder dataBuffer,
        List<string> blockLines,
        HttpResponse response,
        StringBuilder assembled,
        List<ToolCallInfo> assembledToolCalls,
        CancellationToken cancellationToken)
    {
        var resolvedEvent = eventName ?? "message";

        if (resolvedEvent != "done")
        {
            foreach (var raw in blockLines)
            {
                await response.WriteAsync(raw + "\n", cancellationToken);
            }
            await response.WriteAsync("\n", cancellationToken);
            await response.Body.FlushAsync(cancellationToken);
        }

        if (dataBuffer.Length == 0) return;
        var data = dataBuffer.ToString();

        switch (resolvedEvent)
        {
            case "token":
                try
                {
                    var token = JsonSerializer.Deserialize<TokenEvent>(data, JsonOptions);
                    if (!string.IsNullOrEmpty(token?.Text) && assembled.Length < MaxAssembledChars)
                    {
                        var remaining = MaxAssembledChars - assembled.Length;
                        var slice = token.Text.Length <= remaining
                            ? token.Text
                            : token.Text.Substring(0, remaining);
                        assembled.Append(slice);
                    }
                }
                catch (JsonException) { /* malformed token — skip */ }
                break;

            case "tool":
                try
                {
                    var tool = JsonSerializer.Deserialize<ToolEvent>(data, JsonOptions);
                    if (tool?.Name is { Length: > 0 } name &&
                        assembledToolCalls.Count < MaxAssembledToolCalls)
                    {
                        assembledToolCalls.Add(new ToolCallInfo(
                            name,
                            tool.Arguments ?? new Dictionary<string, object?>()));
                    }
                }
                catch (JsonException) { /* malformed tool — skip */ }
                break;
        }
    }

    private static Task PersistAssistantAsync(
        IConversationStore conversationStore,
        string conversationId,
        StringBuilder assembled,
        CancellationToken cancellationToken)
    {
        // Don't write empty assistant rows — they pollute history with
        // half-turns when the orchestrator returned no tokens (e.g. an
        // upstream 4xx that emitted only an error event).
        var content = assembled.ToString();
        if (string.IsNullOrEmpty(content)) return Task.CompletedTask;

        return conversationStore.AddMessageAsync(
            conversationId,
            new ChatMessage("assistant", content, DateTimeOffset.UtcNow),
            cancellationToken);
    }

    private sealed record TokenEvent(string? Text);
    private sealed record ToolEvent(string? Name, Dictionary<string, object?>? Arguments);
}
