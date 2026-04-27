using Azure;
using Azure.AI.OpenAI;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Configuration;
using OpenAI.Chat;

var configuration = new ConfigurationBuilder()
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Development"}.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var endpoint = configuration["Foundry:Endpoint"] ?? throw new InvalidOperationException("Foundry:Endpoint is not set.");
var deploymentName = configuration["Foundry:DeploymentName"] ?? throw new InvalidOperationException("Foundry:DeploymentName is not set.");
var apiKey = configuration["Foundry:ApiKey"];

Console.WriteLine($"Using AI Foundry project endpoint: {endpoint}");
Console.WriteLine($"Model deployment: {deploymentName}");

AIAgent agent;
if (!string.IsNullOrEmpty(apiKey))
{
    // Local dev: API key auth
    Console.WriteLine("Auth mode: API Key");
    var client = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
    agent = client.GetChatClient(deploymentName).AsAIAgent(
        name: "Joker",
        instructions: "You are a helpful and funny assistant who tells short jokes."
    );
}
else
{
    // Production: DefaultAzureCredential
    var tenantId = configuration["AzureAd:TenantId"];
    Console.WriteLine($"Auth mode: DefaultAzureCredential (Tenant: {(string.IsNullOrEmpty(tenantId) ? "default" : tenantId)})");

    var credential = string.IsNullOrEmpty(tenantId)
        ? new DefaultAzureCredential()
        : new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId });

    agent = new AIProjectClient(new Uri(endpoint), credential)
        .AsAIAgent(
            model: deploymentName,
            instructions: "You are a helpful and funny assistant who tells short jokes.",
            name: "Joker"
        );
}

// Invoke the agent and output the text result.
var result = await agent.RunAsync("Tell me a joke about the cloud.");
Console.WriteLine($"\nAgent response:\n {result}");