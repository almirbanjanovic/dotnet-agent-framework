using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.IO;

namespace Contoso.ProductAgent.Endpoints;

// Single endpoint of the Product Agent. Same shape as the CRM Agent's
// endpoint so the orchestrator can route to either without per-agent
// branching.
//
// Two flavors:
//   POST /api/v1/chat        — buffered JSON response (legacy / tests)
//   POST /api/v1/chat/stream — Server-Sent Events: token deltas + tool
//                              calls as they happen.

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
        ProductAgentFactory agentFactory,
        SystemPromptProvider promptProvider,
        CrmMcpClientProvider crmProvider,
        KnowledgeMcpClientProvider knowledgeProvider,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerId) || string.IsNullOrWhiteSpace(request.Message))
        {
            return Results.BadRequest(new { error = "customerId and message are required." });
        }

        var isGuest = GuestId.IsGuest(request.CustomerId);

        // Discover tools from the MCP backends per request. For guests we
        // deliberately skip the CRM MCP entirely — they have no customer
        // record so any CRM tool call is meaningless and risky (prompt-
        // injection probing, hallucinated order details). Knowledge MCP
        // (catalog, return policy, FAQ) remains available.
        var tools = new List<AITool>();
        tools.AddRange(await knowledgeProvider.ExecuteWithClientRetryAsync(
            static (client, ct) => client.ListToolsAsync(cancellationToken: ct),
            cancellationToken));
        if (!isGuest)
        {
            tools.AddRange(await crmProvider.ExecuteWithClientRetryAsync(
                static (client, ct) => client.ListToolsAsync(cancellationToken: ct),
                cancellationToken));
        }

        var systemPrompt = isGuest
            ? promptProvider.Prompt + GuestId.AnonymousGuardrail
            : promptProvider.Prompt;
        var agent = agentFactory.CreateAgent(systemPrompt, tools);
        var messages = ChatHistoryBinder.Build(request.History, request.CustomerId, request.Message);
        var response = await agent.RunAsync(messages, cancellationToken: cancellationToken);

        var toolCalls = ToolCallExtractor.Extract(response);

        return Results.Ok(new ChatResponse(response.ToString(), toolCalls));
    }

    private static async Task HandleStreamAsync(
        ChatRequest request,
        ProductAgentFactory agentFactory,
        SystemPromptProvider promptProvider,
        CrmMcpClientProvider crmProvider,
        KnowledgeMcpClientProvider knowledgeProvider,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Contoso.ProductAgent.Endpoints.ChatStream");
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
            var isGuest = GuestId.IsGuest(request.CustomerId);

            // Anonymous guests get the Knowledge MCP tools only — see
            // HandleAsync above for the rationale.
            var tools = new List<AITool>();
            tools.AddRange(await knowledgeProvider.ExecuteWithClientRetryAsync(
                static (client, ct) => client.ListToolsAsync(cancellationToken: ct),
                cancellationToken));
            if (!isGuest)
            {
                tools.AddRange(await crmProvider.ExecuteWithClientRetryAsync(
                    static (client, ct) => client.ListToolsAsync(cancellationToken: ct),
                    cancellationToken));
            }

            var systemPrompt = isGuest
                ? promptProvider.Prompt + GuestId.AnonymousGuardrail
                : promptProvider.Prompt;
            var agent = agentFactory.CreateAgent(systemPrompt, tools);
            var messages = ChatHistoryBinder.Build(request.History, request.CustomerId, request.Message);

            var emittedToolCalls = new HashSet<string>(StringComparer.Ordinal);
            var emittedToolCallNames = new Dictionary<string, string>(StringComparer.Ordinal);
            var emittedToolResults = new HashSet<string>(StringComparer.Ordinal);
            // pendingCallKeys queues call-keys whose results have not yet
            // arrived, so a result with a null CallId pairs back to the
            // most recent unmatched call (instead of generating an
            // un-pairable random GUID and orphaning the result row).
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
                        var callKey = functionResult.CallId
                            ?? (pendingCallKeys.Count > 0 ? pendingCallKeys.Dequeue() : Guid.NewGuid().ToString("N"));
                        if (emittedToolResults.Add(callKey))
                        {
                            var (preview, truncated) = ToolResultPreview.Render(functionResult.Result);
                            var name = emittedToolCallNames.TryGetValue(callKey, out var n) ? n : "tool";
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

            await SseWriter.WriteAsync(response, "done", new { agent = "product" }, cancellationToken);
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
            logger.LogError(ex, "Product agent stream failed for customer {CustomerId}", request.CustomerId);
            await SseWriter.WriteAsync(
                response,
                "error",
                new { message = "Agent stream failed.", type = ex.GetType().Name },
                CancellationToken.None);
        }
    }
}
