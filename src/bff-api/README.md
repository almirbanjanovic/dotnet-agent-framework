# BFF API
> Backend-for-frontend API handling JWT validation, chat orchestration, image proxy, and Cosmos DB conversation persistence.

Implementation pending. See docs/implementation-plan.md for details.

## Configuration

Required config keys (populated by config-sync from Key Vault):

| Key | Description |
|-----|-------------|
| `Orchestrator:BaseUrl` | Orchestrator Agent base URL |
| `CosmosDb:AgentsEndpoint` | Cosmos DB agents account endpoint |
| `CosmosDb:AgentsDatabase` | Agents database name (default: contoso-agents) |
| `Storage:ImagesEndpoint` | Blob storage endpoint for product images |
| `Storage:ImagesAccountName` | Storage account name |
| `Storage:ImagesContainer` | Blob container for images |
| `AzureAd:TenantId` | Entra tenant ID for DefaultAzureCredential |
| `AzureAd:BffClientId` | Entra app registration client ID for BFF |
| `Bff:Hostname` | BFF public hostname (for CORS/redirects) |

Run config-sync to populate: `cd src/config-sync && dotnet run -- <key-vault-uri> [environment]`

## How to run locally

Implementation pending. Once built:

```bash
cd src/bff-api
dotnet run
```

## Architecture role

BFF API is the backend-for-frontend that sits between the Blazor UI and all backend services. It handles JWT validation (MSAL/Entra), proxies chat messages to the orchestrator-agent via SignalR, serves product images from Blob Storage, proxies direct CRM data requests to crm-api, and persists conversation history in a dedicated Cosmos DB agents database.
