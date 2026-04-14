using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Configuration;

var configuration = new ConfigurationBuilder()
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Development"}.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var endpoint = configuration["Foundry:Endpoint"] ?? throw new InvalidOperationException("Foundry:Endpoint is not set.");
var deploymentName = configuration["Foundry:DeploymentName"] ?? throw new InvalidOperationException("Foundry:DeploymentName is not set.");
var tenantId = configuration["AzureAd:TenantId"];

Console.WriteLine($"Using AI Foundry project endpoint: {endpoint}");
Console.WriteLine($"Model deployment: {deploymentName}");
Console.WriteLine($"Tenant ID: {(string.IsNullOrEmpty(tenantId) ? "(not set — using default credential)" : tenantId)}");

// Pin to the project tenant when AzureAd:TenantId is available (populated by
// config-sync from Key Vault secret AzureAd--TenantId).
var credential = string.IsNullOrEmpty(tenantId)
    ? new DefaultAzureCredential()
    : new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId });

AIAgent agent = new AIProjectClient(new Uri(endpoint), credential)
    .AsAIAgent(
        model: deploymentName,
        instructions: "You are a helpful and funny assistant who tells short jokes.",
        name: "Joker"
    );

// Invoke the agent and output the text result.
var result = await agent.RunAsync("Tell me a joke about the cloud.");
Console.WriteLine($"\nAgent response:\n {result}");