using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

// Builds the Microsoft Agent Framework `AIAgent` for the CRM specialist.
// Authentication is always DefaultAzureCredential — never API keys:
//   - Local: deployer's `az login` token (granted "Cognitive Services
//     OpenAI User" by setup-local Terraform).
//   - AKS:   workload identity backed by the CRM Agent's Entra Agent ID.

internal sealed class CrmAgentFactory
{
    private readonly string _deploymentName;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IServiceProvider _services;
    private readonly AIProjectClient _projectClient;

    public CrmAgentFactory(IConfiguration configuration, ILoggerFactory loggerFactory, IServiceProvider services)
    {
        _loggerFactory = loggerFactory;
        _services = services;
        _deploymentName = configuration["Foundry:DeploymentName"]
            ?? throw new InvalidOperationException("Foundry:DeploymentName is not set.");

        var endpoint = configuration["Foundry:Endpoint"]
            ?? throw new InvalidOperationException("Foundry:Endpoint is not set.");

        var tenantId = configuration["AzureAd:TenantId"];
        var credential = string.IsNullOrEmpty(tenantId)
            ? new DefaultAzureCredential()
            : new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId });

        _projectClient = new AIProjectClient(new Uri(endpoint), credential);
    }

    public AIAgent CreateAgent(string instructions, IList<AITool> tools)
    {
        const string agentName = "CRM Agent";
        const string description = "Contoso Outdoors customer service agent for orders, returns, and support tickets.";

        return _projectClient.AsAIAgent(
            model: _deploymentName,
            instructions: instructions,
            name: agentName,
            description: description,
            tools: tools,
            loggerFactory: _loggerFactory,
            services: _services);
    }
}
