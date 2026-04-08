# Config Sync
> CLI tool that pulls secrets from Azure Key Vault into per-component `appsettings.{Environment}.json` files.

Fully implemented — .NET console app using `DefaultAzureCredential`.

## Usage

```bash
cd src/config-sync
dotnet run -- <key-vault-uri> [environment]
```

**Examples:**

```bash
# Interactive — prompts for environment
dotnet run -- https://kv-agentic-ai-001.vault.azure.net/

# Non-interactive — specify environment directly
dotnet run -- https://kv-agentic-ai-001.vault.azure.net/ Development
dotnet run -- https://kv-agentic-ai-001.vault.azure.net/ Staging
dotnet run -- https://kv-agentic-ai-001.vault.azure.net/ Production
```

You can find the Key Vault URI with: `terraform output keyvault_uri`

## How it works

1. Authenticates to Key Vault via `DefaultAzureCredential` (`az login` locally, managed identity on AKS).
2. Reads the component manifest (hardcoded in `Program.cs`) which maps Key Vault secret names to per-component config keys.
3. Fetches all unique secrets from Key Vault.
4. Writes a separate `appsettings.{Environment}.json` for each component under `src/<component>/`.

**Key Vault naming convention:** `PascalCase--Hierarchy` (double-hyphen = .NET `:` separator).
Example: `CosmosDb--CrmEndpoint` → `{ "CosmosDb": { "CrmEndpoint": "..." } }`

All generated `appsettings.*.json` files are gitignored and never committed.

## Component manifest

| Component | # Keys | Key Vault Secrets |
|-----------|--------|-------------------|
| crm-api | 3 | CosmosDb--CrmEndpoint, CosmosDb--CrmDatabase, AzureAd--TenantId |
| crm-mcp | 2 | CrmApi--BaseUrl, AzureAd--TenantId |
| knowledge-mcp | 6 | Search--Endpoint, Search--IndexName, Storage--ImagesEndpoint, Storage--ImagesAccountName, Storage--ImagesContainer, AzureAd--TenantId |
| crm-agent | 4 | AzureOpenAi--Endpoint, AzureOpenAi--DeploymentName, CrmMcp--BaseUrl, AzureAd--TenantId |
| product-agent | 5 | AzureOpenAi--Endpoint, AzureOpenAi--DeploymentName, KnowledgeMcp--BaseUrl, CrmMcp--BaseUrl, AzureAd--TenantId |
| orchestrator-agent | 5 | AzureOpenAi--Endpoint, AzureOpenAi--DeploymentName, CrmAgent--BaseUrl, ProductAgent--BaseUrl, AzureAd--TenantId |
| bff-api | 9 | Orchestrator--BaseUrl, CosmosDb--AgentsEndpoint, CosmosDb--AgentsDatabase, Storage--ImagesEndpoint, Storage--ImagesAccountName, Storage--ImagesContainer, AzureAd--TenantId, AzureAd--BffClientId, Bff--Hostname |
| blazor-ui | 3 | Bff--BaseUrl, AzureAd--BffClientId, AzureAd--TenantId |
| simple-agent | 3 | AzureOpenAi--Endpoint, AzureOpenAi--DeploymentName, AzureAd--TenantId |

## Architecture role

Config-sync is a local dev tool (not deployed to AKS). It bridges the gap between centralized Key Vault secrets and the per-component `appsettings.{Environment}.json` files that each .NET project reads at startup. In AKS, the same config keys are injected as environment variables via Helm values, so config-sync is only needed for local development.
