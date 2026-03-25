# Blazor UI
> Blazor WebAssembly SPA with MudBlazor, MSAL authentication, and real-time chat via SignalR.

Implementation pending. See docs/implementation-plan.md for details.

## Configuration

Required config keys (populated by config-sync from Key Vault):

| Key | Description |
|-----|-------------|
| `Bff:BaseUrl` | BFF API base URL |
| `AzureAd:BffClientId` | Entra app registration client ID for BFF |
| `AzureAd:TenantId` | Entra tenant ID for DefaultAzureCredential |

Run config-sync to populate: `cd src/config-sync && dotnet run -- <key-vault-uri> [environment]`

## How to run locally

Implementation pending. Once built:

```bash
cd src/blazor-ui
dotnet run
```

## Architecture role

Blazor UI is the user-facing single-page application. It authenticates users via MSAL (Entra ID), connects to the BFF API over SignalR for real-time chat, and renders agent responses including markdown-formatted product images. It runs entirely in the browser as a WebAssembly app.
