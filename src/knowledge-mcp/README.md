# Knowledge MCP Server
> MCP Server providing search_knowledge_base tool backed by Azure AI Search.

Knowledge MCP Server exposes semantic search over policies, guides, and procedures using Azure AI Search or an in-memory embedding store for local development.

## Configuration

Required config keys (populated by config-sync from Key Vault):

| Key | Description |
|-----|-------------|
| `Search:Endpoint` | Azure AI Search endpoint |
| `Search:IndexName` | Search index name |
| `Foundry:ProjectEndpoint` | AI Foundry project endpoint (used to discover the Azure OpenAI connection for embeddings in `DataMode=InMemory`) |
| `Foundry:EmbeddingDeploymentName` | Embedding model deployment name (`DataMode=InMemory` only) |
| `AzureAd:TenantId` | Entra tenant ID for DefaultAzureCredential |
| `DataMode` | `AzureSearch` (default) or `InMemory` for local dev |

Run config-sync to populate: `cd src/config-sync && dotnet run -- <key-vault-uri> [environment]`

## How to run locally

Run the MCP server:

```bash
cd src/knowledge-mcp
dotnet run
```

## Architecture role

Knowledge MCP Server provides semantic search over the product knowledge base via Azure AI Search. Agents (primarily product-agent) call the `search_knowledge_base` tool to retrieve catalog details, sizing guides, and promotional content.
