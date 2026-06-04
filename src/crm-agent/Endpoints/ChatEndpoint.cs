using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.IO;

namespace Contoso.CrmAgent.Endpoints;

// Single endpoint of the CRM Agent. Receives a chat turn from the
// Orchestrator (CustomerId + message + prior history), discovers MCP
// tools from both backends, runs the agent, and returns the answer
// plus the list of tools the agent invoked.
//
// Two flavors:
//   POST /api/v1/chat        — buffered JSON response (legacy / tests)
//   POST /api/v1/chat/stream — Server-Sent Events: token deltas + tool
//                              calls as they happen, ending with a `done`
//                              event. Industry-standard chat UX.

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
        CrmAgentFactory agentFactory,
        SystemPromptProvider promptProvider,
        CrmMcpClientProvider crmProvider,
        KnowledgeMcpClientProvider knowledgeProvider,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerId) || string.IsNullOrWhiteSpace(request.Message))
        {
            return Results.BadRequest(new { error = "customerId and message are required." });
        }

        // Defense in depth: the orchestrator should never route an
        // anonymous guest to CRM, but if it does we hard-refuse rather
        // than discover tools and let the LLM start poking at customer
        // records that don't exist.
        if (GuestId.IsGuest(request.CustomerId))
        {
            return Results.Json(
                new { error = "AnonymousNotSupported", message = "Sign-in required for account questions." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        // Discover tools from both MCP backends per request. They're cheap
        // to enumerate and the agent's tool set may evolve at runtime.
        var tools = new List<AITool>();
        tools.AddRange(await crmProvider.ExecuteWithClientRetryAsync(
            static (client, ct) => client.ListToolsAsync(cancellationToken: ct),
            cancellationToken));
        tools.AddRange(await knowledgeProvider.ExecuteWithClientRetryAsync(
            static (client, ct) => client.ListToolsAsync(cancellationToken: ct),
            cancellationToken));

        var agent = agentFactory.CreateAgent(promptProvider.Prompt, tools);
        var messages = ChatHistoryBinder.Build(request.History, request.CustomerId, request.Message);
        var response = await agent.RunAsync(messages, cancellationToken: cancellationToken);

        var toolCalls = ToolCallExtractor.Extract(response);

        return Results.Ok(new ChatResponse(response.ToString(), toolCalls));
    }

    private static async Task HandleStreamAsync(
        ChatRequest request,
        CrmAgentFactory agentFactory,
        SystemPromptProvider promptProvider,
        CrmMcpClientProvider crmProvider,
        KnowledgeMcpClientProvider knowledgeProvider,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Contoso.CrmAgent.Endpoints.ChatStream");
        var response = httpContext.Response;
        response.ContentType = "text/event-stream";
        response.Headers["Cache-Control"] = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";

        if (string.IsNullOrWhiteSpace(request.CustomerId) || string.IsNullOrWhiteSpace(request.Message))
        {
            await SseWriter.WriteAsync(response, "error", new { message = "customerId and message are required." }, cancellationToken);
            return;
        }

        // Defense in depth (see HandleAsync above). For the streaming path
        // we also set the HTTP status so the orchestrator's stream proxy
        // surfaces a real 403 instead of a successful empty SSE stream.
        if (GuestId.IsGuest(request.CustomerId))
        {
            response.StatusCode = StatusCodes.Status403Forbidden;
            await SseWriter.WriteAsync(
                response,
                "error",
                new { message = "Sign-in required for account questions." },
                cancellationToken);
            return;
        }

        try
        {
            var tools = new List<AITool>();
            tools.AddRange(await crmProvider.ExecuteWithClientRetryAsync(
                static (client, ct) => client.ListToolsAsync(cancellationToken: ct),
                cancellationToken));
            tools.AddRange(await knowledgeProvider.ExecuteWithClientRetryAsync(
                static (client, ct) => client.ListToolsAsync(cancellationToken: ct),
                cancellationToken));

            var agent = agentFactory.CreateAgent(promptProvider.Prompt, tools);
            var messages = ChatHistoryBinder.Build(request.History, request.CustomerId, request.Message);

            // Track which tool-call IDs have already been emitted so we
            // never duplicate a `tool` event when multiple chunks share the
            // same FunctionCallContent. Same idea for tool_result — the
            // SDK can stream a result alongside subsequent text chunks.
            // emittedToolCallNames keeps callId→name so we can label the
            // tool_result event even when SDK delivers result/call in
            // separate enumerator iterations.
            // pendingCallKeys queues call-keys whose results have not yet
            // arrived, so a result with a null CallId still pairs back to
            // the most recent unmatched call (instead of generating an
            // un-pairable random GUID and orphaning the result row in the UI).
            var emittedToolCalls = new HashSet<string>(StringComparer.Ordinal);
            var emittedToolCallNames = new Dictionary<string, string>(StringComparer.Ordinal);
            var emittedToolResults = new HashSet<string>(StringComparer.Ordinal);
            var pendingCallKeys = new Queue<string>();

            await foreach (var update in agent.RunStreamingAsync(messages, cancellationToken: cancellationToken))
            {
                if (!string.IsNullOrEmpty(update.Text))
                {
                    await SseWriter.WriteAsync(response, "token", new { text = update.Text }, cancellationToken);
                }

                foreach (var content in update.Contents)
                {
                    if (content is FunctionCallContent functionCall)
                    {
                        var callKey = functionCall.CallId ?? functionCall.Name;
                        if (emittedToolCalls.Add(callKey))
                        {
                            emittedToolCallNames[callKey] = functionCall.Name;
                            pendingCallKeys.Enqueue(callKey);
                            var args = functionCall.Arguments ?? new Dictionary<string, object?>();
                            await SseWriter.WriteAsync(
                                response,
                                "tool",
                                new { name = functionCall.Name, callId = functionCall.CallId, arguments = args },
                                cancellationToken);
                        }
                    }
                    else if (content is FunctionResultContent functionResult)
                    {
                        // Prefer the explicit CallId when present. Fall back
                        // to the oldest pending call so the UI can still
                        // pair the result with its `tool` row in order.
                        var callKey = functionResult.CallId
                            ?? (pendingCallKeys.Count > 0 ? pendingCallKeys.Dequeue() : Guid.NewGuid().ToString("N"));
                        if (emittedToolResults.Add(callKey))
                        {
                            var (preview, truncated) = ToolResultPreview.Render(functionResult.Result);
                            var name = emittedToolCallNames.TryGetValue(callKey, out var n) ? n : "tool";
                            // Echo back whichever id the UI has — the explicit one
                            // if present, otherwise the matched fallback key (also
                            // what the original `tool` event used as callId
                            // surrogate when CallId was null).
                            await SseWriter.WriteAsync(
                                response,
                                "tool_result",
                                new
                                {
                                    name,
                                    callId = functionResult.CallId ?? callKey,
                                    preview,
                                    truncated
                                },
                                cancellationToken);
                        }
                    }
                }
            }

            await SseWriter.WriteAsync(response, "done", new { agent = "crm" }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Client disconnected — nothing to do.
        }
        catch (Exception ex)
        {
            if (ex is IOException || ex is HttpRequestException || ex.InnerException is IOException)
            {
                await crmProvider.InvalidateClientAsync();
                await knowledgeProvider.InvalidateClientAsync();
            }
            // Log full exception for operators — client only sees a sanitized
            // SSE error event below. ex.Message may include payload fragments,
            // MCP tool args, or other internals.
            logger.LogError(ex, "CRM agent stream failed for customer {CustomerId}", request.CustomerId);
            await SseWriter.WriteAsync(
                response,
                "error",
                new { message = "Agent stream failed.", type = ex.GetType().Name },
                CancellationToken.None);
        }
    }
}
