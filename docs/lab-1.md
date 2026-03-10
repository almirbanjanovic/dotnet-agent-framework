# Lab 1 — Infrastructure, Validation & Data Seeding

This lab stands up the full Azure environment, validates connectivity, and seeds all databases with the Contoso Outdoors data.

## Prerequisites

- [.NET SDK 9.0+](https://dotnet.microsoft.com/download)
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) — `az login` completed
- [Terraform >= 1.14.6](https://developer.hashicorp.com/terraform/install)
- An Azure subscription with permissions to create resources

## What gets deployed

| Resource | Purpose |
|----------|---------|
| **Azure AI Foundry** | AI Services account with chat model (gpt-4.1) and embedding model (text-embedding-ada-002) |
| **Cosmos DB** (×3 accounts) | Operational (CRM data), Knowledge (RAG vector store), Agents (state persistence) |
| **Storage Account** | Product images blob storage — images uploaded automatically during `terraform apply` |
| **AKS** | Kubernetes cluster for future lab deployments |
| **ACR** | Container image registry |
| **Key Vault** | Secrets management (endpoints, keys, deployment names) |
| **Managed Identities** | RBAC for backend and kubelet workloads |

## Step 1 — Deploy infrastructure

Full infrastructure setup details are in [infra/README.md](../infra/README.md). The summary:

### 1a. Create local config files

Create `infra/terraform/backend.hcl` (gitignored):

```hcl
resource_group_name  = "rg-agentic-ai-centralus"
storage_account_name = "stagenticaicentralus"
container_name       = "tfstate"
key                  = "agentic-ai.tfstate"
```

Create `infra/terraform/terraform.tfvars` (gitignored) — see [infra/README.md](../infra/README.md) for the full example with all variables.

### 1b. Bootstrap the Terraform backend

This only needs to run once per environment:

```powershell
cd infra
./init-backend.ps1
```

### 1c. Deploy

```bash
cd infra/terraform
terraform init -backend-config=backend.hcl
terraform plan -var-file="terraform.tfvars"
terraform apply -auto-approve -var-file="terraform.tfvars"
```

This provisions all Azure resources and uploads the 15 product images to blob storage.

### 1d. Verify outputs

After `terraform apply`, note the Key Vault URI:

```bash
terraform output keyvault_uri
```

You'll need this for the next step.

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

This populates `src/appsettings.json` (gitignored) with 17 configuration values:

| Key | Description |
|-----|-------------|
| `AZURE_OPENAI_ENDPOINT` | Azure OpenAI endpoint |
| `AZURE_OPENAI_DEPLOYMENT_NAME` | Chat model deployment name |
| `AZURE_OPENAI_API_KEY` | API key for authentication |
| `AZURE_OPENAI_EMBEDDING_DEPLOYMENT` | Embedding model name |
| `COSMOSDB_OPERATIONAL_ENDPOINT` | Operational Cosmos DB endpoint |
| `COSMOSDB_OPERATIONAL_KEY` | Operational Cosmos DB key |
| `COSMOSDB_OPERATIONAL_DATABASE` | Operational database name |
| `COSMOSDB_KNOWLEDGE_ENDPOINT` | Knowledge (RAG) Cosmos DB endpoint |
| `COSMOSDB_KNOWLEDGE_KEY` | Knowledge Cosmos DB key |
| `COSMOSDB_KNOWLEDGE_DATABASE` | Knowledge database name |
| `COSMOSDB_AGENTS_ENDPOINT` | Agents Cosmos DB endpoint |
| `COSMOSDB_AGENTS_KEY` | Agents Cosmos DB key |
| `COSMOSDB_AGENTS_DATABASE` | Agents database name |
| `STORAGE_IMAGES_ENDPOINT` | Product images blob endpoint |
| `STORAGE_IMAGES_ACCOUNT_NAME` | Product images storage account name |
| `STORAGE_IMAGES_CONTAINER` | Product images container name |
| `STORAGE_IMAGES_KEY` | Product images storage key |

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

## Step 4 — Generate PDFs

The SharePoint data seeder reads PDF files, but the repo only stores editable `.txt` source files. The **generate-pdfs** tool converts them:

```bash
cd data/contoso-sharepoint/generate-pdfs
dotnet restore
dotnet run
```

Expected output:

```
Found 12 text files to convert.

  ✓ guides\backpack-fitting-guide.pdf
  ✓ guides\boot-sizing-guide.pdf
  ✓ guides\gear-care-and-maintenance.pdf
  ✓ guides\layering-guide.pdf
  ✓ guides\tent-selection-guide.pdf
  ✓ policies\loyalty-program-terms.pdf
  ✓ policies\price-match-policy.pdf
  ✓ policies\return-and-refund-policy.pdf
  ✓ policies\warranty-policy.pdf
  ✓ procedures\exchanging-a-product.pdf
  ✓ procedures\filing-a-warranty-claim.pdf
  ✓ procedures\processing-a-return.pdf

Done. Generated 12 PDF files.
```

## Step 5 — Seed Cosmos DB

The **seed-data** tool performs two operations:

1. **Phase 1 — CRM data** → Parses 6 CSV files from `data/contoso-crm/` and upserts them into the Operational Cosmos DB containers (Customers, Orders, OrderItems, Products, Promotions, SupportTickets)
2. **Phase 2 — SharePoint documents (RAG)** → Reads the generated PDFs, extracts text, chunks it, generates vector embeddings via `text-embedding-ada-002`, and upserts into the Knowledge Cosmos DB `KnowledgeDocuments` container

```bash
cd src/seed-data
dotnet restore
dotnet run
```

Expected output:

```
═══════════════════════════════════════════════════════════
  Contoso Outdoors — Cosmos DB Seed Tool
═══════════════════════════════════════════════════════════

  OpenAI endpoint:         https://aif-agentic-ai-centralus-gpt-4-1.openai.azure.com/
  Embedding deployment:    text-embedding-ada-002
  Operational endpoint:    https://cosmos-dotnetagent-operational-...
  Knowledge endpoint:      https://cosmos-dotnetagent-knowledge-...

───────────────────────────────────────────────────────────
  Phase 1: Seeding structured data (CRM → containers)
───────────────────────────────────────────────────────────

  ✓ customers.csv → Customers (8 documents)
  ✓ orders.csv → Orders (...)
  ...

───────────────────────────────────────────────────────────
  Phase 2: Vectorizing documents (SharePoint → RAG store)
───────────────────────────────────────────────────────────

  ✓ policies/return-and-refund-policy.pdf: 3 chunk(s) embedded and upserted
  ✓ guides/boot-sizing-guide.pdf: 2 chunk(s) embedded and upserted
  ...

═══════════════════════════════════════════════════════════
  Seeding complete!
═══════════════════════════════════════════════════════════
```

If Phase 2 shows 0 PDF files, go back to Step 4 and run `generate-pdfs` first.

## Verification checklist

After completing all steps, verify:

- [ ] `terraform output` shows all endpoints, keys, and names
- [ ] `src/appsettings.json` has 17 non-empty values
- [ ] `simple-agent` returns a joke from Azure OpenAI
- [ ] 12 PDFs exist in `data/contoso-sharepoint/` subdirectories
- [ ] Cosmos DB Operational account has 6 containers with data
- [ ] Cosmos DB Knowledge account has `KnowledgeDocuments` with vectorized chunks
- [ ] Azure Blob Storage `product-images` container has 15 `.png` files

## What's next

Lab 1 is complete. Your Azure environment is fully provisioned, validated, and seeded. Future labs will build on this foundation to create domain APIs, MCP servers, and agent orchestrations.
