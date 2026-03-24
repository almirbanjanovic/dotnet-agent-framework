# Orchestrator Agent
> Intent classification and routing agent that delegates to CRM Agent and Product Agent.

Implementation pending. See docs/implementation-plan.md for details.

## Configuration

Required config keys (populated by config-sync from Key Vault):

| Key | Description |
|-----|-------------|
| `AzureOpenAi:Endpoint` | Azure OpenAI service endpoint |
| `AzureOpenAi:DeploymentName` | Model deployment name |
| `CrmAgent:BaseUrl` | CRM Agent base URL |
| `ProductAgent:BaseUrl` | Product Agent base URL |
| `AzureAd:TenantId` | Entra tenant ID for DefaultAzureCredential |

Run config-sync to populate: `cd src/config-sync && dotnet run -- <key-vault-uri>`
