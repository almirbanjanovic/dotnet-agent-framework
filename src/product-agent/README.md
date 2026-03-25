# Product Agent
> Product specialist agent using Azure OpenAI with Knowledge MCP and CRM MCP tools.

Implementation pending. See docs/implementation-plan.md for details.

## Configuration

Required config keys (populated by config-sync from Key Vault):

| Key | Description |
|-----|-------------|
| `AzureOpenAi:Endpoint` | Azure OpenAI service endpoint |
| `AzureOpenAi:DeploymentName` | Model deployment name |
| `KnowledgeMcp:BaseUrl` | Knowledge MCP server base URL |
| `CrmMcp:BaseUrl` | CRM MCP server base URL |
| `AzureAd:TenantId` | Entra tenant ID for DefaultAzureCredential |

Run config-sync to populate: `cd src/config-sync && dotnet run -- <key-vault-uri> [environment]`

## How to run locally

Implementation pending. Once built:

```bash
cd src/product-agent
dotnet run
```

## Architecture role

Product Agent is a specialist LLM agent for catalog browsing, promotions, recommendations, and sizing guides. The orchestrator-agent routes product-related intents here. It connects to both knowledge-mcp (semantic search) and crm-mcp (customer context) and uses Azure OpenAI for reasoning.
