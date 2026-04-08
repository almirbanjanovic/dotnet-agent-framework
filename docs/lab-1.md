# Lab 1 — Infrastructure, Validation & Data Seeding

This lab stands up the full Azure environment, validates connectivity, and seeds all databases with the Contoso Outdoors data.

## Prerequisites

- [Lab 0 — Bootstrap](lab-0.md) completed (accounts, tools, remote state backend)
- `az login` authenticated to the correct subscription

## What gets deployed

| Resource | Purpose |
| ---------- | --------- |
| **Azure AI Foundry** | AI Services account with chat model (gpt-4.1) and embedding model (text-embedding-3-small) |
| **Cosmos DB** (×2 accounts) | CRM (operational data) + Agents (state persistence) |
| **Azure AI Search** | Knowledge base search — indexes PDFs via Knowledge Source API (Standard tier, semantic ranker) |
| **Storage Account** | Product images + SharePoint documents blob storage — uploaded automatically during `terraform apply` |
| **AKS** | Kubernetes cluster for future lab deployments |
| **ACR** | Container image registry |
| **Key Vault** | Secrets management (endpoints, deployment names, identity client IDs — no API keys, RBAC only) |
| **Managed Identities** | 5 non-agent identities (bff, crm-api, crm-mcp, know-mcp, kubelet) + 3 agent identities (CRM Agent, Product Agent, Orchestrator Agent via Entra Agent ID) — all with least-privilege RBAC |

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

The script performs 8 phases (0-7) with a confirmation gate between each:

| Phase | What it does |
| :-----: | ------------- |
| **0** | Agent Identity SP -- creates or finds service principal for Terraform's msgraph provider (needed for Entra Agent ID API) |
| **1** | Opens resource firewalls (adds deployer IP to Key Vault, Storage, Cosmos DB, AI Services, AI Search) |
| **2** | `terraform init` with remote backend |
| **3** | `terraform validate` to check configuration syntax |
| **4** | `terraform plan` to preview all changes |
| **5** | `terraform apply` to provision resources and upload blobs |
| **6** | Seed CRM data -- runs seed-data tool directly via `dotnet run` with DefaultAzureCredential (firewalls are open, deployer has Cosmos DB RBAC) |
| **7** | Link Entra users to Customers — reads Entra object IDs from Key Vault, updates `entra_id` in Cosmos DB |

If `terraform apply` fails, the script runs a **post-failure diagnostic** that lists all deny-effect Azure Policy assignments (including parameterized policies in initiatives) across the subscription and resource group. This helps identify `RequestDisallowedByPolicy` errors quickly.

### Option B — GitHub Actions

1. Go to **Actions → Terraform Plan, Approve, Apply** in your GitHub repository
2. Click **Run workflow**, select the `dev` environment, and confirm
3. The workflow runs in four stages:
   - **Plan** -- authenticates via OIDC, runs `terraform plan`, and outputs the change set
   - **Manual approval** — creates a GitHub issue for review; an approver must approve before proceeding
   - **Purge soft-deleted** — purges soft-deleted Cognitive Services and Key Vault resources that would block re-creation
   - **Apply** — runs `terraform apply -auto-approve` to provision all resources. On failure, runs a policy diagnostic listing any deny-effect policies
   - **Seed Data** — seeds CRM tables from CSV via `dotnet run`, then links Entra user object IDs to the Customers table

All Terraform variables are read from the GitHub environment variables that `init` configured in Lab 0.

## Step 2 — Configure app settings

The **config-sync** tool pulls secrets from Key Vault into per-component `appsettings.{Environment}.json` files so each project can use them locally. Since the deploy script closes resource firewalls when it finishes, you need to temporarily open the Key Vault firewall first.

Get your deployer IP and Key Vault name:

```powershell
# PowerShell
$DEPLOYER_IP = (Invoke-RestMethod https://api.ipify.org)
$RG = "<your-resource-group>"   # e.g. rg-dotnetagent-dev-eastus2
$KV = (az keyvault list --resource-group $RG --query "[0].name" -o tsv)
```

```bash
# Bash / WSL / macOS
DEPLOYER_IP=$(curl -s https://api.ipify.org)
RG="<your-resource-group>"   # e.g. rg-dotnetagent-dev-eastus2
KV=$(az keyvault list --resource-group "$RG" --query "[0].name" -o tsv)
```

Open Key Vault firewall, run config-sync, then close it. Run from the **repository root**:

```powershell
# PowerShell — Open
az keyvault network-rule add --name $KV --resource-group $RG --ip-address "$DEPLOYER_IP/32"
Start-Sleep 15

# Sync
Push-Location src/config-sync
$kvUri = (az keyvault show --name $KV --resource-group $RG --query properties.vaultUri -o tsv)
dotnet run -- $kvUri Development
Pop-Location

# Close
az keyvault network-rule remove --name $KV --resource-group $RG --ip-address "$DEPLOYER_IP/32"
```

```bash
# Bash / WSL / macOS — Open
az keyvault network-rule add --name "$KV" --resource-group "$RG" --ip-address "$DEPLOYER_IP/32"
sleep 15

# Sync
pushd src/config-sync
dotnet run -- $(az keyvault show --name "$KV" --resource-group "$RG" --query properties.vaultUri -o tsv) Development
popd

# Close
az keyvault network-rule remove --name "$KV" --resource-group "$RG" --ip-address "$DEPLOYER_IP/32"
```

Expected output:

```text
═══════════════════════════════════════════════════════════
  Config Sync — Key Vault → per-component appsettings.Development.json
═══════════════════════════════════════════════════════════

  Key Vault:     <your-keyvault-uri>
  Environment:   Development
  Auth:          DefaultAzureCredential (az login)

  Fetching secrets from Key Vault...

  ✓ AzureAd--BffClientId
  ✓ AzureAd--TenantId
  ✓ Foundry--DeploymentName
  ✓ Foundry--Endpoint
  ...

  Fetched 21/21 secrets

  Writing per-component appsettings.Development.json files...

  ✓ crm-api/appsettings.Development.json (3 keys)
  ✓ crm-mcp/appsettings.Development.json (2 keys)
  ✓ knowledge-mcp/appsettings.Development.json (6 keys)
  ✓ crm-agent/appsettings.Development.json (4 keys)
  ✓ product-agent/appsettings.Development.json (5 keys)
  ✓ orchestrator-agent/appsettings.Development.json (5 keys)
  ✓ bff-api/appsettings.Development.json (9 keys)
  ✓ blazor-ui/appsettings.Development.json (3 keys)

═══════════════════════════════════════════════════════════
  Done! Each component has its own appsettings.Development.json.
═══════════════════════════════════════════════════════════
```

## Step 3 — Validate infrastructure

The **simple-agent** project creates a minimal AI agent that calls AI Foundry. This confirms your endpoint, deployment, and credentials are all working. Since AI Foundry (Cognitive Services) has a firewall, you need to temporarily open it.

```powershell
# PowerShell
$FOUNDRY = (az cognitiveservices account list --resource-group $RG --query "[0].name" -o tsv)
```

```bash
# Bash / WSL / macOS
FOUNDRY=$(az cognitiveservices account list --resource-group "$RG" --query "[0].name" -o tsv)
```

Open the AI Foundry firewall, run simple-agent, then close it. Run from the **repository root**:

```powershell
# PowerShell — Open (no /32 suffix — Foundry doesn't accept CIDR notation)
az cognitiveservices account network-rule add --resource-group $RG --name $FOUNDRY --ip-address $DEPLOYER_IP
Start-Sleep 15

# Validate
Push-Location src/simple-agent
dotnet run
Pop-Location

# Close
az cognitiveservices account network-rule remove --resource-group $RG --name $FOUNDRY --ip-address $DEPLOYER_IP
```

```bash
# Bash / WSL / macOS — Open (no /32 suffix — Foundry doesn't accept CIDR notation)
az cognitiveservices account network-rule add --resource-group "$RG" --name "$FOUNDRY" --ip-address "$DEPLOYER_IP"
sleep 15

# Validate
pushd src/simple-agent
dotnet run
popd

# Close
az cognitiveservices account network-rule remove --resource-group "$RG" --name "$FOUNDRY" --ip-address "$DEPLOYER_IP"
```

Expected output (the joke will differ on each run — it's AI-generated):

```text
Using AI Foundry endpoint: https://<your-foundry-endpoint>/
Deployment name: gpt-4.1

Agent response:
 Why did the developer break up with the cloud?
 Because the relationship had too many issues... and none of them were resolved!
```

If you see an error, check:

- `az login` is authenticated
- `az login` is authenticated with an account that has the **Cognitive Services OpenAI User** role on the AI Foundry account
- `Foundry:Endpoint` and `Foundry:DeploymentName` are set in `src/simple-agent/appsettings.Development.json` or via environment variables (`Foundry__Endpoint`, `Foundry__DeploymentName`)
- The AI Foundry deployment exists in the Azure portal

## Verification checklist

After completing all steps, verify:

- [ ] Infrastructure resources are visible in the Azure portal (or `terraform output` shows all endpoints)
- [ ] 9 per-component `appsettings.Development.json` files exist under `src/` with non-empty values
- [ ] `simple-agent` returns a joke from AI Foundry
- [ ] Cosmos DB CRM account has 6 containers with data (Customers, Orders, etc.)
- [ ] Azure AI Search index has vectorized document chunks (check indexer status in Azure portal)
- [ ] Azure Blob Storage `product-images` container has 15 `.png` files
- [ ] Azure Blob Storage `sharepoint-docs` container has 12 `.pdf` files

## What's next

Lab 1 is complete. Your Azure environment is fully provisioned, validated, and seeded. Future labs will build on this foundation to create .NET Minimal APIs, MCP servers, and agent orchestrations.
