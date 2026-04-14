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

Console.WriteLine($"Using AI Foundry project endpoint: {endpoint}");
Console.WriteLine($"Model deployment: {deploymentName}");

var credential = new DefaultAzureCredential();

AIAgent agent = new AIProjectClient(new Uri(endpoint), credential)
    .AsAIAgent(
        model: deploymentName,
        instructions: "You are a helpful and funny assistant who tells short jokes.",
        name: "Joker"
    );

// Invoke the agent and output the text result.
var result = await agent.RunAsync("Tell me a joke about the cloud.");
Console.WriteLine($"\nAgent response:\n {result}");