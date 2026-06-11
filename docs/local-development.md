# Local Track — Foundry-Only Lab

> ## 📍 You are on the **Local Track** — “Foundry only, everything else local”
>
> | | Local Track *(this page)* | [Full Azure Track — Lab 0](labs/full-azure/lab-0.md) → [Lab 1](labs/full-azure/lab-1.md) |
> |---|---|---|
> | Azure resources | 1 (Foundry only) | 14+ (Foundry, Cosmos×2, AI Search, AKS, ACR, Storage, Key Vault, identities, networking) |
> | Setup time | ~10 min | ~45–60 min |
> | Cost | ~$1–5/day | ~$50–100/day |
> | Where the 9 services run | `dotnet run` (Aspire) on your laptop | AKS pods — 8 in Lab 1, `fraud-workflow` joins in Lab 3 |
> | CRM data | In-memory from `data/contoso-crm/*.csv` | Cosmos DB |
> | Knowledge base | In-memory vectors over `data/contoso-sharepoint/**/*.txt` | Azure AI Search (PDFs) |
> | User auth | Microsoft Entra ID via MSAL (8 test users in your tenant) | Microsoft Entra ID via MSAL (8 test users in your tenant) |
> | When to pick this | Inner loop, demos, agent prompt iteration | Production-shaped end-to-end testing, security/identity work |
>
> Switch tracks anytime; they share no state.

---

Run the entire .NET Agent Framework system locally with a single command. The
local mode runs all 9 services in-process via .NET Aspire, with **in-memory
data** (no Cosmos DB Emulator, no Azurite, no databases to install).

---

## Prerequisites

### Tools

- **[.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)** — for running all components
- **[Azure CLI](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli)** — for `az login`
- **[Terraform >= 1.14.7](https://developer.hashicorp.com/terraform/install)** — to provision Azure AI Foundry
- **[Python 3.9+](https://www.python.org/downloads/)** — required by `infra/setup-local.sh` for JSON/template processing (PowerShell path does not require Python)

### Accounts

- An **Azure subscription** with permission to create an Azure AI Services account

---

## One-Time Setup

### Step 1: Authenticate with Azure

```bash
az login
```

`DefaultAzureCredential` picks up your CLI token, so the local services
authenticate to Foundry as you — no API keys are required.

### Step 2: Deploy Foundry and generate local config

From the repository root:

```powershell
./infra/setup-local.ps1
```

```bash
./infra/setup-local.sh
```

**What this does:**

1. Creates the working resource group `rg-dotnetagent-localdev` out-of-band via `az group create` (idempotent; the RG is **not** Terraform-managed so `-Cleanup` later leaves it intact).
2. Bootstraps a Standard_LRS Storage Account + `tfstate` container **inside the working RG**, then grants you `Storage Blob Data Contributor` on it. Terraform state for the local-dev stack lives there as remote state — it survives `setup-local -Cleanup` because the storage account is bootstrapped out-of-band (Azure CLI), never enters Terraform state, and `terraform destroy` can't touch it. To wipe state for real, delete the working RG: `az group delete --name rg-dotnetagent-localdev`.
3. Generates `infra/terraform/local-dev/backend.hcl` (gitignored) and runs `terraform init -reconfigure -backend-config=backend.hcl`.
4. Runs `terraform apply` in `infra/terraform/local-dev/` — provisions inside the working RG:
   - 1 Azure AI Services account (Foundry) with a default project (`default-project`)
   - 2 model deployments: chat (`gpt-4.1`) and embeddings (`text-embedding-3-small`)
   - Grants you `Cognitive Services OpenAI User` on the Foundry account and `Azure AI User` on the project
5. Reads the Foundry **project** endpoint, deployment names, and tenant ID from Terraform output.
6. Generates `appsettings.Local.json` for each component from `appsettings.Local.json.template`:
   - Agents (`crm-agent`, `product-agent`, `orchestrator-agent`, `simple-agent`) and `knowledge-mcp` get `Foundry:ProjectEndpoint`, `Foundry:DeploymentName` (or `Foundry:EmbeddingDeploymentName`), and `AzureAd:TenantId` substituted in.
   - `crm-api` and `knowledge-mcp` get `DataMode = InMemory` so they load CSV/TXT data from `data/`.
   - `crm-mcp`, `bff-api`, and `blazor-ui` get static port + URL configuration only (no Foundry credentials needed — they call other services over HTTP).
   - Auth everywhere is `DefaultAzureCredential` — your `az login` token. No API keys are written.


**No API keys are written.** Everything authenticates via your Azure CLI token.

### Optional Foundry Toolbox tools

`crm-agent` and `product-agent` can opt in to a Foundry-hosted MCP Toolbox by
setting `Foundry:ToolboxName` in their generated `appsettings.Local.json` files.
The local templates leave this value empty, so the default local stack uses only
the repo's MCP servers.

Product Agent guest requests deliberately suppress the hosted toolbox even when
`Foundry:ToolboxName` is configured. Guests receive Knowledge MCP tools only,
preserving the anonymous guardrail that blocks customer-specific CRM access.

**Cost:** ~$1–5/day (pay-per-token only, no infrastructure beyond the Foundry account).

**Region:** Defaults to `centralus`. Override with `TF_VAR_location=<region>` (the region must support `gpt-4.1` and `text-embedding-3-small` with at least 120K TPM embedding capacity).

### Cleanup

When you're finished with the labs, tear down the local Foundry environment:

```powershell
# Windows / PowerShell
./infra/setup-local.ps1 -Cleanup
```

```bash
# macOS / Linux
./infra/setup-local.sh --cleanup
```

This destroys the Foundry resources, **the Entra SPA app registration, and the 8 test users**, and removes the generated `appsettings.Local.json` files.

Foundry account cleanup is configured as a hard delete in Terraform provider features (`purge_soft_delete_on_destroy = true`), so treat `-Cleanup` as non-recoverable for that local account.

**The working RG is intentionally preserved:**

- `rg-dotnetagent-localdev` is bootstrapped out-of-band by `setup-local`, looked up via a Terraform `data` source, and never enters Terraform state — so `terraform destroy` cannot touch it. It also holds the Standard_LRS storage account backing Terraform's remote state (also bootstrapped out-of-band). Any diagnostic resources you've placed alongside the Foundry account survive a tear-down/re-apply cycle, and so does TF state.

To wipe everything for real, run `az group delete --name rg-dotnetagent-localdev`.

---

## Running the System

### Use the Aspire dashboard (recommended)

```bash
dotnet run --project src/AppHost
```

This starts the Aspire orchestration dashboard (default at **https://localhost:15888**) with:

- All 9 components registered with live logs and metrics
- Health-check status per service
- Port mappings (5001–5008)
- One-click stop / restart per service

> **The dashboard asks for a token on first load.** Aspire 9+ guards it with a per-run browser token. The AppHost console prints a line like `Login to the dashboard at https://localhost:15888/login?t=<GUID>` — click that URL (or paste the GUID into the prompt). The token rotates each restart.

### Manual start (optional)

If you prefer to run components individually, open 9 terminals. Each project
must be told to load `appsettings.Local.json` (generated by `setup-local`)
by setting `ASPNETCORE_ENVIRONMENT=Local` for the spawned process, and you
must pass `--urls` so the project binds to its expected port (the templates
intentionally do not hardcode the Kestrel URL — that's reserved for the
Aspire AppHost, which assigns ports via the DCP proxy). The Aspire AppHost
does both for you automatically — you only need to do this for manual runs.

> **Note on `dotnet run --environment`** — the .NET 9 SDK redefined the
> `--environment` / `-e` flag to take `NAME=VALUE` pairs (it does **not**
> set `ASPNETCORE_ENVIRONMENT` on its own anymore). Set the env var first
> in the same shell, as shown below.

PowerShell (Windows):

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Local'
dotnet run --project src/crm-api --urls http://localhost:5001
```

Bash (macOS / Linux / WSL):

```bash
ASPNETCORE_ENVIRONMENT=Local dotnet run --project src/crm-api --urls http://localhost:5001
```

| Terminal | Component | Project | Port |
|----------|-----------|---------|------|
| 1 | CRM API | `src/crm-api` | 5001 |
| 2 | CRM MCP | `src/crm-mcp` | 5002 |
| 3 | Knowledge MCP | `src/knowledge-mcp` | 5003 |
| 4 | CRM Agent | `src/crm-agent` | 5004 |
| 5 | Product Agent | `src/product-agent` | 5005 |
| 6 | Orchestrator Agent | `src/orchestrator-agent` | 5006 |
| 7 | BFF API | `src/bff-api` | 5007 |
| 8 | Blazor UI | `src/blazor-ui` | 5008 |
| 9 | Fraud Workflow | `src/fraud-workflow` | 5010 |

---

## System Ports & URLs

| Component | Port | URL | Purpose |
|-----------|------|-----|---------|
| CRM API | 5001 | `http://localhost:5001` | Core CRM data endpoints (11 routes) |
| CRM MCP | 5002 | `http://localhost:5002` | MCP server wrapping the CRM API as 11 tools |
| Knowledge MCP | 5003 | `http://localhost:5003` | MCP server with `search_knowledge_base` |
| CRM Agent | 5004 | `http://localhost:5004` | CRM specialist agent |
| Product Agent | 5005 | `http://localhost:5005` | Product/recommendation agent |
| Orchestrator Agent | 5006 | `http://localhost:5006` | Intent classifier + router |
| BFF API | 5007 | `http://localhost:5007` | Backend-for-Frontend |
| Blazor UI | 5008 | `http://localhost:5008` | User interface (WASM SPA) |
| Fraud Workflow | 5010 | `http://localhost:5010` | Refund-risk workflow (fan-out, aggregator, paused human gate) |
| Aspire Dashboard | 15888 | `https://localhost:15888` | Monitoring + orchestration |

---

## User Authentication

Both tracks use **real Microsoft Entra ID via MSAL** for user sign-in.
`infra/setup-local.{ps1,sh}` provisions a per-developer SPA app registration
in your tenant plus 8 test users matching the seeded customers in
[`data/contoso-crm/customers.csv`](../data/contoso-crm/customers.csv):

| Test user | UPN | Customer ID | Loyalty Tier |
|-----------|-----|-------------|--------------|
| Emma Wilson   | `emma.wilson-local@<your-tenant>`   | 101 | Silver |
| James Chen    | `james.chen-local@<your-tenant>`    | 102 | Bronze |
| Sarah Miller  | `sarah.miller-local@<your-tenant>`  | 103 | Gold |
| David Park    | `david.park-local@<your-tenant>`    | 104 | Silver |
| Lisa Torres   | `lisa.torres-local@<your-tenant>`   | 105 | Bronze |
| Mike Johnson  | `mike.johnson-local@<your-tenant>`  | 106 | Gold |
| Anna Roberts  | `anna.roberts-local@<your-tenant>`  | 107 | Bronze |
| Tom Garcia    | `tom.garcia-local@<your-tenant>`    | 108 | Silver |

The `-local` suffix exists so the Local Track and Full Azure Track can both
run in the same tenant — the Full Azure Track creates the unsuffixed
`emma.wilson@<tenant>` and tenants enforce UPN uniqueness. `setup-local`
writes each generated password to `local-dev-credentials.txt` at the repo root
(gitignored — each `setup-local` run rotates the passwords, so the file is rewritten in full).
The BFF maps each UPN → customer ID via `AzureAd:CustomerMap` in
`src/bff-api/appsettings.Local.json`, so `emma.wilson-local` always resolves to
customer `101` (Silver tier, Portland OR).

If you need to bypass MSAL for an automated test, set `AzureAd:Enabled = false`
in both [src/bff-api/appsettings.Local.json](../src/bff-api/appsettings.Local.json)
and [src/blazor-ui/appsettings.Local.json](../src/blazor-ui/appsettings.Local.json),
then restart AppHost. The Blazor UI will fall back to the customer dropdown
and the BFF will accept `X-Customer-Id` headers — useful only for non-interactive
testing.

---

## Architecture Overview

### What's running where

**Local machine (in-process):**

- All 9 services via `dotnet run --project src/AppHost`
- In-memory CRM data (CSVs from `data/contoso-crm/`)
- In-memory knowledge base (TXT files from `data/contoso-sharepoint/`, embedded with Foundry)
- Local file system for product images (`data/contoso-images/`)
- Aspire dashboard for observability

**Azure (single resource):**

- Azure AI Foundry account with `gpt-4.1` and `text-embedding-3-small` deployments

### Data patterns

**CRM data (CSV → in-memory):**

- CRM API loads `data/contoso-crm/*.csv` into `InMemoryCrmDataService` at startup.
- BFF proxies CRM API calls. Agents access CRM data through the CRM MCP tools.

**Knowledge base (TXT → embeddings → in-memory vector index):**

- Knowledge MCP loads `data/contoso-sharepoint/**/*.txt` at startup.
- Each file is embedded via Foundry, then queries hit `InMemorySearchService` for cosine-similarity ranking.
- Foundry calls authenticate via `DefaultAzureCredential` — no API key.

**Product images (file system):**

- BFF reads PNGs from `data/contoso-images/` and streams them through `/api/v1/images/{filename}`.

### Component interaction

```text
Browser
  │
  ▼
Blazor UI (5008)
  │
  └─► BFF API (5007)
        │
        ├─► CRM API (5001) — in-memory CRM service
        │     └─► Fraud Workflow (5010)  [fire-and-forget refund-risk alert on category=return tickets;
        │                              the workflow calls back to crm-api /internal/tickets/{id}/refund-decision]
        │
        └─► Orchestrator Agent (5006)
              │
              ├─► CRM Agent (5004)
              │     ├─► CRM MCP (5002) ─► CRM API (5001)
              │     └─► Knowledge MCP (5003) ─► local TXT files + Foundry embeddings
              │
              └─► Product Agent (5005)
                    ├─► CRM MCP (5002) ─► CRM API (5001)
                    └─► Knowledge MCP (5003) ─► local TXT files + Foundry embeddings

Fraud Workflow (5010) also calls:
  • CRM MCP (5002) and Knowledge MCP (5003) for the 3 specialist agents that fan out per refund alert
  • BFF API (5007) hosts the operations review queue UI for human approve/reject decisions

All agents call Azure AI Foundry for:
  • Chat completions: gpt-4.1
  • Embeddings:        text-embedding-3-small
Authentication everywhere: DefaultAzureCredential (your `az login` token).
```

---

## Common Tasks

### Check health

Open the Aspire dashboard at **https://localhost:15888**. Each service shows:

- Status (running / failed)
- Live logs
- Environment variables and resource usage

Or hit the endpoints directly:

```bash
curl http://localhost:5001/health        # liveness
curl http://localhost:5001/ready         # readiness (deps reachable)
```

### View CRM data

```bash
curl http://localhost:5001/api/v1/customers
curl http://localhost:5001/api/v1/customers/101
curl http://localhost:5001/api/v1/customers/101/orders
```

### Run tests

Test projects live under `src-tests/`:

```bash
dotnet test src-tests/Contoso.CrmApi.Tests/Contoso.CrmApi.Tests.csproj
dotnet test src-tests/Contoso.BffApi.Tests/Contoso.BffApi.Tests.csproj
# Or run everything:
dotnet test dotnet-agent-framework.sln
```

### Debug a component

In your IDE, set a breakpoint and run the project directly (not via AppHost).
Set `ASPNETCORE_ENVIRONMENT=Local` in the same shell so the project loads
`appsettings.Local.json`, and pass `--urls` for the port (the .NET 9 SDK's
`--environment NAME=VALUE` flag does **not** set the environment for you):

```powershell
# PowerShell
$env:ASPNETCORE_ENVIRONMENT = 'Local'
dotnet run --project src/crm-api --urls http://localhost:5001 --no-restore
```

```bash
# Bash / macOS / Linux
ASPNETCORE_ENVIRONMENT=Local dotnet run --project src/crm-api --urls http://localhost:5001 --no-restore
```

---

## Troubleshooting

### `az login` fails or token expired

```bash
az account clear
az login
```

### Port 5001–5008 already in use

```powershell
# Windows / PowerShell
Get-NetTCPConnection -LocalPort 5001 | Select-Object -ExpandProperty OwningProcess | ForEach-Object { Stop-Process -Id $_ -Force }
```

```bash
# macOS / Linux
lsof -i :5001
kill -9 <PID>
```

### `appsettings.Local.json` missing

Re-run setup; the script regenerates the files:

```powershell
./infra/setup-local.ps1
```

### Aspire dashboard logs `AuthenticationException: ... UntrustedRoot`

The ASP.NET Core developer certificate isn't trusted on this machine. The
dashboard at `https://localhost:15888` then fails to gRPC-call itself over
TLS. One-time fix:

```powershell
dotnet dev-certs https --trust
```

Click **Yes** on the OS trust prompt, then re-run `dotnet run --project src/AppHost`.

### Components are calling Azure endpoints instead of using in-memory data

This means `appsettings.Local.json` was not loaded. Confirm:

- `src/<component>/appsettings.Local.json` exists (re-run `setup-local` if missing).
- You are starting the system via `dotnet run --project src/AppHost` **or** you set `ASPNETCORE_ENVIRONMENT=Local` in the shell before `dotnet run` for manual runs (the .NET 9 SDK's `dotnet run --environment` flag does **not** set this).
- The CRM API startup log line shows `Hosting environment: Local` (not `Development`).

### Foundry quota or model deployment failure

Check the Foundry account in the Azure portal under
`rg-dotnetagent-localdev`. Confirm the chat and embedding
deployments exist and have available quota in the chosen region. If `terraform
apply` fails due to quota, retry with a different region:

```powershell
$env:TF_VAR_location = "eastus2"
./infra/setup-local.ps1
```

### Knowledge base returns no matches

The first request after startup populates the in-memory vector index by
calling Foundry for embeddings. Watch the Knowledge MCP logs in the Aspire
dashboard for progress. Reload after a few seconds.

---

## What's Different from Azure Deployment

| Aspect | Local Dev | Azure Deployment |
|--------|-----------|------------------|
| CRM data | In-memory (`data/contoso-crm/*.csv`) | Cosmos DB (CRM account, 6 containers) |
| Knowledge base | In-memory vector index over local TXT files | Azure AI Search (Standard, integrated vectorization over PDFs) |
| Product images | File system (`data/contoso-images/`) | Azure Blob Storage (image proxy through BFF) |
| AI models | Foundry (Azure) — `DefaultAzureCredential` | Foundry (Azure) — workload identity / agent identity |
| Auth | Microsoft Entra ID via MSAL (PKCE) — 8 test users in your tenant | Microsoft Entra ID via MSAL (PKCE) — 8 test users in your tenant |
| Network | `localhost` | App Gateway for Containers + private endpoints |
| Hosting | `dotnet run` via Aspire | AKS (Helm + workload identity) |
| Cost | ~$1–5/day | ~$50–100/day |
| Setup time | ~10 min | ~45–60 min |

No code changes are required to switch — components read `DataMode` from
configuration to pick `InMemory*` vs Azure-backed services.
