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

Run config-sync to populate: `cd src/config-sync && dotnet run -- <key-vault-uri>`
