using Contoso.OrchestratorAgent.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Contoso.OrchestratorAgent.HealthChecks;

// Issues a tiny "Ping" classification through the IntentClassifier so
// we exercise the Foundry chat deployment end-to-end (DefaultAzureCredential
// → AIProjectClient → AIAgent.RunAsync).

internal sealed class FoundryHealthCheck(IntentClassifier classifier) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _ = await classifier.ClassifyAsync("Ping", cancellationToken);
            return HealthCheckResult.Healthy("Foundry chat model is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Foundry chat model is not reachable.", ex);
        }
    }
}
