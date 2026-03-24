# CRM API
> REST API backed by Cosmos DB for customer, contact, opportunity, and interaction data.

Implementation details in docs/implementation-plan.md.

## Configuration

Required config keys (populated by config-sync from Key Vault):

| Key | Description |
|-----|-------------|
| `CosmosDb:Endpoint` | Cosmos DB CRM account endpoint |
| `CosmosDb:DatabaseName` | Database name (default: contoso-crm) |
| `AzureAd:TenantId` | Entra tenant ID for DefaultAzureCredential |

Run config-sync to populate: `cd src/config-sync && dotnet run -- <key-vault-uri>`
