# Product Agent
> Product specialist agent using Azure AI Foundry chat with Knowledge MCP and CRM MCP tools.

ASP.NET Core Minimal API exposing `POST /api/v1/chat`. Builds an `AIAgent` from `Microsoft.Agents.AI`, attaches both MCP toolsets, and forwards prior turns from the orchestrator's `history` field so multi-turn context is preserved.

## Configuration

Required config keys (populated by config-sync from Key Vault):

| Key | Description |
|-----|-------------|
| `Foundry:ProjectEndpoint` | AI Foundry project endpoint |
| `Foundry:DeploymentName` | Model deployment name |
| `KnowledgeMcp:BaseUrl` | Knowledge MCP server base URL |
| `CrmMcp:BaseUrl` | CRM MCP server base URL |
| `AzureAd:TenantId` | Entra tenant ID for DefaultAzureCredential |

Optional:

| Key | Description |
|-----|-------------|
| `Foundry:ToolboxName` | Optional Foundry-hosted MCP Toolbox name. Empty by default; guest requests suppress the hosted toolbox even when configured. |

Run config-sync to populate: `cd src/config-sync && dotnet run -- <key-vault-uri> [environment]`

## How to run locally

```bash
cd src/product-agent
dotnet run
```

The agent listens on the URL bound by `ASPNETCORE_URLS` (Aspire AppHost binds 5005 in local dev).

## Architecture role

Product Agent is a specialist LLM agent for catalog browsing, promotions, recommendations, and sizing guides. The orchestrator-agent routes product-related intents here. It connects to both knowledge-mcp (semantic search) and crm-mcp (customer context) and uses Azure AI Foundry for reasoning.

