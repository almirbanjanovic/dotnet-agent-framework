# Simple Agent
> Minimal Azure OpenAI connectivity test — validates that credentials, endpoint, and deployment are working.

Fully implemented — .NET console app using the Microsoft.Agents.AI SDK.

## Configuration

Required config keys (set via environment variables or a local `appsettings.json`):

| Key | Description |
|-----|-------------|
| `AzureOpenAi:Endpoint` | Azure OpenAI service endpoint |
| `AzureOpenAi:DeploymentName` | Model deployment name |
| `AzureAd:TenantId` | Entra tenant ID for DefaultAzureCredential (optional) |

> **Note:** simple-agent is not in the config-sync manifest. Copy the relevant keys from another component's generated `appsettings.{Environment}.json`, or set the values as environment variables.

## How to run locally

```bash
cd src/simple-agent
dotnet run
```

The agent sends a single prompt ("Tell me a joke about the cloud") to Azure OpenAI and prints the response. A successful response confirms that credentials and the deployment are configured correctly.

## Architecture role

Simple-agent is a developer validation tool used during Lab 1 to confirm Azure OpenAI connectivity before building the real agents. It is not deployed to AKS and has no role in the production architecture.
