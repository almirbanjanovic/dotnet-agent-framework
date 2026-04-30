using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Configuration;

// ─────────────────────────────────────────────────────────────────────────
// simple-agent — the smallest possible Microsoft Agent Framework example.
//
// Reads the Foundry endpoint and chat deployment from appsettings (set by
// `infra/setup-local.{ps1,sh}` from terraform output), authenticates with
// DefaultAzureCredential — no API keys, in any environment — and asks a
// single agent ("Joker") for a one-off response.
// ─────────────────────────────────────────────────────────────────────────

var configuration = new ConfigurationBuilder()
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Development"}.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var endpoint       = configuration["Foundry:Endpoint"]       ?? throw new InvalidOperationException("Foundry:Endpoint is not set.");
var deploymentName = configuration["Foundry:DeploymentName"] ?? throw new InvalidOperationException("Foundry:DeploymentName is not set.");
var tenantId       = configuration["AzureAd:TenantId"];

Console.WriteLine($"Using AI Foundry project endpoint: {endpoint}");
Console.WriteLine($"Model deployment:                  {deploymentName}");
Console.WriteLine($"Auth mode:                         DefaultAzureCredential (Tenant: {(string.IsNullOrEmpty(tenantId) ? "default" : tenantId)})");

// DefaultAzureCredential walks a chain of credential sources:
//   az CLI → Visual Studio → Managed Identity → Workload Identity → ...
// `setup-local` granted you the "Cognitive Services OpenAI User" role on
// the Foundry account, so your `az login` token is sufficient locally.
// On AKS the same code resolves the agent's workload identity instead.
var credential = string.IsNullOrEmpty(tenantId)
    ? new DefaultAzureCredential()
    : new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId });

// `AsAIAgent` is the Microsoft Agent Framework entry point: it takes any
// Foundry model deployment and returns an `AIAgent` you can call with a
// single `RunAsync` invocation. No system prompt? No tools? No memory?
// That's fine — this agent does one thing.
AIAgent agent = new AIProjectClient(new Uri(endpoint), credential)
    .AsAIAgent(
        model: deploymentName,
        instructions: "You are a helpful and funny assistant who tells short jokes.",
        name: "Joker");

var result = await agent.RunAsync("Tell me a joke about the cloud.");
Console.WriteLine($"\nAgent response:\n {result}");
