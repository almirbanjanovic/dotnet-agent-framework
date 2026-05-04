using Microsoft.Extensions.AI;

namespace Contoso.ProductAgent.Endpoints;

// Single endpoint of the Product Agent. Same shape as the CRM Agent's
// endpoint so the orchestrator can route to either without per-agent
// branching.

internal static class ChatEndpoint
{
    public static IEndpointRouteBuilder MapChatEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/chat", HandleAsync);
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
}
