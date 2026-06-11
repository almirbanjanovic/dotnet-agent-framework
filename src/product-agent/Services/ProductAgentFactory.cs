using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

internal sealed class ProductAgentFactory
{
    private readonly string _deploymentName;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IServiceProvider _services;
    private readonly AIProjectClient _projectClient;
    private readonly AITool? _toolboxTool;

    public ProductAgentFactory(IConfiguration configuration, ILoggerFactory loggerFactory, IServiceProvider services)
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

        var toolboxName = configuration["Foundry:ToolboxName"];
        if (!string.IsNullOrWhiteSpace(toolboxName))
        {
            _toolboxTool = FoundryAITool.CreateHostedMcpToolbox(toolboxName, version: null);
        }
    }

    public AIAgent CreateAgent(string instructions, IList<AITool> tools, bool includeToolbox = true)
    {
        const string agentName = "Product Agent";
        const string description = "Contoso Outdoors product specialist for catalog browsing, promotions, and gear advice.";

        var mergedTools = MergeTools(tools, _toolboxTool, includeToolbox);

        return _projectClient.AsAIAgent(
            model: _deploymentName,
            instructions: instructions,
            name: agentName,
            description: description,
            tools: mergedTools,
            loggerFactory: _loggerFactory,
            services: _services);
    }

    internal static List<AITool> MergeTools(IList<AITool> tools, AITool? toolboxTool, bool includeToolbox)
    {
        List<AITool> mergedTools = [.. tools];
        if (includeToolbox && toolboxTool is not null)
        {
            mergedTools.Add(toolboxTool);
        }

        return mergedTools;
    }
}
