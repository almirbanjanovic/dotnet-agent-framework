# Foundry-Only Deployment — Local Dev Mode (Technical Overview)

> **Author:** Stewie (Lead/Architect)
> **Requested by:** Almir Banjanovic
> **Date:** 2025-07-26
> **Last Updated:** 2025-07-27
> **Status:** Specification v3 — in-memory + repository pattern approach

---

## 1. Overview

### What This Mode Is

Local dev mode is an **additive** deployment option alongside the existing full Azure deployment. It deploys **only Azure AI Foundry** (AI Services account + 2 model deployments) to Azure, while running everything else — all 8 application components — on the developer's local machine using `dotnet run`.

**Key difference from previous versions:** Local dev mode uses **in-memory data** (CSVs loaded at startup) via the **repository pattern**, not emulators. This is simpler, faster, and cleaner for local development.

The existing full deployment path (`init.ps1` → `deploy.ps1` → Terraform → AKS) is **completely untouched**. Both modes coexist in the repo.

### Comparison Table

| Aspect | Full Azure Deployment | Local Dev Mode |
|---|---|---|
| **Azure resources** | 14+ services (AI, Cosmos×2, Search, Storage, AKS, ACR, AGC, VNet, KV, identities, DNS, TLS) | 1 resource group + 1 AI Services account + 2 model deployments |
| **Estimated cost** | ~$50–100/day | ~$1–5/day (pay-per-token only) |
| **Setup time** | 45–60 min | ~10 min |
| **CRM Data** | Azure Cosmos DB (2 accounts, RBAC auth) | In-memory repository (CSV → `List<T>`) |
| **Knowledge Index** | Azure AI Search (semantic ranking) | In-memory vector search (embeddings via Foundry + cosine similarity) |
| **Product Images** | Azure Blob Storage (managed identity) | File system or in-memory cache |
| **Auth model** | DefaultAzureCredential + RBAC everywhere | API keys (Foundry), dev bypass for user auth |
| **Networking** | Private endpoints, VNet, NSGs | `localhost` only |
| **Identity** | Managed identities, workload identity, Entra Agent ID | None (dev mode) |
| **Run method** | AKS (Helm charts, Docker images) | `dotnet run --project src/AppHost` |
| **Config source** | Key Vault → config-sync → appsettings.Development.json | Direct `appsettings.Local.json` |
| **Dashboard** | None (production-grade) | Aspire orchestration dashboard at localhost:15000 |

### Developer Workflow (Git Clone to Running System)

See **[Local Development Guide](local-development.md)** for the quick-start walk-through. In brief:

```
1. git clone <repo>
2. Install prerequisites: .NET 9 SDK, Azure CLI, Terraform
3. az login
4. Run: ./infra/setup-local.ps1
   → Deploys Foundry via Terraform (1 resource group + 1 AI account + 2 model deployments)
   → Retrieves API key from Terraform output
   → Generates appsettings.Local.json for each component with Foundry credentials
5. Run: dotnet run --project src/AppHost
   → Starts Aspire orchestration
   → All 8 components run locally with in-memory data
   → Dashboard at https://localhost:15000
6. Open browser: https://localhost:5008
```

---

## 2. Terraform: Foundry-Only Configuration

### Approach Analysis

**Option A: Separate root module (`infra/terraform/local-dev/`)**
- Creates a new, minimal root module that calls `modules/foundry/v1`
- Has its own `providers.tf` (only azurerm + http — no azuread, azapi, kubernetes, kubectl, msgraph)
- Has its own state (no risk of conflicting with the full deployment state)
- Overrides `local_auth_enabled = true` (API keys enabled) — requires adding one variable to the foundry module
- Pros: Complete isolation, no risk to existing infrastructure
- Cons: Minor duplication of foundry module call

**Option B: Shared root with `local-dev.tfvars`**
- Uses the existing `main.tf` with a new `.tfvars` file
- Problem: `main.tf` references 20+ modules, many with required variables and provider dependencies (kubernetes, kubectl, msgraph). Cannot partially apply.
- Would require extensive `count`/`for_each` conditionals across all modules
- **Not viable without major refactoring of main.tf**

**Recommendation: Option A** — separate root module. Clean isolation, no risk to production infrastructure, simplest to maintain.

### Required Change to Foundry Module

Add one variable to `modules/foundry/v1/variables.tf`:

```hcl
variable "local_auth_enabled" {
  description = "Whether to enable local (API key) authentication. Set to true for local dev mode."
  type        = bool
  default     = false
}
```

Update `modules/foundry/v1/main.tf` line 19:

```hcl
# Change from:
local_auth_enabled    = false

# Change to:
local_auth_enabled    = var.local_auth_enabled
```

Add API key output to `modules/foundry/v1/outputs.tf`:

```hcl
output "primary_access_key" {
  description = "Primary access key (only available when local_auth_enabled = true)"
  value       = azurerm_cognitive_account.this.primary_access_key
  sensitive   = true
}
```

**Impact on existing deployment:** Zero. The default value (`false`) preserves current behavior. The full deployment in `main.tf` does not pass this variable, so it inherits the default. The `primary_access_key` output will be empty/null when local auth is disabled.

### New File: `infra/terraform/local-dev/main.tf`

```hcl
# =============================================================================
# Local Dev — Foundry Only
# Deploys: 1 resource group + 1 AI Services account + 2 model deployments
# =============================================================================

data "http" "deployer_ip" {
  url = "https://api.ipify.org"
}

resource "azurerm_resource_group" "this" {
  name     = var.resource_group_name
  location = var.location
  tags     = var.tags
}

module "foundry" {
  source = "../modules/foundry/v1"

  base_name           = var.base_name
  environment         = var.environment
  location            = var.location
  resource_group_name = azurerm_resource_group.this.name

  account_kind             = "AIServices"
  sku_name                 = "S0"
  deployment_sku_name      = var.deployment_sku_name
  deployment_model_format  = "OpenAI"
  deployment_model_name    = var.chat_model_name
  deployment_model_version = var.chat_model_version
  version_upgrade_option   = "NoAutoUpgrade"

  create_embedding_deployment = true
  embedding_model_name        = var.embedding_model_name
  embedding_model_version     = var.embedding_model_version
  embedding_sku_name          = var.embedding_sku_name
  embedding_capacity          = var.embedding_capacity

  local_auth_enabled            = true
  public_network_access_enabled = true
  allowed_ips                   = [chomp(data.http.deployer_ip.response_body)]

  tags = var.tags
}
```

### New File: `infra/terraform/local-dev/variables.tf`

```hcl
variable "base_name" {
  description = "Project base name for resource naming"
  type        = string
  default     = "dotnetagent"
}

variable "environment" {
  description = "Environment name"
  type        = string
  default     = "localdev"
}

variable "location" {
  description = "Azure region"
  type        = string
  default     = "centralus"
}

variable "resource_group_name" {
  description = "Resource group name for local dev Foundry resources"
  type        = string
  default     = "rg-dotnetagent-localdev"
}

variable "chat_model_name" {
  description = "Chat model name"
  type        = string
  default     = "gpt-4.1"
}

variable "chat_model_version" {
  description = "Chat model version"
  type        = string
  default     = "2025-04-14"
}

variable "deployment_sku_name" {
  description = "Deployment SKU"
  type        = string
  default     = "GlobalStandard"
}

variable "embedding_model_name" {
  description = "Embedding model name"
  type        = string
  default     = "text-embedding-3-small"
}

variable "embedding_model_version" {
  description = "Embedding model version"
  type        = string
  default     = "1"
}

variable "embedding_sku_name" {
  description = "Embedding SKU"
  type        = string
  default     = "GlobalStandard"
}

variable "embedding_capacity" {
  description = "Embedding TPM capacity (in thousands)"
  type        = number
  default     = 10
}

variable "tags" {
  description = "Tags to apply to all resources"
  type        = map(string)
  default = {
    project     = "dotnet-agent-framework"
    managed-by  = "terraform"
    environment = "localdev"
    mode        = "local-dev"
  }
}
```

### New File: `infra/terraform/local-dev/outputs.tf`

```hcl
output "foundry_endpoint" {
  description = "Azure AI Foundry endpoint"
  value       = module.foundry.endpoint
}

output "foundry_api_key" {
  description = "Foundry API key for local dev"
  value       = module.foundry.primary_access_key
  sensitive   = true
}

output "chat_deployment_name" {
  description = "Chat model deployment name"
  value       = module.foundry.deployment_name
}

output "embedding_deployment_name" {
  description = "Embedding model deployment name"
  value       = module.foundry.embedding_deployment_name
}

output "resource_group_name" {
  description = "Resource group name"
  value       = azurerm_resource_group.this.name
}
```

### New File: `infra/terraform/local-dev/providers.tf`

```hcl
terraform {
  required_version = "~> 1.14"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.63.0"
    }

    http = {
      source  = "hashicorp/http"
      version = "~> 3.5"
    }
  }

  # Local state — no remote backend needed for local dev
  backend "local" {}
}

provider "azurerm" {
  features {
    cognitive_account {
      purge_soft_delete_on_destroy = true
    }

    resource_group {
      prevent_deletion_if_contains_resources = false
    }
  }

  resource_provider_registrations = "extended"
}
```

### What `terraform apply` Creates

Exactly 4 resources:

1. **`azurerm_resource_group.this`** — `rg-dotnetagent-localdev`
2. **`azurerm_cognitive_account`** — `aif-dotnetagent-localdev-centralus` (AI Services, S0, API keys enabled, public access with IP allowlist)
3. **`azurerm_cognitive_deployment.this`** — `gpt-4.1` (GlobalStandard)
4. **`azurerm_cognitive_deployment.embedding`** — `text-embedding-3-small` (GlobalStandard, 10K TPM)

**Estimated cost:** $0/month base + ~$1–5/day usage (pay-per-token only, no reserved capacity).

---

## 3. Data Model: Repository Pattern + In-Memory

All data in local dev mode is loaded from CSV files at startup into memory, accessed via repositories, and served through HTTP APIs.

### Data Sources

| Domain | Source | Format | Components |
|--------|--------|--------|------------|
| **CRM Data** | `data/contoso-crm/*.csv` (Customers, Orders, Products, Promotions, SupportTickets) | CSV | Repository → CRM API → BFF/Agents |
| **Knowledge Base** | `data/contoso-sharepoint/` (PDFs/TXTs) | PDF, TXT | In-memory vector index → Knowledge MCP |
| **Product Images** | `data/contoso-images/*.png` | PNG | File system cache → BFF proxy |

### Repository Pattern

Each component uses the **repository pattern** to access data. In local dev mode, repositories load from CSV files and cache in memory:

```csharp
// Example: ICrmRepository
public interface ICrmRepository
{
    Task<IReadOnlyList<Customer>> GetCustomersAsync();
    Task<Customer?> GetCustomerAsync(string id);
    Task<IReadOnlyList<Order>> GetOrdersByCustomerAsync(string customerId);
}

// Local implementation
public class LocalCrmRepository : ICrmRepository
{
    private readonly Lazy<List<Customer>> _customers;
    private readonly Lazy<List<Order>> _orders;
    
    public LocalCrmRepository()
    {
        _customers = new Lazy<List<Customer>>(() =>
            LoadCsvFile<Customer>("data/contoso-crm/customers.csv"));
        _orders = new Lazy<List<Order>>(() =>
            LoadCsvFile<Order>("data/contoso-crm/orders.csv"));
    }
    
    public Task<IReadOnlyList<Customer>> GetCustomersAsync() =>
        Task.FromResult(_customers.Value.AsReadOnly());
}

// In Program.cs
builder.Services.AddScoped<ICrmRepository, LocalCrmRepository>();
```

### Component DataMode Switch

Each component supports a `DataMode` configuration that switches implementations:

```json
{
  "DataMode": "Local"  // or "Azure"
}
```

| Component | Local Mode | Azure Mode |
|-----------|-----------|-----------|
| **crm-api** | `LocalCrmRepository` (CSV → memory) | `AzureCrmRepository` (Cosmos DB) |
| **knowledge-mcp** | `LocalVectorSearchService` (in-memory embeddings + cosine similarity) | `AzureSearchService` (Azure AI Search) |
| **bff-api** | `LocalConversationRepository` (in-memory or JSON file) | `CosmosConversationRepository` (Cosmos DB) |
| **seed-data** | N/A (CSV files are the source) | Runs once to seed Cosmos DB |

---

## 4. Component Architecture

### 4.1 CRM API (`crm-api`)

**Local Mode:**
- Repository: `LocalCrmRepository` reads `data/contoso-crm/*.csv` files
- Each CSV is parsed at startup into `List<T>` in memory
- All queries filter the in-memory collections
- No database calls, no credentials needed

**Azure Mode:**
- Repository: `AzureCrmRepository` reads from Azure Cosmos DB
- Uses `DefaultAzureCredential` for authentication

**Configuration (`appsettings.Local.json`):**
```json
{
  "DataMode": "Local"
}
```

**Implementation pattern:**
```csharp
builder.Services.AddScoped<ICrmRepository>(sp =>
{
    var dataMode = builder.Configuration["DataMode"] ?? "Local";
    return dataMode == "Azure"
        ? new AzureCrmRepository(cosmosClient)
        : new LocalCrmRepository();
});
```

---

### 4.2 Knowledge MCP (`knowledge-mcp`)

**Local Mode:**
- `LocalVectorSearchService` loads PDFs/TXTs from `data/contoso-sharepoint/` at startup
- Each document is chunked (~512 tokens) and embedded using Foundry's `text-embedding-3-small`
- Embeddings are cached in memory
- Search queries are embedded and matched via cosine similarity

**Azure Mode:**
- `AzureSearchService` queries the Azure AI Search index with semantic ranking

**Configuration (`appsettings.Local.json`):**
```json
{
  "DataMode": "Local",
  "Foundry": {
    "Endpoint": "https://aif-dotnetagent-localdev-centralus.cognitiveservices.azure.com/",
    "ApiKey": "<generated-by-setup-local.ps1>",
    "EmbeddingDeploymentName": "text-embedding-3-small"
  }
}
```

---

### 4.3 CRM Agent, Product Agent, Orchestrator Agent

**Local Mode:**
- Use `AzureOpenAIClient` with `AzureKeyCredential` (API key from Foundry)
- Call CRM MCP and Knowledge MCP over HTTP (`localhost:500X`)

**Azure Mode:**
- Use `AIProjectClient` with `DefaultAzureCredential`

**Configuration (`appsettings.Local.json`):**
```json
{
  "Foundry": {
    "Endpoint": "https://aif-dotnetagent-localdev-centralus.cognitiveservices.azure.com/",
    "ApiKey": "<generated-by-setup-local.ps1>",
    "DeploymentName": "gpt-4.1"
  },
  "CrmMcp": { "BaseUrl": "http://localhost:5002" },
  "KnowledgeMcp": { "BaseUrl": "http://localhost:5003" }
}
```

---

### 4.4 BFF API (`bff-api`)

**Local Mode:**
- **Conversation persistence:** `LocalConversationRepository` (in-memory or JSON file)
- **CRM API proxy:** `http://localhost:5001`
- **Image proxy:** Serves images from `data/contoso-images/` directory
- **User auth:** Dev bypass (synthetic `ClaimsPrincipal` from header)

**Azure Mode:**
- **Conversation persistence:** `CosmosConversationRepository` (Cosmos DB)
- **Image proxy:** Azure Blob Storage with managed identity
- **User auth:** MSAL + Entra ID

**Configuration (`appsettings.Local.json`):**
```json
{
  "DataMode": "Local",
  "CrmApi": { "BaseUrl": "http://localhost:5001" },
  "Auth": { "DevMode": true }
}
```

**Dev Auth Middleware:**
```csharp
if (app.Configuration.GetValue<bool>("Auth:DevMode"))
{
    app.Use(async (context, next) =>
    {
        var customerId = context.Request.Headers["X-Dev-Customer-Id"]
            .FirstOrDefault() ?? "101";
        var claims = new[] {
            new Claim(ClaimTypes.NameIdentifier, $"dev-user-{customerId}"),
            new Claim("customer_id", customerId),
            new Claim(ClaimTypes.Name, "Dev User")
        };
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "DevAuth"));
        await next();
    });
}
```

---

### 4.5 Blazor UI (`blazor-ui`)

**Local Mode:**
- Skips MSAL authentication
- Shows a dev customer selector dropdown (IDs 101–108)
- Passes selected customer ID to BFF via `X-Dev-Customer-Id` header

**Azure Mode:**
- Uses MSAL for Entra ID authentication

**Configuration (`appsettings.Local.json`):**
```json
{
  "Auth": { "DevMode": true }
}
```

---

## 5. Configuration Files

All `appsettings.Local.json` files are auto-generated by `setup-local.ps1` and are gitignored. Commit `appsettings.Local.json.template` files instead for documentation.

### Emulator Connection Strings (Well-Known Values)

These are constant across all local dev installations (from .NET emulator specifications):

```json
{
  "Cosmos": "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==",
  "Azurite": "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1"
}
```

These are included in the template files for reference but are not used in local dev mode (in-memory repos don't need them).

---

## 6. Setup Script Summary: `infra/setup-local.ps1`

The setup script does 7 things:

1. **Check prerequisites** — .NET 9, Azure CLI, Terraform, Cosmos Emulator, Azurite
2. **Deploy Foundry** — `terraform apply` in `infra/terraform/local-dev/`
3. **Retrieve Foundry config** — Endpoint, API key, deployment names via `terraform output`
4. **Generate appsettings.Local.json** — For all 8 components with correct Foundry credentials
5. **Start emulators** — Cosmos DB Emulator + Azurite (if not running)
6. **Seed data** — Run `src/seed-data` to populate in-memory repos
7. **Print port map** — Show the developer where everything is running

**Options:**
- `./infra/setup-local.ps1` — Full setup (all 7 steps)
- `./infra/setup-local.ps1 -SkipTerraform` — Skip Foundry deployment (if already deployed)
- `./infra/setup-local.ps1 -SkipSeed` — Skip data seeding (if already seeded)

---

## 7. Port Map

| Service | Port | Protocol | Notes |
|---------|------|----------|-------|
| Cosmos DB Emulator | 8081 | HTTPS | Self-signed cert (not used in local dev) |
| Azurite Blob | 10000 | HTTP | (not used in local dev) |
| crm-api | 5001 | HTTP | REST API |
| crm-mcp | 5002 | HTTP | MCP server |
| knowledge-mcp | 5003 | HTTP | MCP server |
| crm-agent | 5004 | HTTP | Agent endpoint |
| product-agent | 5005 | HTTP | Agent endpoint |
| orchestrator-agent | 5006 | HTTP | Agent endpoint |
| bff-api | 5007 | HTTPS | Backend-for-Frontend |
| blazor-ui | 5008 | HTTPS | SPA |
| **Aspire Dashboard** | **15000** | **HTTPS** | **Monitoring all components** |

---

## 8. Summary: What Changes vs. Full Azure

### When Running Locally

- ✅ All 8 components run as separate processes (or via Aspire orchestration)
- ✅ Data lives in memory (CSVs loaded at startup)
- ✅ No emulators needed (repository pattern handles everything)
- ✅ Foundry deployed to Azure (real API calls)
- ✅ Dev auth bypass (no MSAL, just customer ID header)
- ✅ Dashboard at `localhost:15000` shows all components

### When Deploying to Azure

- ✅ Same codebase (no code changes, only config changes)
- ✅ CRM API points to Cosmos DB (RBAC auth)
- ✅ Knowledge MCP points to Azure AI Search (semantic ranking)
- ✅ BFF persists conversations to Cosmos DB
- ✅ MSAL authentication (real Entra ID users)
- ✅ No dashboard (production-grade monitoring via Application Insights)

### Key Implementation Principle

**Configuration, not code.** Each component detects its deployment mode from `DataMode` configuration:
- Local: Repositories use in-memory data
- Azure: Repositories use Azure services
- Same business logic in both modes

This ensures local dev is always testing the actual production code paths.

---

## Next Steps

1. **Read the quick-start guide:** [Local Development Guide](local-development.md)
2. **Run setup:** `./infra/setup-local.ps1`
3. **Start the system:** `dotnet run --project src/AppHost`
4. **Open dashboard:** `https://localhost:15000`
5. **Open UI:** `https://localhost:5008`

See [README.md](../README.md) for more information on architecture and deployment paths.
