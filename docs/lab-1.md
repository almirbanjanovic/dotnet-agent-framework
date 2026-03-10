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

## Step 4 — Seed Cosmos DB

### Why seed data?

AI agents can only work with data they can access. The chat model (gpt-4.1) has no knowledge of Contoso Outdoors — it doesn't know your customers, orders, products, or policies. Seeding loads all of this data into Cosmos DB so agents can query it at runtime via MCP tools.

### Two types of data, two strategies

**Structured data (CRM)** — Customer records, orders, products, and promotions live as JSON documents in Cosmos DB. Agents query this data using standard SQL-like queries (e.g., "find all orders for customer 101"). No AI processing is needed during seeding — the data is already in a queryable format.

**Unstructured data (SharePoint documents)** — Policy documents, procedures, and guides are free-form text. You can't run a SQL query against a paragraph of text to answer *"what is your return policy?"*. This is where **RAG (Retrieval-Augmented Generation)** and the **embedding model** come in.

### What the embedding model does

The embedding model (`text-embedding-ada-002`) converts text into **vectors** — arrays of 1,536 floating-point numbers that capture the *meaning* of the text. Two texts that are semantically similar produce vectors that are close together in vector space, even if they share no exact words.

During seeding, each document is:
1. **Extracted** — text is pulled from the PDF
2. **Chunked** — split into ~500-token segments (the embedding model has a token limit)
3. **Embedded** — each chunk is sent to `text-embedding-ada-002`, which returns a 1,536-dimension float array
4. **Stored** — the chunk text + its vector are saved to the `KnowledgeDocuments` container in Cosmos DB

At query time, when a user asks *"what is your return policy?"*, the same embedding model converts the question into a vector, and Cosmos DB's `VectorDistance` function finds the chunks with the most similar vectors — semantic search by meaning, not keyword matching. These chunks are then passed to the chat model as context so it can generate a grounded, accurate answer.

### Running the seed tool

The **seed-data** tool performs both operations:

1. **Phase 1 — CRM data** → Parses 6 CSV files from `data/contoso-crm/` and upserts them into the Operational Cosmos DB containers (Customers, Orders, OrderItems, Products, Promotions, SupportTickets)
2. **Phase 2 — SharePoint documents (RAG)** → Reads PDFs from `data/contoso-sharepoint/`, extracts text, chunks it, generates vector embeddings via `text-embedding-ada-002`, and upserts into the Knowledge Cosmos DB `KnowledgeDocuments` container

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
