using Azure;
using Azure.AI.OpenAI;
using Azure.AI.Projects;
using Azure.Identity;
using Contoso.ServiceDefaults;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ModelContextProtocol.Client;
using OpenAI.Chat;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;
using ChatRole = Microsoft.Extensions.AI.ChatRole;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile(
    $"appsettings.{builder.Environment.EnvironmentName}.json",
    optional: true,
    reloadOnChange: true);

builder.AddServiceDefaults();

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5005);
});

builder.Services.AddSingleton<CrmMcpClientProvider>();
builder.Services.AddSingleton<KnowledgeMcpClientProvider>();
builder.Services.AddSingleton<SystemPromptProvider>();
builder.Services.AddSingleton<ProductAgentFactory>();

builder.Services.AddHealthChecks()
    .AddCheck<CrmMcpHealthCheck>("crm-mcp", tags: ["ready"])
    .AddCheck<KnowledgeMcpHealthCheck>("knowledge-mcp", tags: ["ready"])
    .AddCheck<FoundryHealthCheck>("foundry", tags: ["ready"]);

var app = builder.Build();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapPost("/api/v1/chat", async (
    ChatRequest request,
    ProductAgentFactory agentFactory,
    SystemPromptProvider promptProvider,
    CrmMcpClientProvider crmProvider,
    KnowledgeMcpClientProvider knowledgeProvider,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.CustomerId) || string.IsNullOrWhiteSpace(request.Message))
    {
        return Results.BadRequest(new { error = "customerId and message are required." });
    }

    var crmClient = await crmProvider.GetClientAsync(cancellationToken);
    var knowledgeClient = await knowledgeProvider.GetClientAsync(cancellationToken);

    var tools = new List<AITool>();
    tools.AddRange(await crmClient.ListToolsAsync(cancellationToken: cancellationToken));
    tools.AddRange(await knowledgeClient.ListToolsAsync(cancellationToken: cancellationToken));

    var agent = agentFactory.CreateAgent(promptProvider.Prompt, tools);
    var userMessage = new ChatMessage(ChatRole.User, $"CustomerId: {request.CustomerId}\nMessage: {request.Message}");
    var response = await agent.RunAsync(userMessage, cancellationToken: cancellationToken);

    var toolCalls = ToolCallExtractor.Extract(response);

    return Results.Ok(new ChatResponse(response.ToString(), toolCalls));
});

app.Run();

internal sealed record ChatRequest(string CustomerId, string Message);

internal sealed record ChatResponse(string Response, IReadOnlyList<ToolCallInfo> ToolCalls);

internal sealed record ToolCallInfo(string Name, IReadOnlyDictionary<string, object?> Arguments);

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

internal sealed class SystemPromptProvider
{
    public SystemPromptProvider(IHostEnvironment environment)
    {
        var promptPath = Path.Combine(environment.ContentRootPath, "Prompts", "system-prompt.md");
        Prompt = File.ReadAllText(promptPath);
    }

    public string Prompt { get; }
}

internal sealed class ProductAgentFactory
{
    private readonly string _deploymentName;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IServiceProvider _services;
    private readonly OpenAI.Chat.ChatClient? _chatClient;
    private readonly AIProjectClient? _projectClient;

    public ProductAgentFactory(IConfiguration configuration, ILoggerFactory loggerFactory, IServiceProvider services)
    {
        _loggerFactory = loggerFactory;
        _services = services;
        _deploymentName = configuration["Foundry:DeploymentName"]
            ?? throw new InvalidOperationException("Foundry:DeploymentName is not set.");

        var endpoint = configuration["Foundry:Endpoint"]
            ?? throw new InvalidOperationException("Foundry:Endpoint is not set.");

        var apiKey = configuration["Foundry:ApiKey"];

        if (!string.IsNullOrEmpty(apiKey))
        {
            var client = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
            _chatClient = client.GetChatClient(_deploymentName);
        }
        else
        {
            var tenantId = configuration["AzureAd:TenantId"];
            var credential = string.IsNullOrEmpty(tenantId)
                ? new DefaultAzureCredential()
                : new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId });

            _projectClient = new AIProjectClient(new Uri(endpoint), credential);
        }
    }

    public AIAgent CreateAgent(string instructions, IList<AITool> tools)
    {
        const string agentName = "Product Agent";
        const string description = "Contoso Outdoors product specialist for catalog browsing, promotions, and gear advice.";

        if (_chatClient is not null)
        {
            return _chatClient.AsAIAgent(
                instructions: instructions,
                name: agentName,
                description: description,
                tools: tools,
                loggerFactory: _loggerFactory,
                services: _services);
        }

        return _projectClient!.AsAIAgent(
            model: _deploymentName,
            instructions: instructions,
            name: agentName,
            description: description,
            tools: tools,
            loggerFactory: _loggerFactory,
            services: _services);
    }
}

internal abstract class McpClientProvider
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _baseUrl;
    private McpClient? _client;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    protected McpClientProvider(string name, string baseUrl, ILoggerFactory loggerFactory)
    {
        Name = name;
        _baseUrl = baseUrl;
        _loggerFactory = loggerFactory;
    }

    public string Name { get; }

    public async Task<McpClient> GetClientAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
        {
            return _client;
        }

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            _client ??= await CreateClientAsync(cancellationToken);
            return _client;
        }
        catch
        {
            _client = null;
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<McpClient> CreateClientAsync(CancellationToken cancellationToken)
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(_baseUrl),
            Name = Name
        }, _loggerFactory);

        return await McpClient.CreateAsync(transport, loggerFactory: _loggerFactory, cancellationToken: cancellationToken);
    }
}

internal sealed class CrmMcpClientProvider : McpClientProvider
{
    public CrmMcpClientProvider(IConfiguration configuration, ILoggerFactory loggerFactory)
        : base("crm-mcp", configuration["CrmMcp:BaseUrl"] ?? "http://localhost:5002", loggerFactory)
    {
    }
}

internal sealed class KnowledgeMcpClientProvider : McpClientProvider
{
    public KnowledgeMcpClientProvider(IConfiguration configuration, ILoggerFactory loggerFactory)
        : base("knowledge-mcp", configuration["KnowledgeMcp:BaseUrl"] ?? "http://localhost:5003", loggerFactory)
    {
    }
}

internal sealed class CrmMcpHealthCheck(CrmMcpClientProvider crmProvider) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = await crmProvider.GetClientAsync(cancellationToken);
            _ = await client.PingAsync(cancellationToken: cancellationToken);
            return HealthCheckResult.Healthy("CRM MCP server is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("CRM MCP server is not reachable.", ex);
        }
    }
}

internal sealed class KnowledgeMcpHealthCheck(KnowledgeMcpClientProvider knowledgeProvider) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = await knowledgeProvider.GetClientAsync(cancellationToken);
            _ = await client.PingAsync(cancellationToken: cancellationToken);
            return HealthCheckResult.Healthy("Knowledge MCP server is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Knowledge MCP server is not reachable.", ex);
        }
    }
}

internal sealed class FoundryHealthCheck(ProductAgentFactory agentFactory, SystemPromptProvider promptProvider) : IHealthCheck
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
