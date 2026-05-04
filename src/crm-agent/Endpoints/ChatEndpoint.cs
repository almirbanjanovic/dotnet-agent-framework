using Microsoft.Extensions.AI;

namespace Contoso.CrmAgent.Endpoints;

// Single endpoint of the CRM Agent. Receives a chat turn from the
// Orchestrator (CustomerId + message + prior history), discovers MCP
// tools from both backends, runs the agent, and returns the answer
// plus the list of tools the agent invoked.

internal static class ChatEndpoint
{
    public static IEndpointRouteBuilder MapChatEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/chat", HandleAsync);
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

        // Discover tools from both MCP backends per request. They're cheap
        // to enumerate and the agent's tool set may evolve at runtime.
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
