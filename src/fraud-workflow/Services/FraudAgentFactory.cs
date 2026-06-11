using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Contoso.FraudWorkflow.Services;

// Builds the Microsoft Agent Framework `AIAgent` instances for the three
// specialist agents. Authentication is always DefaultAzureCredential —
// never API keys (matches crm-agent / product-agent / orchestrator-agent).

internal sealed class FraudAgentFactory
{
    private readonly string _deploymentName;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IServiceProvider _services;
    private readonly AIProjectClient _projectClient;

    public FraudAgentFactory(IConfiguration configuration, ILoggerFactory loggerFactory, IServiceProvider services)
    {
        _loggerFactory = loggerFactory;
        _services = services;
        _deploymentName = configuration["Foundry:DeploymentName"]
            ?? throw new InvalidOperationException("Foundry:DeploymentName is not set.");

        var endpoint = configuration["Foundry:ProjectEndpoint"]
            ?? throw new InvalidOperationException("Foundry:ProjectEndpoint is not set.");

        var tenantId = configuration["AzureAd:TenantId"];
        var credential = string.IsNullOrEmpty(tenantId)
            ? new DefaultAzureCredential()
            : new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId });

        _projectClient = new AIProjectClient(new Uri(endpoint), credential);
    }

    public AIAgent CreateAgent(string name, string description, string instructions, IList<AITool> tools)
    {
        return _projectClient.AsAIAgent(
            model: _deploymentName,
            instructions: instructions,
            name: name,
            description: description,
            tools: tools,
            loggerFactory: _loggerFactory,
            services: _services);
    }
}
