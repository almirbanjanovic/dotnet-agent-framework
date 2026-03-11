# Lab 1 — Infrastructure, Validation & Data Seeding

This lab stands up the full Azure environment, validates connectivity, and seeds all databases with the Contoso Outdoors data.

## Prerequisites

- [Lab 0 — Bootstrap](lab-0.md) completed (accounts, tools, `terraform.tfvars`, state backend)
- `az login` authenticated to the correct subscription

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

### Option A — Local

From `infra/terraform/`:

```bash
# Ensure the backend storage account is reachable
az storage account update \
  --name <your-storage-account> \
  --resource-group <your-resource-group> \
  --public-network-access Enabled

terraform init -reconfigure -backend-config=backend.hcl
terraform validate
terraform plan -var-file="terraform.tfvars"
terraform apply -auto-approve -var-file="terraform.tfvars"
```

This provisions all Azure resources and uploads the 15 product images to blob storage.

### Option B — GitHub Actions

> Requires [Lab 0 Step 1](lab-0.md#step-1--set-up-entra-and-github-for-cicd) (Entra + GitHub setup) to be completed first.

1. Go to **Actions → Terraform Plan, Approve, Apply** in your GitHub repository
2. Click **Run workflow**, select the `dev` environment, and confirm
3. The workflow runs in three stages:
   - **Plan** — authenticates via OIDC, runs `terraform plan`, and outputs the change set
   - **Manual approval** — creates a GitHub issue for review; an approver must approve before proceeding
   - **Apply** — runs `terraform apply -auto-approve` to provision all resources

All Terraform variables are read from the GitHub environment variables that `init-github` configured in Lab 0.

### Verify outputs

After `terraform apply` (either option), note the Key Vault URI:

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

## Step 4 — Seed Cosmos DB

The chat model (gpt-4.1) has no knowledge of Contoso Outdoors. Seeding loads customer, order, product, and policy data into Cosmos DB so agents can query it at runtime via MCP tools.

The seed tool performs two operations:

1. **CRM data** — Parses CSV files from `data/contoso-crm/` and upserts them as JSON documents into the Operational Cosmos DB (standard SQL queries, no vectorization)
2. **SharePoint documents (RAG)** — Extracts text from PDFs in `data/contoso-sharepoint/`, chunks it, generates vector embeddings via `text-embedding-ada-002`, and upserts into the Knowledge Cosmos DB for semantic search

For details on RAG, the embedding model, and the data architecture, see [data/README.md](../data/README.md).

### Running the seed tool

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

  ✓ Connected to operational database 'contoso-outdoors'
  ✓ Connected to knowledge database 'knowledge'

───────────────────────────────────────────────────────────
  Phase 1: Seeding structured data (CRM → containers)
───────────────────────────────────────────────────────────

  Loads customer, order, and product data from CSV files
  into Cosmos DB containers. Agents query this data using
  standard SQL queries via MCP tools at runtime.

  ✓ customers.csv → Customers (8 documents)
  ✓ orders.csv → Orders (...)
  ...

───────────────────────────────────────────────────────────
  Phase 2: Vectorizing documents (SharePoint → RAG store)
───────────────────────────────────────────────────────────

  Extracts text from PDFs, chunks it into ~500-token segments,
  generates 1536-dim vector embeddings via the embedding model,
  and stores each chunk + vector in KnowledgeDocuments.
  This enables semantic search (RAG) at query time — agents
  find relevant documents by meaning, not keyword matching.

  ✓ policies/return-and-refund-policy.pdf: 3 chunk(s) embedded and upserted
  ✓ guides/boot-sizing-guide.pdf: 2 chunk(s) embedded and upserted
  ...

═══════════════════════════════════════════════════════════
  Seeding complete!
═══════════════════════════════════════════════════════════
```

## Verification checklist

After completing all steps, verify:

- [ ] `terraform output` shows all endpoints, keys, and names
- [ ] `src/appsettings.json` has 17 non-empty values
- [ ] `simple-agent` returns a joke from Azure OpenAI
- [ ] Cosmos DB Operational account has 6 containers with data
- [ ] Cosmos DB Knowledge account has `KnowledgeDocuments` with vectorized chunks
- [ ] Azure Blob Storage `product-images` container has 15 `.png` files

## What's next

Lab 1 is complete. Your Azure environment is fully provisioned, validated, and seeded. Future labs will build on this foundation to create domain APIs, MCP servers, and agent orchestrations.
