# Simple Agent
> Minimal AI Foundry connectivity test — validates that credentials, endpoint, and deployment are working.

Fully implemented — .NET console app using the Microsoft.Agents.AI 1.0 GA SDK with `AIProjectClient` (Foundry API).

## Configuration

Required config keys (set via environment variables or a local `appsettings.json`):

| Key | Description |
|-----|-------------|
| `AzureOpenAi:Endpoint` | AI Foundry project endpoint (Azure AI Services URL) |
| `AzureOpenAi:DeploymentName` | Model deployment name (passed to `GetResponsesClient()`) |
| `AzureAd:TenantId` | Entra tenant ID for DefaultAzureCredential (optional) |

> **Note:** simple-agent is not in the config-sync manifest. Copy the relevant keys from another component's generated `appsettings.{Environment}.json`, or set the values as environment variables.

## How to run locally

```bash
cd src/simple-agent
dotnet run
```

The agent sends a single prompt ("Tell me a joke about the cloud") via the Foundry Responses API and prints the response. A successful response confirms that credentials and the deployment are configured correctly.

## Architecture role

Simple-agent is a developer validation tool used during Lab 1 to confirm AI Foundry connectivity before building the real agents. It is not deployed to AKS and has no role in the production architecture.
