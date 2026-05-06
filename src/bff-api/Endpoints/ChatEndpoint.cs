using System.Text.Json;
using Contoso.BffApi.Models;
using Contoso.BffApi.Services;

namespace Contoso.BffApi.Endpoints;

// POST /api/v1/chat — the primary user-facing endpoint.
//
// Flow:
//   1. Resolve customer ID from CustomerContext (header in dev, JWT claim in prod).
//   2. Load or create the conversation.
//   3. Snapshot prior turns (so the orchestrator gets history as context),
//      then persist the new user message.
//   4. Forward to the orchestrator via OrchestratorClient.
//   5. Persist the assistant reply, return ChatResponse to the UI.

internal static class ChatEndpoint
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static RouteHandlerBuilder MapChatEndpoint(this IEndpointRouteBuilder app)
    {
        return app.MapPost("/chat", HandleAsync);
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
        // current turn.
        var historyForOrchestrator = conversation.Messages
            .Where(m => !string.IsNullOrEmpty(m.Content))
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
            return Results.Json(
                new
                {
                    error = ex.GetType().Name,
                    message = $"The AI agent is currently unavailable: {ex.Message}",
                    conversationId = conversation.Id
                },
                statusCode: StatusCodes.Status502BadGateway);
        }

        try
        {
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Orchestrator returned {StatusCode} for customer {CustomerId}. Body: {Body}",
                    (int)response.StatusCode, customerId, payload);

                // Always return JSON the UI can render — never proxy an empty body.
                var safeBody = string.IsNullOrWhiteSpace(payload) ? "(empty response)" : payload;
                return Results.Json(
                    new
                    {
                        error = "OrchestratorError",
                        message = $"The AI agent returned {(int)response.StatusCode}: {safeBody}",
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

            var assistantMessage = agentResponse?.Response ?? payload;
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
}
