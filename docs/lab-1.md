# Lab 1 — Infrastructure, Validation & Data Seeding

This lab stands up the full Azure environment, validates connectivity, and seeds all databases with the Contoso Outdoors data.

## Prerequisites

- [Lab 0 — Bootstrap](lab-0.md) completed (accounts, tools, remote state backend)
- `az login` authenticated to the correct subscription

## What gets deployed

| Resource | Purpose |
|----------|---------|
| **Azure AI Foundry** | AI Services account with chat model (gpt-4.1) and embedding model (text-embedding-ada-002) |
| **Azure SQL Database** | Operational CRM data (Serverless tier — customers, orders, products, etc.) |
| **Cosmos DB** (×1 account) | Agents (state persistence) |
| **Azure AI Search** | Knowledge base search — indexes PDFs via integrated vectorization |
| **Event Grid** | Triggers AI Search indexer on new PDF uploads to blob storage |
| **Storage Account** | Product images + SharePoint documents blob storage — uploaded automatically during `terraform apply` |
| **AKS** | Kubernetes cluster for future lab deployments |
| **ACR** | Container image registry |
| **Key Vault** | Secrets management (endpoints, keys, deployment names) |
| **Managed Identities** | RBAC for backend, search, and kubelet workloads |

## Step 1 — Deploy infrastructure and seed data

`terraform apply` provisions all infrastructure **and** loads all data in a single step, regardless of whether you run it locally or via CI/CD:

| What | How |
|------|-----|
| Azure SQL Database, Cosmos DB (agents), AI Search, Event Grid, Storage, AKS, ACR, Key Vault | Terraform resources |
| Product images (`.png`) → `product-images` blob container | `azurerm_storage_blob` |
| SharePoint PDFs (`.pdf`) → `sharepoint-docs` blob container | `azurerm_storage_blob` |
| CRM data (CSV) → Azure SQL Database tables | `null_resource` + `local-exec` (runs `dotnet run src/seed-data`) |
| PDF text extraction, chunking, embedding → AI Search index | AI Search indexer (triggered automatically after PDFs land in blob) |

No separate seeding or indexing step is needed.

### Option A — Terminal

From `infra/terraform/`:

```bash
# Re-enable public access on the state storage account (disabled after bootstrap)
az storage account update \
  --name <your-storage-account> \
  --resource-group <your-resource-group> \
  --public-network-access Enabled

terraform init -reconfigure -backend-config=backend.hcl
terraform validate
terraform plan -var-file="terraform.tfvars"
terraform apply -auto-approve -var-file="terraform.tfvars"
```

### Option B — GitHub Actions

1. Go to **Actions → Terraform Plan, Approve, Apply** in your GitHub repository
2. Click **Run workflow**, select the `dev` environment, and confirm
3. The workflow runs in three stages:
   - **Plan** — authenticates via OIDC, runs `terraform plan`, and outputs the change set
   - **Manual approval** — creates a GitHub issue for review; an approver must approve before proceeding
   - **Apply** — runs `terraform apply -auto-approve` to provision all resources

All Terraform variables are read from the GitHub environment variables that `init` configured in Lab 0.

### Verify outputs

After deployment, note the Key Vault URI — you'll need it for the next step.

**Terminal** — from `infra/terraform/`:

```bash
terraform output keyvault_uri
```

**GitHub Actions** — find the Key Vault URI in the Azure portal: open your Key Vault resource → **Properties** → **Vault URI**.

## Step 2 — Configure app settings

The **config-sync** tool pulls secrets from Key Vault into `src/appsettings.json` so all projects can use them locally.

```bash
cd src/config-sync
dotnet restore
dotnet run -- <your-keyvault-uri>
```

For example:

```bash
dotnet run -- https://kv-agentic-ai-001.vault.azure.net/
```

This populates `src/appsettings.json` (gitignored) with configuration values:

| Key | Description |
|-----|-------------|
| `AZURE_OPENAI_ENDPOINT` | Azure OpenAI endpoint |
| `AZURE_OPENAI_DEPLOYMENT_NAME` | Chat model deployment name |
| `AZURE_OPENAI_API_KEY` | API key for authentication |
| `AZURE_OPENAI_EMBEDDING_DEPLOYMENT` | Embedding model name |
| `SQL_SERVER_FQDN` | Azure SQL Server FQDN |
| `SQL_DATABASE_NAME` | SQL database name |
| `SQL_ADMIN_LOGIN` | SQL admin username |
| `SQL_ADMIN_PASSWORD` | SQL admin password |
| `COSMOSDB_AGENTS_ENDPOINT` | Agents Cosmos DB endpoint |
| `COSMOSDB_AGENTS_KEY` | Agents Cosmos DB key |
| `COSMOSDB_AGENTS_DATABASE` | Agents database name |
| `STORAGE_IMAGES_ENDPOINT` | Product images blob endpoint |
| `STORAGE_IMAGES_ACCOUNT_NAME` | Product images storage account name |
| `STORAGE_IMAGES_CONTAINER` | Product images container name |
| `STORAGE_IMAGES_KEY` | Product images storage key |
| `SEARCH_ENDPOINT` | Azure AI Search endpoint |
| `SEARCH_ADMIN_KEY` | Azure AI Search admin key |
| `SEARCH_INDEX_NAME` | AI Search index name |

Expected output:

```
  ✓ AZURE-OPENAI-ENDPOINT → AZURE_OPENAI_ENDPOINT
  ✓ AZURE-OPENAI-API-KEY → AZURE_OPENAI_API_KEY
  ✓ AZURE-OPENAI-DEPLOYMENT-NAME → AZURE_OPENAI_DEPLOYMENT_NAME
  ...
  Wrote 17/17 secrets to .../src/appsettings.json
```

## Step 3 — Validate infrastructure

The **simple-agent** project creates a minimal AI agent that calls Azure OpenAI. This confirms your endpoint, deployment, and API key are all working.

```bash
cd src/simple-agent
dotnet restore
dotnet run
```

Expected output:

```
Using Azure OpenAI endpoint: https://aif-agentic-ai-centralus-gpt-4-1.openai.azure.com/
Deployment name: gpt-4.1

Agent response:
 Why did the developer break up with the cloud?
 Because the relationship had too many issues... and none of them were resolved!
```

If you see an error, check:
- `az login` is authenticated
- `src/appsettings.json` has non-empty values for `AZURE_OPENAI_ENDPOINT`, `AZURE_OPENAI_DEPLOYMENT_NAME`, and `AZURE_OPENAI_API_KEY`
- The AI Services deployment exists in the Azure portal

## Verification checklist

After completing all steps, verify:

- [ ] Infrastructure resources are visible in the Azure portal (or `terraform output` shows all endpoints)
- [ ] `src/appsettings.json` has 17 non-empty values
- [ ] `simple-agent` returns a joke from Azure OpenAI
- [ ] Azure SQL Database has 6 tables with data (Customers, Orders, etc.)
- [ ] Azure AI Search index has vectorized document chunks (check indexer status in Azure portal)
- [ ] Azure Blob Storage `product-images` container has 15 `.png` files
- [ ] Azure Blob Storage `sharepoint-docs` container has 12 `.pdf` files

## What's next

Lab 1 is complete. Your Azure environment is fully provisioned, validated, and seeded. Future labs will build on this foundation to create domain APIs, MCP servers, and agent orchestrations.
