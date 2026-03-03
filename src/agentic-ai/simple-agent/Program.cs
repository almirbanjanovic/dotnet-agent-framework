using System.ClientModel;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Configuration;
using OpenAI.Chat;

// Configuration priority (last wins):
// 1. appsettings.json      - local dev settings (shared across all agentic-ai samples), not checked into source control
// 2. Environment variables  - used in AKS/Helm deployments
var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var endpoint = configuration["AZURE_OPENAI_ENDPOINT"] ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
var deploymentName = configuration["AZURE_OPENAI_DEPLOYMENT_NAME"] ?? throw new InvalidOperationException("AZURE_OPENAI_DEPLOYMENT_NAME is not set.");
var apiKey = configuration["AZURE_OPENAI_API_KEY"] ?? throw new InvalidOperationException("AZURE_OPENAI_API_KEY is not set.");

AIAgent agent = new AzureOpenAIClient(
    new Uri(endpoint),
    new ApiKeyCredential(apiKey))
    .GetChatClient(deploymentName)
    .AsAIAgent(instructions: "You are good at telling jokes.", name: "Joker");

// Invoke the agent and output the text result.
Console.WriteLine(await agent.RunAsync("Tell me a joke about a pirate."));