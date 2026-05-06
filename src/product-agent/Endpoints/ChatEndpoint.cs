using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

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

        // Discover tools from both MCP backends per request.
        var crmClient = await crmProvider.GetClientAsync(cancellationToken);
        var knowledgeClient = await knowledgeProvider.GetClientAsync(cancellationToken);

        var tools = new List<AITool>();
        tools.AddRange(await crmClient.ListToolsAsync(cancellationToken: cancellationToken));
        tools.AddRange(await knowledgeClient.ListToolsAsync(cancellationToken: cancellationToken));

        var agent = agentFactory.CreateAgent(promptProvider.Prompt, tools);
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
        CancellationToken cancellationToken)
    {
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
            var crmClient = await crmProvider.GetClientAsync(cancellationToken);
            var knowledgeClient = await knowledgeProvider.GetClientAsync(cancellationToken);

            var tools = new List<AITool>();
            tools.AddRange(await crmClient.ListToolsAsync(cancellationToken: cancellationToken));
            tools.AddRange(await knowledgeClient.ListToolsAsync(cancellationToken: cancellationToken));

            var agent = agentFactory.CreateAgent(promptProvider.Prompt, tools);
            var messages = ChatHistoryBinder.Build(request.History, request.CustomerId, request.Message);

            var emittedToolCalls = new HashSet<string>(StringComparer.Ordinal);

            await foreach (var update in agent.RunStreamingAsync(messages, cancellationToken: cancellationToken))
            {
                if (!string.IsNullOrEmpty(update.Text))
                {
                    await SseWriter.WriteAsync(response, "token", new { text = update.Text }, cancellationToken);
                }

                foreach (var content in update.Contents)
                {
                    if (content is FunctionCallContent functionCall &&
                        emittedToolCalls.Add(functionCall.CallId ?? functionCall.Name))
                    {
                        var args = functionCall.Arguments ?? new Dictionary<string, object?>();
                        await SseWriter.WriteAsync(
                            response,
                            "tool",
                            new { name = functionCall.Name, arguments = args },
                            cancellationToken);
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
            // Surface only the type name — ex.Message may include payload
            // fragments, MCP tool args, or other internals.
            await SseWriter.WriteAsync(
                response,
                "error",
                new { message = "Agent stream failed.", type = ex.GetType().Name },
                CancellationToken.None);
        }
    }
}
