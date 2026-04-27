# BFF API
> Backend-for-frontend API handling JWT validation, chat orchestration, image proxy, and Cosmos DB conversation persistence.

Implements the gateway API for Blazor UI with chat orchestration, CRM proxying, image proxy, and conversation persistence.

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
| `BlazorUi:Origin` | Optional CORS origin override (default: http://localhost:5001) |
| `DataMode` | Set to `InMemory` for local dev (header-based auth + local images) |

Run config-sync to populate: `cd src/config-sync && dotnet run -- <key-vault-uri> [environment]`

## How to run locally

```bash
cd src/bff-api
dotnet run
```

### Local dev auth
- Set `DataMode=InMemory`.
- Include `X-Customer-Id` header on chat/conversation requests.

## Architecture role

BFF API is the backend-for-frontend that sits between the Blazor UI and all backend services. It handles JWT validation (MSAL/Entra), proxies chat messages to the orchestrator-agent via SignalR, serves product images from Blob Storage, proxies direct CRM data requests to crm-api, and persists conversation history in a dedicated Cosmos DB agents database.
