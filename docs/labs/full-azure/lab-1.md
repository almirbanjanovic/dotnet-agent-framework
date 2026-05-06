# Lab 1 — Full Infrastructure & Data Seeding (Full Azure Track)

> **Track:** Full Azure — production-shaped: AKS pods, Cosmos DB, AI Search, GitHub OIDC.
> Looking for the Local Track instead? See [`../local/lab-1.md`](../local/lab-1.md).

This track stands up the full Azure environment, validates connectivity, and seeds all databases with the Contoso Outdoors data.

## Prerequisites

- [Lab 0](lab-0.md) completed (accounts, tools, remote state backend)
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
  ✓ Foundry--ProjectEndpoint
  ...

  Fetched 21/21 secrets

  Writing per-component appsettings.Development.json files...

  ✓ crm-api/appsettings.Development.json (3 keys)
  ✓ crm-mcp/appsettings.Development.json (2 keys)
  ✓ knowledge-mcp/appsettings.Development.json (6 keys)
  ✓ crm-agent/appsettings.Development.json (5 keys)
  ✓ product-agent/appsettings.Development.json (5 keys)
  ✓ orchestrator-agent/appsettings.Development.json (5 keys)
  ✓ bff-api/appsettings.Development.json (10 keys)
  ✓ blazor-ui/appsettings.Development.json (3 keys)
  ✓ simple-agent/appsettings.Development.json (3 keys)

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
Using AI Foundry project endpoint: https://<your-foundry-endpoint>/
Model deployment: gpt-4.1
Auth mode: DefaultAzureCredential (Tenant: <your-tenant-id>)

Agent response:
 Why did the developer break up with the cloud?
 Because the relationship had too many issues... and none of them were resolved!
```

If you see an error, check:

- `az login` is authenticated
- `az login` is authenticated with an account that has the **Cognitive Services OpenAI User** role on the AI Foundry account
- `Foundry:ProjectEndpoint` and `Foundry:DeploymentName` are set in `src/simple-agent/appsettings.Development.json` or via environment variables (`Foundry__ProjectEndpoint`, `Foundry__DeploymentName`)
- The AI Foundry deployment exists in the Azure portal

### How `simple-agent` works (your first Microsoft Agent Framework call)

If you're new to the Microsoft Agent Framework, [src/simple-agent/Program.cs](../../../src/simple-agent/Program.cs) is the smallest possible "hello world". The whole file is ~25 lines of code; here are the three calls that matter:

```csharp
// 1. DefaultAzureCredential — the only auth path in this repo. Walks
//    az CLI → Visual Studio → Managed Identity → Workload Identity in
//    order. Locally it picks up your `az login` token; in AKS it picks
//    up the workload identity assigned to the pod.
var credential = new DefaultAzureCredential(
    new DefaultAzureCredentialOptions { TenantId = tenantId });

// 2. AIProjectClient is the Agent Framework's typed client over your
//    Foundry project. `AsAIAgent(...)` adapts it into a runnable agent.
AIAgent agent = new AIProjectClient(new Uri(endpoint), credential)
    .AsAIAgent(
        model: deploymentName,
        instructions: "You are a helpful and funny assistant who tells short jokes.",
        name: "Joker");

// 3. RunAsync sends the prompt + system instructions to the model and
//    returns when the model is done.
var result = await agent.RunAsync("Tell me a joke about the cloud.");
```

The same three primitives — `DefaultAzureCredential` → `AIProjectClient.AsAIAgent(...)` → `agent.RunAsync(...)` — are how *every* agent in this repo is built. The richer agents in [Lab 2](lab-2.md) just add **tools** (MCP clients) and **multi-turn history** to the same `AsAIAgent` call.

## Step 4 — Deploy the 8 services to AKS

Lab 1 has provisioned AKS, ACR, and the workload identities, but the Pods themselves aren't running yet. The simplest way to deploy all 8 services in one go is the **Deploy All Services** workflow. It runs build + test for every service, pushes one image per service to ACR, then `helm upgrade --install`s each chart in dependency-aware tiers (CRM API + Knowledge MCP → CRM MCP → CRM/Product agents → Orchestrator → BFF + Blazor UI).

> **One-time prerequisite — install the Application Gateway for Containers (AGC) ALB Controller.** Terraform provisions the AGC resource (load balancer, frontend FQDN, TLS cert) but does **not** install the in-cluster controller that programs it. Without the controller, the Blazor UI is reachable inside the cluster but **not** from the internet. Follow the upstream Helm install guide once per cluster:
> [Deploy Application Gateway for Containers ALB Controller (Helm BYO)](https://learn.microsoft.com/en-us/azure/application-gateway/for-containers/quickstart-deploy-application-gateway-for-containers-alb-controller-helm)
>
> Then apply the Gateway + HTTPRoute manifests this repo ships:
>
> ```bash
> az aks get-credentials --resource-group "$RG" --name "aks-${BASE_NAME}-${ENVIRONMENT}-${LOCATION}" --overwrite-existing
> AGC_FRONTEND_ID=$(terraform -chdir=infra/terraform output -raw agc_frontend_id)
> envsubst < infra/k8s/manifests/gateway/application-loadbalancer.yaml.template | kubectl apply -f -
> kubectl apply -f infra/k8s/manifests/gateway/gateway.yaml
> kubectl apply -f infra/k8s/manifests/gateway/httproute-blazor-ui.yaml
> kubectl apply -f infra/k8s/manifests/gateway/httproute-bff-api.yaml
> ```
>
> See [`infra/k8s/manifests/gateway/README.md`](../../../infra/k8s/manifests/gateway/README.md) for the full sequence (UAMI creation, role assignments, federated identity).

> **Optional — pod-level network segmentation.** The policies in `infra/k8s/manifests/network-policies/` enforce least-privilege traffic. Without them every pod can reach every other pod (which still works, just less secure). Apply with:
>
> ```bash
> kubectl apply -f infra/k8s/manifests/network-policies/
> ```
>
> The namespace, service accounts, and the `keyvault-secrets` Kubernetes Secret are already provisioned by Terraform during Step 1.

1. Go to **Actions → Deploy All Services** in your GitHub repository
2. Click **Run workflow**, select the `dev` environment + your region, and confirm
3. The matrix-strategy jobs run for each service:
   - `build-test` — restore, build, and `dotnet test` per service (parallel)
   - `docker-build-push` — build the per-service Dockerfile and push to ACR (parallel)
   - `deploy-tier-1` … `deploy-tier-5` — `helm upgrade --install` in dependency order

After the workflow completes, validate with:

```bash
kubectl get pods -n contoso          # 8 pods, all Running
kubectl get gateway -n contoso       # PROGRAMMED=True (only after AGC routing prereq above is done)
kubectl get httproute -n contoso     # ACCEPTED=True for blazor-ui + bff-api
```

You should see 8 pods, all `Running`. The Blazor UI is reachable at `https://<AGC_FRONTEND_FQDN>/` — run `terraform -chdir=infra/terraform output -raw agc_frontend_fqdn`.

> Subsequent code changes deploy through the **per-service** workflows (`deploy-crm-api.yml`, `deploy-crm-mcp.yml`, `deploy-knowledge-mcp.yml`, `deploy-crm-agent.yml`, `deploy-product-agent.yml`, `deploy-orchestrator-agent.yml`, `deploy-bff-api.yml`, `deploy-blazor-ui.yml`) — each one is triggered automatically when its `src/<service>/**` subtree changes on `main`, or you can run any of them manually from **Actions → Deploy <Service>**.

## Verification checklist

After completing all steps, verify:

- [ ] Infrastructure resources are visible in the Azure portal (or `terraform output` shows all endpoints)
- [ ] 9 per-component `appsettings.Development.json` files exist under `src/` with non-empty values
- [ ] `simple-agent` returns a joke from AI Foundry
- [ ] Cosmos DB CRM account has 6 containers with data (Customers, Orders, etc.)
- [ ] Azure AI Search index has vectorized document chunks (check indexer status in Azure portal)
- [ ] Azure Blob Storage `product-images` container has 15 `.png` files
- [ ] Azure Blob Storage `sharepoint-docs` container has 12 `.pdf` files
- [ ] `kubectl get pods -n contoso` shows 8 pods all `Running` (Step 4)

## What's next

Lab 1 is complete. Your Azure environment is fully provisioned, validated, and seeded. Continue with:

- **[Lab 2 — Single & Multi-Agent Workflows](lab-2.md)** — drive the existing CRM / Product / Orchestrator agents directly, then add a third specialist (Returns Agent) without touching the others.
- **[Lab 3 — Human-in-the-Loop Workflows](lab-3.md)** — build an ambient, durable refund-risk workflow with three parallel agents, an aggregator, and a Blazor operations dashboard for review.
