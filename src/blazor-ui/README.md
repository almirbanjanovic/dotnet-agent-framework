# Blazor UI
> Blazor WebAssembly SPA with MudBlazor components and MSAL authentication.

Single-page application served as static files. The UI sends chat requests as plain HTTPS POSTs to the BFF API; the response is a single JSON document containing the conversation id, assistant text, and any tool calls.

## Configuration

Configuration is baked into `wwwroot/appsettings.json` at publish time. The container itself has no runtime configuration:

| Key | Description |
|-----|-------------|
| `Bff:BaseUrl` | BFF API base URL |
| `AzureAd:BffClientId` | Entra app registration client ID for BFF |
| `AzureAd:TenantId` | Entra tenant ID |

## How to run locally

```bash
cd src/blazor-ui
dotnet run
```

The dev server starts on the URL bound by `ASPNETCORE_URLS` (Aspire AppHost binds 5008 in local dev) and proxies API calls to the BFF.

## Architecture role

Blazor UI is the user-facing single-page application. It authenticates users via MSAL (Entra ID), sends chat messages to the BFF API as HTTP/JSON, and renders agent responses (including markdown-formatted product images) using MudBlazor + Markdig. It runs entirely in the browser as a WebAssembly app and ships in a static-file container served by nginx.

