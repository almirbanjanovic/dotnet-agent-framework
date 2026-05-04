# BFF API
> Backend-for-frontend API handling JWT validation, chat orchestration, image proxy, and Cosmos DB conversation persistence.

Implements the gateway API for Blazor UI with chat orchestration, CRM proxying, image proxy, and conversation persistence.

## Configuration

Required config keys (populated by config-sync from Key Vault):

| Key | Description |
|-----|-------------|
| `Orchestrator:BaseUrl` | Orchestrator Agent base URL |
| `CosmosDb:AgentsEndpoint` | Cosmos DB agents account endpoint |
| `CosmosDb:AgentsDatabase` | Agents database name (default: agents) |
| `Storage:ImagesEndpoint` | Blob storage endpoint for product images |
| `Storage:ImagesAccountName` | Storage account name |
| `Storage:ImagesContainer` | Blob container for images |
| `AzureAd:TenantId` | Entra tenant ID for DefaultAzureCredential |
| `AzureAd:BffClientId` | Entra app registration client ID for BFF |
| `Bff:Hostname` | BFF public hostname (for CORS/redirects) |
| `BlazorUi:Origin` | Optional CORS origin override (default: http://localhost:5008) |
| `DataMode` | Set to `InMemory` for local dev (header-based auth + local images) |

Run config-sync to populate: `cd src/config-sync && dotnet run -- <key-vault-uri> [environment]`

## How to run locally

```bash
cd src/bff-api
dotnet run
```

### Local dev sign-in
- `setup-local.{ps1,sh}` provisions a SPA app registration in your tenant and writes `AzureAd:Enabled=true`, `AzureAd:BffClientId`, and `AzureAd:CustomerMap` into `appsettings.Local.json`.
- The Blazor UI redirects to `login.microsoftonline.com`; the BFF validates the JWT and resolves the signed-in UPN to a customer ID via `AzureAd:CustomerMap`.
- Header-based dev auth (`X-Customer-Id`) is only honoured when `AzureAd:Enabled=false` AND `DataMode=InMemory` — keep it as an automated-test escape hatch only.

## Architecture role

BFF API is the backend-for-frontend that sits between the Blazor UI and all backend services. It handles JWT validation (MSAL/Entra), forwards chat messages (and prior conversation history) to the orchestrator-agent over HTTP/JSON, serves product images from Blob Storage, proxies direct CRM data requests to crm-api, and persists conversation history in a dedicated Cosmos DB agents database.
