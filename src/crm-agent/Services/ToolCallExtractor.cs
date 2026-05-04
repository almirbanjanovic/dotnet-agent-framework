using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

// Pulls every MCP function call the agent made out of the response so the
// BFF/UI can show "the agent looked up your order #12345" alongside the
// final answer.

internal static class ToolCallExtractor
{
    public static IReadOnlyList<ToolCallInfo> Extract(AgentResponse response)
    {
        var toolCalls = new List<ToolCallInfo>();
        foreach (var content in response.Messages.SelectMany(message => message.Contents))
        {
            if (content is not FunctionCallContent functionCall)
            {
                continue;
            }

            var arguments = functionCall.Arguments ?? new Dictionary<string, object?>();
            toolCalls.Add(new ToolCallInfo(
                functionCall.Name,
                arguments.ToDictionary(entry => entry.Key, entry => entry.Value)));
        }

        return toolCalls;
    }
}
