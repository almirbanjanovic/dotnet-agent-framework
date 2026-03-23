using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Configuration;
using OpenAI.Chat;

// Configuration priority (last wins):
// 1. appsettings.json      - local dev settings (populated by config-sync tool from Key Vault)
// 2. Environment variables  - used in AKS/Helm deployments
var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var endpoint = configuration["AZURE_OPENAI_ENDPOINT"] ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
var deploymentName = configuration["AZURE_OPENAI_DEPLOYMENT_NAME"] ?? throw new InvalidOperationException("AZURE_OPENAI_DEPLOYMENT_NAME is not set.");
var tenantId = configuration["AzureAd:TenantId"] ?? configuration["AzureAd__TenantId"];

Console.WriteLine($"Using Azure OpenAI endpoint: {endpoint}");
Console.WriteLine($"Deployment name: {deploymentName}");
Console.WriteLine($"Tenant ID: {(string.IsNullOrEmpty(tenantId) ? "(not set — using default credential)" : tenantId)}");

// Pin to the project tenant when AzureAd:TenantId is available (populated by
// config-sync from Key Vault secret AzureAd__TenantId). Prevents
// DefaultAzureCredential from picking up tokens from the wrong tenant.
var credential = string.IsNullOrEmpty(tenantId)
    ? new DefaultAzureCredential()
    : new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId });

AIAgent agent = new AzureOpenAIClient(
    new Uri(endpoint),
    credential)
    .GetChatClient(deploymentName)
    .AsAIAgent(instructions: "You are a helpful and funny assistant who tells short jokes.", name: "Joker");

// Invoke the agent and output the text result.
var result = await agent.RunAsync("Tell me a joke about the cloud.");
Console.WriteLine($"\nAgent response:\n {result}");