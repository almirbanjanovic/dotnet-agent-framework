# CRM Agent
> CRM specialist agent using Azure OpenAI with CRM MCP tools for customer data operations.

Implementation pending. See docs/implementation-plan.md for details.

## Configuration

Required config keys (populated by config-sync from Key Vault):

| Key | Description |
|-----|-------------|
| `AzureOpenAi:Endpoint` | Azure OpenAI service endpoint |
| `AzureOpenAi:DeploymentName` | Model deployment name |
| `CrmMcp:BaseUrl` | CRM MCP server base URL |
| `AzureAd:TenantId` | Entra tenant ID for DefaultAzureCredential |

Run config-sync to populate: `cd src/config-sync && dotnet run -- <key-vault-uri> [environment]`

## How to run locally

Implementation pending. Once built:

```bash
cd src/crm-agent
dotnet run
```

## Architecture role

CRM Agent is a specialist LLM agent that handles customer-data requests — account lookups, order history, billing, support tickets, and policy questions. The orchestrator-agent routes CRM-related intents here. It connects to crm-mcp for CRM data tools and uses Azure OpenAI for reasoning.
