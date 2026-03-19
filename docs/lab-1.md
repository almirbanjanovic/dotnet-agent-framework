# Lab 1 — Infrastructure, Validation & Data Seeding

This lab stands up the full Azure environment, validates connectivity, and seeds all databases with the Contoso Outdoors data.

## Prerequisites

- [Lab 0 — Bootstrap](lab-0.md) completed (accounts, tools, remote state backend)
- `az login` authenticated to the correct subscription

## What gets deployed

| Resource | Purpose |
| ---------- | --------- |
| **Azure AI Foundry** | AI Services account with chat model (gpt-4.1) and embedding model (text-embedding-ada-002) |
| **Cosmos DB** (×2 accounts) | CRM (operational data) + Agents (state persistence) |
| **Azure AI Search** | Knowledge base search — indexes PDFs via Knowledge Source API (Standard tier, semantic ranker) |
| **Storage Account** | Product images + SharePoint documents blob storage — uploaded automatically during `terraform apply` |
| **AKS** | Kubernetes cluster for future lab deployments |
| **ACR** | Container image registry |
| **Key Vault** | Secrets management (endpoints, keys, deployment names) |
| **Managed Identities** | 8 per-service identities with least-privilege RBAC (bff, crm-api, crm-mcp, know-mcp, crm-agent, prod-agent, orch-agent, kubelet) |

## Step 1 — Deploy infrastructure and seed data

The deploy script provisions all infrastructure and seeds data in a single run:

| What | How |
| ------ | ----- |
| Cosmos DB (CRM + agents), AI Search, Storage, AKS, ACR, Key Vault | Terraform resources (phases 1–5) |
| Product images (`.png`) → `product-images` blob container | `azurerm_storage_blob` (during terraform apply) |
| SharePoint PDFs (`.pdf`) → `sharepoint-docs` blob container | `azurerm_storage_blob` (during terraform apply) |
| PDF text extraction, chunking, embedding → AI Search index | AI Search Knowledge Source auto-generates indexer (5-min schedule) |
| CRM data (CSV) → Cosmos DB containers | Deploy script phase 6 (runs `dotnet run` for seed-data) |
| Entra user IDs → Cosmos DB Customers container | Deploy script phase 7 (reads object IDs from Key Vault, updates Cosmos DB) |

### Option A — Terminal

From the `infra/` directory:

```powershell
# PowerShell
./deploy.ps1
```

```bash
# Bash / WSL / macOS
chmod +x deploy.sh
./deploy.sh
```

The script performs 9 phases with a confirmation gate between each:

| Phase | What it does |
| :-----: | ------------- |
| **1** | Re-enables public access on the state storage account (disabled after bootstrap) |
| **2** | `terraform init` with remote backend, imports existing Entra users into Terraform state |
| **3** | `terraform validate` to check configuration syntax |
| **4** | `terraform plan` to preview all changes |
| **5** | `terraform apply` to provision resources and upload blobs |
| **6** | Seed CRM data — runs seed-data tool inside AKS pod with workload identity (RBAC-based, no keys) |
| **7** | Link Entra users to Customers — reads Entra object IDs from Key Vault, updates `entra_id` in Cosmos DB |
| **8** | Config sync — pulls secrets from Key Vault into `src/appsettings.json` (runs while firewalls are open) |
| **9** | Validate — runs `simple-agent` to verify Azure OpenAI connectivity (runs while firewalls are open) |

Before Phase 1, the script runs a **pre-flight check** that purges soft-deleted Key Vaults and Cognitive Services accounts from previous runs. Azure retains these in a soft-deleted state which blocks re-creation with the same name. Key Vault purges use `--no-wait` since they can take several minutes.

If `terraform apply` fails, the script runs a **post-failure diagnostic** that lists all deny-effect Azure Policy assignments (including parameterized policies in initiatives) across the subscription and resource group. This helps identify `RequestDisallowedByPolicy` errors quickly.

### Option B — GitHub Actions

1. Go to **Actions → Terraform Plan, Approve, Apply** in your GitHub repository
2. Click **Run workflow**, select the `dev` environment, and confirm
3. The workflow runs in four stages:
   - **Plan** — authenticates via OIDC, imports existing Entra users into state, runs `terraform plan`, and outputs the change set
   - **Manual approval** — creates a GitHub issue for review; an approver must approve before proceeding
   - **Purge soft-deleted** — purges soft-deleted Cognitive Services and Key Vault resources that would block re-creation
   - **Apply** — runs `terraform apply -auto-approve` to provision all resources. On failure, runs a policy diagnostic listing any deny-effect policies
   - **Seed Data** — seeds CRM tables from CSV via `dotnet run`, then links Entra user object IDs to the Customers table

All Terraform variables are read from the GitHub environment variables that `init` configured in Lab 0.

## Step 2 — Verify results

Steps 2 and 3 are now automated as part of the deploy script (Phases 8 and 9):

- **Phase 8 (Config Sync)** — The deploy script automatically pulls secrets from Key Vault into `src/appsettings.json` while firewalls are open. You'll see the sync output during the deploy.
- **Phase 9 (Validate)** — The deploy script automatically runs `simple-agent` to verify Azure OpenAI connectivity while firewalls are open.

> **Running manually:** If you need to re-run config-sync or simple-agent after deployment, you must first open the resource firewalls (add your IP to Key Vault and Cognitive Services network rules), since the deploy script closes all firewalls when it finishes.

## Verification checklist

After completing all steps, verify:

- [ ] Infrastructure resources are visible in the Azure portal (or `terraform output` shows all endpoints)
- [ ] `src/appsettings.json` has 15 non-empty values
- [ ] `simple-agent` returns a joke from Azure OpenAI
- [ ] Cosmos DB CRM account has 6 containers with data (Customers, Orders, etc.)
- [ ] Azure AI Search index has vectorized document chunks (check indexer status in Azure portal)
- [ ] Azure Blob Storage `product-images` container has 15 `.png` files
- [ ] Azure Blob Storage `sharepoint-docs` container has 12 `.pdf` files

## What's next

Lab 1 is complete. Your Azure environment is fully provisioned, validated, and seeded. Future labs will build on this foundation to create .NET Minimal APIs, MCP servers, and agent orchestrations.
