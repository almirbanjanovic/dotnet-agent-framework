# Local Development Quick-Start Guide

Run the entire .NET Agent Framework system locally with a single command.

---

## Prerequisites

Before you start, ensure you have the following installed:

### Tools

- **[.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)** — for running all components
- **[Azure CLI](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli)** — for Azure authentication (`az login`)
- **[Terraform >= 1.14.7](https://developer.hashicorp.com/terraform/install)** — for deploying Foundry
- **[Cosmos DB Emulator](https://aka.ms/cosmosdb-emulator)** — for local CRM data persistence
- **Node.js + npm** — for Azurite (Azure Storage emulator)

### Accounts

- An **Azure subscription** with Owner or Contributor permissions

---

## One-Time Setup

### Step 1: Authenticate with Azure

```bash
az login
```

### Step 2: Start local emulators

**Cosmos DB Emulator:**
- On Windows, open the Cosmos DB Emulator application from Start menu, or run:
  ```powershell
  # If installed via winget/MSI, it may start as a service automatically.
  # Verify it's running: https://localhost:8081/_explorer/index.html
  ```

**Azurite (Storage Emulator):**
```bash
npm install -g azurite  # (one-time)
azurite --silent --location .azurite
# Runs on: http://127.0.0.1:10000 (blob), http://127.0.0.1:10001 (queue), http://127.0.0.1:10002 (table)
```

> **Tip:** Keep both emulator terminals open while developing.

### Step 3: Deploy Foundry and generate local config

From the project root:

```powershell
./infra/setup-local.ps1
```

**What this does:**
1. Deploys **Azure AI Foundry** (1 resource group + 1 AI Services account + 2 model deployments) to your Azure subscription
2. Retrieves the Foundry API key
3. Generates `appsettings.Local.json` for each of the 8 components with:
   - Foundry endpoint and API key
   - Cosmos DB emulator connection strings
   - Azurite blob storage connection strings
   - Seeded customer IDs for dev auth
4. Seeds CRM data (customers, orders, products, promotions, support tickets) into the local Cosmos emulator
5. Uploads product images and knowledge documents to Azurite

**Cost:** ~$1–5/day (pay-per-token only, no infrastructure charges beyond Foundry account creation).

**Cleanup (optional):** To remove Foundry and free up costs:
```powershell
./infra/setup-local.ps1 -Cleanup
```

---

## Running the System

### Quick Start: Use Aspire Dashboard

From the project root, run all 8 components together with monitoring:

```bash
dotnet run --project src/AppHost
```

This starts the **Aspire orchestration dashboard** at **https://localhost:15000** with:
- All 8 components visible and live logs
- Health checks for each component
- Port mappings (5001–5008)
- One-click stop/restart

### Manual Start (Optional)

If you prefer to run components individually, open 8 terminals and run:

| Terminal | Component | Port |
|----------|-----------|------|
| 1 | `cd src/crm-api && dotnet run --environment Local` | 5001 |
| 2 | `cd src/crm-mcp && dotnet run --environment Local` | 5002 |
| 3 | `cd src/knowledge-mcp && dotnet run --environment Local` | 5003 |
| 4 | `cd src/crm-agent && dotnet run --environment Local` | 5004 |
| 5 | `cd src/product-agent && dotnet run --environment Local` | 5005 |
| 6 | `cd src/orchestrator-agent && dotnet run --environment Local` | 5006 |
| 7 | `cd src/bff-api && dotnet run --environment Local` | 5007 |
| 8 | `cd src/blazor-ui && dotnet run --environment Local` | 5008 |

---

## System Ports & URLs

| Component | Port | URL | Purpose |
|-----------|------|-----|---------|
| **CRM API** | 5001 | `http://localhost:5001` | Core CRM data endpoints (11 APIs) |
| **CRM MCP** | 5002 | `http://localhost:5002` | MCP server wrapping CRM API as tools |
| **Knowledge MCP** | 5003 | `http://localhost:5003` | MCP server for knowledge base search |
| **CRM Agent** | 5004 | `http://localhost:5004` | Agent specializing in CRM queries |
| **Product Agent** | 5005 | `http://localhost:5005` | Agent specializing in product queries |
| **Orchestrator Agent** | 5006 | `http://localhost:5006` | Intent classifier & router agent |
| **BFF API** | 5007 | `http://localhost:5007` | Backend-for-Frontend (conversation, image proxy) |
| **Blazor UI** | 5008 | `https://localhost:5008` | User interface (WASM SPA, MudBlazor) |
| **Aspire Dashboard** | 15000 | `https://localhost:15000` | Monitoring & orchestration |

---

## User Authentication (Dev Mode)

The Blazor UI includes a customer **dropdown menu** for dev testing. Select from these built-in customers:

| ID | Name | Email |
|----|------|-------|
| 101 | Alice Johnson | alice.johnson@contoso.com |
| 102 | Bob Smith | bob.smith@contoso.com |
| 103 | Charlie Brown | charlie.brown@contoso.com |
| 104 | Diana Prince | diana.prince@contoso.com |
| 105 | Eve Martinez | eve.martinez@contoso.com |
| 106 | Frank Chen | frank.chen@contoso.com |
| 107 | Grace Lee | grace.lee@contoso.com |
| 108 | Henry Davis | henry.davis@contoso.com |

This is a shortcut for local dev. In Azure, the UI uses MSAL for Microsoft Entra ID authentication.

---

## Architecture Overview

### What's Running Where

**Local Machine:**
- ✅ **All 8 components** (via `dotnet run`)
- ✅ **Cosmos DB Emulator** (CRM data persistence)
- ✅ **Azurite** (Blob storage, product images, knowledge docs)
- ✅ **Aspire orchestration** (health monitoring, logs)

**Azure:**
- ☁️ **Foundry account** (Azure AI Services, gpt-4.1 + text-embedding-3-small models)

### Data Patterns

**CRM Data (In-Memory → Cosmos):**
- CSV files in `data/contoso-crm/` are parsed and seeded into Cosmos containers
- Each component reads from Cosmos via the **CRM API** (repository pattern)
- BFF proxies CRM API calls; agents access via **CRM MCP tools**

**Knowledge Base (In-Memory → Azurite):**
- PDF/TXT files in `data/contoso-sharepoint/` are seeded into Azurite blob storage
- Agents search via **Knowledge MCP** → embeddings via Foundry → cosine similarity
- Results augment agent context (RAG pattern)

**Product Images (Blob Storage):**
- PNG files in `data/contoso-images/` are uploaded to Azurite
- BFF proxies image bytes to the browser (`/api/images/{filename}`)

### Component Interaction

```text
Browser
  ↓
Blazor UI (5008)
  ├→ BFF API (5007)
  │   ├→ CRM API (5001) → Cosmos DB Emulator
  │   ├→ Orchestrator Agent (5006)
  │   └→ Blob Storage (Azurite)
  │
  └→ Orchestrator Agent (5006, via HTTP)
      ├→ CRM Agent (5004)
      │   ├→ CRM MCP (5002) → CRM API (5001)
      │   └→ Knowledge MCP (5003) → Azurite
      │
      └→ Product Agent (5005)
          ├→ CRM MCP (5002) → CRM API (5001)
          └→ Knowledge MCP (5003) → Azurite

All agents use Foundry (Azure) for:
  - Chat model: gpt-4.1
  - Embeddings: text-embedding-3-small
```

---

## Common Tasks

### Check Health of All Components

Open the Aspire Dashboard: **https://localhost:15000**

Each component shows:
- ✅ Running (green) or ❌ Failed (red)
- Real-time logs
- Environment variables
- Resource usage

### View CRM Data

**Via API:**
```bash
curl http://localhost:5001/api/v1/customers
curl http://localhost:5001/api/v1/customers/101
curl http://localhost:5001/api/v1/orders?customerId=101
```

**Via Cosmos Emulator UI:**
Open https://localhost:8081/_explorer/index.html in your browser and browse the `contoso-crm` database.

### Run Tests

```bash
# Unit tests for CRM API
dotnet test src/crm-api.tests

# Integration tests for BFF
dotnet test src/bff-api.tests
```

### Debug a Component

Add a breakpoint in your IDE and run:
```bash
cd src/crm-api
dotnet run --environment Local --no-restore
```

The component will wait for the debugger to attach.

---

## Troubleshooting

### Issue: `az login` fails or requires re-authentication

**Solution:**
```bash
az account clear
az login
```

### Issue: Cosmos DB Emulator won't start

**Symptoms:** Port 8081 already in use or "emulator not found"

**Solution (Windows):**
```powershell
# Check if running
Get-Process | grep -i cosmos

# Kill existing process if stuck
Stop-Process -Name "CosmosDB.Emulator" -Force

# Restart from Start menu or re-install:
winget install Microsoft.Azure.CosmosEmulator
```

### Issue: Azurite connection refused

**Symptoms:** `Connection refused: 127.0.0.1:10000`

**Solution:**
```bash
# Ensure Azurite is running
azurite --silent --location .azurite

# Or kill if stuck
pkill -f azurite   # macOS/Linux
Stop-Process -Name "node" -Filter {$_.Path -like "*azurite*"}  # Windows
```

### Issue: "Connection string is required" at startup

**Symptoms:** Component fails with missing `CosmosDb:ConnectionString`

**Solution:**
```bash
# Regenerate config
./infra/setup-local.ps1

# Verify `appsettings.Local.json` exists in src/crm-api/ (and other components)
ls src/crm-api/appsettings.Local.json
```

### Issue: Terraform state corruption

**Symptoms:** `terraform apply` fails with "resource already exists" or "state mismatch"

**Solution:**
```bash
cd infra/terraform/local-dev

# Check current state
terraform state list

# (Rarely needed) Reset state if corrupted
rm -rf .terraform.lock.hcl terraform.tfstate*
terraform init
terraform apply
```

### Issue: Component port 5001–5008 already in use

**Symptoms:** `Address already in use` when starting components

**Solution:**
```bash
# Find what's using the port (example: 5001)
netstat -ano | grep 5001  # Windows
lsof -i :5001             # macOS/Linux

# Kill the process by PID
Stop-Process -Id <PID> -Force  # Windows
kill -9 <PID>                  # macOS/Linux

# Or just pick a different port:
dotnet run --environment Local -- --urls "http://localhost:5011"
```

---

## Development Workflow

### 1. Clone and Initialize

```bash
git clone <repo>
cd dotnet-agent-framework
```

### 2. Run Setup

```bash
./infra/setup-local.ps1
# This also starts emulators if needed
```

### 3. Start All Components

```bash
dotnet run --project src/AppHost
# Dashboard at https://localhost:15000
```

### 4. Open UI in Browser

```bash
https://localhost:5008
```

Select a customer from the dropdown and start chatting!

### 5. Make Code Changes

Edit any component (e.g., `src/crm-api/Program.cs`). Aspire detects file changes and restarts the component automatically.

### 6. Run Tests

```bash
dotnet test src/crm-api.tests
```

### 7. Stop All Components

In the Aspire Dashboard, click **Stop** or Ctrl+C in the terminal where AppHost is running.

---

## What's Different from Azure Deployment

| Aspect | Local Dev | Azure |
|--------|-----------|-------|
| **CRM Database** | Cosmos DB Emulator (local) | Azure Cosmos DB (managed service) |
| **Storage** | Azurite (local) | Azure Blob Storage |
| **AI Models** | Foundry (Azure) | Foundry (Azure) |
| **Auth** | Dropdown menu (dev bypass) | MSAL + Microsoft Entra ID |
| **Network** | `localhost` only | Public internet (Ingress + NSGs) |
| **Cost** | ~$1–5/day | ~$50–100/day |
| **Setup time** | ~15 min | ~45–60 min |
| **Deployment** | `dotnet run` | Helm + AKS |

In Azure, replace emulators and dropdown auth with real cloud services and MSAL authentication. **No code changes needed** — all components support both modes via configuration.

---

## Next Steps

- **Learn the architecture:** See [docs/architecture.png](architecture.png) and [README.md](../README.md)
- **Implement a component:** Follow [docs/implementation-plan.md](implementation-plan.md)
- **Deploy to Azure:** See [infra/README.md](../infra/README.md) and [Lab 1](lab-1.md)
- **Write tests:** See [docs/security.md](security.md) for RBAC patterns and test strategies
- **Contribute:** Create a feature branch, make changes, run tests, submit a PR

---

## Support

- **Got stuck?** Check [Troubleshooting](#troubleshooting) above
- **Found a bug?** Open an issue on GitHub
- **Want to contribute?** See [CONTRIBUTING.md](../CONTRIBUTING.md) (if it exists) or start with the implementation plan
