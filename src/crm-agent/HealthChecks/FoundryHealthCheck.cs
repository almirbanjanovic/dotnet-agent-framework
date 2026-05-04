using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenAI.Chat;

// Issues a one-token, no-tool ping at the Foundry chat deployment via a
// fresh AIAgent. Verifies both the deployment is alive and the agent's
// workload identity (or local az login) can call it.

internal sealed class FoundryHealthCheck(CrmAgentFactory agentFactory, SystemPromptProvider promptProvider) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var agent = agentFactory.CreateAgent(promptProvider.Prompt, new List<AITool>());
            var options = new ChatClientAgentRunOptions(new ChatOptions
            {
                MaxOutputTokens = 1,
                Temperature = 0,
                ToolMode = ChatToolMode.None
            });

            _ = await agent.RunAsync("ping", options: options, cancellationToken: cancellationToken);
            return HealthCheckResult.Healthy("Foundry chat model is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Foundry chat model is not reachable.", ex);
        }
    }
}
