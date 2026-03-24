# Knowledge MCP Server
> MCP Server providing search_knowledge_base tool backed by Azure AI Search.

Implementation pending. See docs/implementation-plan.md for details.

## Configuration

Required config keys (populated by config-sync from Key Vault):

| Key | Description |
|-----|-------------|
| `Search:Endpoint` | Azure AI Search endpoint |
| `Search:IndexName` | Search index name |
| `Storage:ImagesEndpoint` | Blob storage endpoint for product images |
| `Storage:ImagesAccountName` | Storage account name |
| `Storage:ImagesContainer` | Blob container for images |
| `AzureAd:TenantId` | Entra tenant ID for DefaultAzureCredential |

Run config-sync to populate: `cd src/config-sync && dotnet run -- <key-vault-uri> [environment]`
