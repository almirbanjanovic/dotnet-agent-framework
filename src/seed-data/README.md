# Seed Data
> CLI tool that seeds Cosmos DB CRM containers from CSV files in the `data/` folder.

Fully implemented — .NET console app that creates containers (if not exist) and upserts customer, order, and product data.

## Configuration

Required config keys (set via environment variables or a local `appsettings.json`):

| Key | Description |
|-----|-------------|
| `CosmosDb:Endpoint` | Cosmos DB CRM account endpoint |
| `CosmosDb:DatabaseName` | Database name (default: contoso-crm) |
| `AzureAd:TenantId` | Entra tenant ID for DefaultAzureCredential (optional) |

> **Note:** seed-data is not in the config-sync manifest. Copy `appsettings.{Environment}.json` from `src/crm-api/` (which has the same Cosmos DB keys), or set the values as environment variables.

Optional environment variable:

| Key | Description |
|-----|-------------|
| `CRM_DATA_PATH` | Override path to CSV data folder (defaults to `data/contoso-crm/` relative to repo root) |
| `ENTRA_MAPPING` | Semicolon-separated `customer_id=entra_oid` pairs for linking Entra users to customers |

## How to run locally

```bash
cd src/seed-data
dotnet run
```

The tool connects to Cosmos DB, creates containers if they don't exist, and upserts all records from CSV files. If `ENTRA_MAPPING` is set, it also patches customer documents with Entra user IDs.

## Architecture role

Seed-data is a one-time setup tool used during Lab 1 to populate the CRM Cosmos DB with sample data. It is typically run by the deploy script and is not deployed to AKS. After seeding, the CRM API serves this data to MCP servers and agents.
