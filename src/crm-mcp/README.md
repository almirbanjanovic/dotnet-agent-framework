# CRM MCP Server
> MCP Server exposing 10 tools that wrap CRM API endpoints for agent consumption.

Implementation pending. See docs/implementation-plan.md for details.

## Configuration

Required config keys (populated by config-sync from Key Vault):

| Key | Description |
|-----|-------------|
| `CrmApi:BaseUrl` | CRM API base URL |
| `AzureAd:TenantId` | Entra tenant ID for DefaultAzureCredential |

Run config-sync to populate: `cd src/config-sync && dotnet run -- <key-vault-uri> [environment]`

## How to run locally

Implementation pending. Once built:

```bash
cd src/crm-mcp
dotnet run
```

## Architecture role

CRM MCP Server is a thin protocol adapter that translates MCP tool calls into CRM API HTTP requests. Agents (crm-agent, product-agent) connect to this server to access customer, order, product, promotion, and ticket data without knowing the REST API contract directly.
