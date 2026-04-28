using Azure;
using Azure.AI.OpenAI;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.Chat;

namespace Contoso.OrchestratorAgent.Services;

internal sealed class IntentClassifier
{
    private const string ClassificationTemplate = """
        Classify the following customer message into one of these categories:
        - CRM: order status, returns, refunds, account info, support tickets, complaints
        - PRODUCT: product recommendations, catalog browsing, pricing, promotions, sizing, gear advice

        Respond with ONLY the category name (CRM or PRODUCT).

        Customer message: {0}
        """;

    private readonly IIntentClassifierClient _client;

    public IntentClassifier(
        IConfiguration configuration,
        SystemPromptProvider promptProvider,
        ILoggerFactory loggerFactory,
        IServiceProvider services)
    {
        var agent = BuildAgent(configuration, promptProvider.Prompt, loggerFactory, services);
        _client = new AIAgentIntentClassifierClient(agent);
    }

    internal IntentClassifier(IIntentClassifierClient client)
    {
        _client = client;
    }

    public async Task<string> ClassifyAsync(string message, CancellationToken cancellationToken)
    {
        var prompt = string.Format(ClassificationTemplate, message);
        var intent = (await _client.RunAsync(prompt, cancellationToken)).Trim();

        if (intent.Contains("PRODUCT", StringComparison.OrdinalIgnoreCase))
        {
            return "PRODUCT";
        }

        if (intent.Contains("CRM", StringComparison.OrdinalIgnoreCase))
        {
            return "CRM";
        }

        return "CRM";
    }

    private static AIAgent BuildAgent(
        IConfiguration configuration,
        string prompt,
        ILoggerFactory loggerFactory,
        IServiceProvider services)
    {
        var deploymentName = configuration["Foundry:DeploymentName"]
            ?? throw new InvalidOperationException("Foundry:DeploymentName is not set.");
        var endpoint = configuration["Foundry:Endpoint"]
            ?? throw new InvalidOperationException("Foundry:Endpoint is not set.");
        var apiKey = configuration["Foundry:ApiKey"];

        if (!string.IsNullOrEmpty(apiKey))
        {
            var client = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
            return client.GetChatClient(deploymentName).AsAIAgent(
                instructions: prompt,
                name: "Orchestrator Agent",
                description: "Classifies customer intent for routing to CRM or Product agents.",
                loggerFactory: loggerFactory,
                services: services);
        }

        var tenantId = configuration["AzureAd:TenantId"];
        var credential = string.IsNullOrEmpty(tenantId)
            ? new DefaultAzureCredential()
            : new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId });

        var projectClient = new AIProjectClient(new Uri(endpoint), credential);
        return projectClient.AsAIAgent(
            model: deploymentName,
            instructions: prompt,
            name: "Orchestrator Agent",
            description: "Classifies customer intent for routing to CRM or Product agents.",
            loggerFactory: loggerFactory,
            services: services);
    }
}

internal interface IIntentClassifierClient
{
    Task<string> RunAsync(string prompt, CancellationToken cancellationToken);
}

internal sealed class AIAgentIntentClassifierClient : IIntentClassifierClient
{
    private readonly AIAgent _agent;
    private readonly ChatClientAgentRunOptions _runOptions;

    public AIAgentIntentClassifierClient(AIAgent agent)
    {
        _agent = agent;
        _runOptions = new ChatClientAgentRunOptions(new ChatOptions
        {
            MaxOutputTokens = 5,
            Temperature = 0,
            ToolMode = ChatToolMode.None
        });
    }

    public async Task<string> RunAsync(string prompt, CancellationToken cancellationToken)
    {
        var response = await _agent.RunAsync(prompt, options: _runOptions, cancellationToken: cancellationToken);
        return response.ToString();
    }
}
