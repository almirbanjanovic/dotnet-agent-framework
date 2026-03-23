# Project Context

- **Owner:** Almir Banjanovic
- **Project:** .NET Agent Framework — 8-container agentic AI system with Contoso Outdoors (Blazor WASM UI, BFF API, CRM API, CRM MCP, Knowledge MCP, CRM Agent, Product Agent, Orchestrator Agent)
- **Stack:** .NET 9, Minimal APIs, Blazor WebAssembly, MudBlazor, ModelContextProtocol C# SDK, Microsoft.Agents.AI, Azure.AI.OpenAI, Cosmos DB, Azure AI Search, Terraform, AKS, Helm, Docker
- **Created:** 2026-03-19

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

### 2026-03-19 — Full Backend Analysis (Read-Only)

**Current state**: Lab 1 complete. Infrastructure fully provisioned via Terraform. No backend application code exists yet — only 3 utility projects.

**Existing projects (src/)**:
- `src/simple-agent/` — Azure OpenAI validation console app (Azure.AI.OpenAI 2.1.0, Microsoft.Agents.AI 1.0.0-rc2)
- `src/config-sync/` — Key Vault → `src/appsettings.json` bridge (Azure.Security.KeyVault.Secrets 4.7.0)
- `src/seed-data/` — CSV → Cosmos DB seeder (Microsoft.Azure.Cosmos 3.46.1, CrmSeeder.cs)

**Planned projects (not yet created)**:
- `src/crm-api/` — .NET Minimal API, 11 endpoints (customers, orders, products, promotions, support tickets)
- `src/crm-mcp/` — MCP Server, 10 tools wrapping CRM API endpoints
- `src/knowledge-mcp/` — MCP Server, 1 tool: search_knowledge_base (Azure AI Search)
- `src/crm-agent/` — CRM specialist agent (Microsoft.Agents.AI + Azure.AI.OpenAI)
- `src/product-agent/` — Product specialist agent
- `src/orchestrator-agent/` — Intent classification + routing
- `src/bff-api/` — JWT validation, CRM proxy, image proxy, chat, Cosmos DB conversation persistence
- `src/blazor-ui/` — Blazor WASM SPA (Lois's domain)

**Cosmos DB containers (CRM)**:
- `Customers` (PK: /id) — 10 customers, loyalty tiers Bronze/Silver/Gold/Platinum
- `Orders` (PK: /customer_id) — 12 orders, statuses: shipped/delivered/processing/returned
- `OrderItems` (PK: /order_id) — 19 line items
- `Products` (PK: /id) — 15 products across footwear/clothing/tents/backpacks/accessories/cooking
- `Promotions` (PK: /id) — 5 active promotions with tier eligibility
- `SupportTickets` (PK: /customer_id) — 5 tickets (4 open, 1 closed)

**Cosmos DB containers (Agents)**:
- `conversations` (PK: /sessionId) — conversation persistence, BFF sole writer/reader

**Knowledge base**: 12 PDFs in AI Search (5 guides, 4 policies, 3 procedures) — auto-vectorized via Knowledge Source

**Key Vault secrets**: 18 mapped (OpenAI, Cosmos×2, Storage, Search, Entra)

**Config pattern**: appsettings.json (gitignored) + env vars, ConfigurationBuilder priority order

**Auth**: 3-layer — User JWT (Entra/MSAL/PKCE), Managed Identity (DefaultAzureCredential), Agent Identity (Entra Agent ID)

**Infrastructure**: Terraform 20 modules + RBAC modules, deployed via deploy.ps1 (7 phases) or GitHub Actions

**Endpoint inventory (planned for CRM API — 11 endpoints)**:
1. GET /customers — all customers
2. GET /customers/{id} — customer detail
3. GET /customers/{id}/orders — customer orders
4. GET /orders/{id} — order detail (with line items JOIN)
5. GET /products — search/browse products (query, category, in_stock_only)
6. GET /products/{id} — product detail
7. GET /promotions — all active promotions
8. GET /promotions/eligible/{customerId} — tier-filtered promotions
9. GET /customers/{id}/support-tickets — customer tickets (open_only filter)
10. POST /support-tickets — create support ticket
11. (possible 11th: GET /orders/{id}/items or similar)

**MCP tools (planned for CRM MCP — 10 tools)**:
get_all_customers, get_customer_detail, get_customer_orders, get_order_detail, get_products, get_product_detail, get_promotions, get_eligible_promotions, get_support_tickets, create_support_ticket

**Data files**:
- `data/contoso-crm/` — 6 CSV files
- `data/contoso-sharepoint/` — 12 PDFs + 12 TXT source files (guides/, policies/, procedures/)
- `data/contoso-images/` — 15 PNG product images

### 2026-03-19 — Cross-Team Finding: Full Codebase Analysis Complete

**Team Update (from all 5 agents):** Architecture is fully specced and infrastructure is provisioned, but **zero application code exists yet.** This is the intended state at end of Phase 1 (infrastructure/tooling complete). All 5 agents confirm the critical path: CRM API is the foundation — all downstream components depend on it. No fundamental re-design needed. All decisions merged into `.squad/decisions.md` with consensus on next steps.

### 2026-03-19 — .NET Build Foundation Established

**What:** Created the centralized build foundation before any new projects are added. Four deliverables:

1. **`Directory.Build.props`** (repo root) — Centralizes `TargetFramework=net9.0`, `LangVersion=latest`, `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`. All 3 existing .csproj files cleaned of redundant properties — they now contain only `OutputType`, `RootNamespace`, project-specific properties, and package references. Every future project inherits these automatically.

2. **`global.json`** (repo root) — Pins SDK to `9.0.100` with `rollForward: latestFeature`. Ensures consistent builds across machines and CI.

3. **`.editorconfig`** (repo root) — Comprehensive .NET code style: file-scoped namespaces (warning), `var` for apparent types, PascalCase public members, `_camelCase` private fields, camelCase params/locals, expression-bodied members, pattern matching, using directives outside namespace with System first. Enforces naming conventions at warning level.

4. **NuGet package updates** (all 3 projects):
   - `Azure.Identity`: 1.13.2/1.14.2 → **1.19.0** (all projects)
   - `Azure.Security.KeyVault.Secrets`: 4.7.0 → **4.9.0** (config-sync)
   - `Microsoft.Azure.Cosmos`: 3.46.1 → **3.57.1** (seed-data)
   - `Microsoft.Extensions.Configuration.*`: 10.0.3 → **10.0.5** (seed-data, simple-agent)
   - `Microsoft.Extensions.AI.OpenAI`: 10.3.0 → **10.4.1** (simple-agent)
   - `Azure.AI.OpenAI`: stayed at **2.1.0** (already latest stable)
   - `Microsoft.Agents.AI/AI.OpenAI`: left at **1.0.0-rc2** (pre-release, do not touch)

**Build result:** `dotnet build dotnet-agent-framework.sln` — 0 errors, 0 warnings with `TreatWarningsAsErrors=true`.

**Why this matters:** Every new project (CRM API, MCP servers, agents, BFF) automatically inherits the centralized build properties. No copy-paste of TargetFramework/Nullable/ImplicitUsings. Warnings-as-errors catches issues at compile time. Consistent code style from day one.

### 2026-03-19 — Fixed TypeLoadException in simple-agent (Microsoft.Extensions.AI version skew)

**Problem:** `dotnet run` on `src/simple-agent/` threw `System.TypeLoadException: Could not load type 'Microsoft.Extensions.AI.FunctionApprovalRequestContent'` from `Microsoft.Extensions.AI.Abstractions`.

**Root cause:** `Microsoft.Agents.AI` 1.0.0-rc2 pulled `Microsoft.Extensions.AI` transitively at **10.3.0**, while `Microsoft.Extensions.AI.OpenAI` 10.4.1 pulled `Microsoft.Extensions.AI.Abstractions` at **10.4.1**. The version skew meant `FunctionInvokingChatClient` (from the 10.4.1 Abstractions surface) referenced `FunctionApprovalRequestContent` which didn't exist in the 10.3.0 `Microsoft.Extensions.AI` assembly.

**Fix:** Added explicit package references to `simple-agent.csproj`:
- `Microsoft.Extensions.AI` → **10.4.1**
- `Microsoft.Extensions.AI.Abstractions` → **10.4.1**

This forces NuGet to resolve all three AI extension packages to 10.4.1, overriding the 10.3.0 transitive from `Microsoft.Agents.AI` rc2.

**Verification:** Solution builds clean (0 errors, 0 warnings). `dotnet run` starts the agent, creates `FunctionInvokingChatClient` without TypeLoadException, and only fails at the Azure OpenAI HTTP call (expected — tenant config issue, not a code bug).

**Lesson:** When using `Microsoft.Agents.AI` (pre-release), always pin `Microsoft.Extensions.AI` and `Microsoft.Extensions.AI.Abstractions` explicitly to match the version of `Microsoft.Extensions.AI.OpenAI`. The rc2 SDK's transitive dependency on 10.3.0 causes version skew with newer AI extension packages. This will likely be fixed in the GA release.

### 2026-03-19 — Fixed Azure Tenant Mismatch in DefaultAzureCredential (all 3 projects)

**Problem:** `DefaultAzureCredential()` picked up a token from the Microsoft corp tenant (`72f988bf-86f1-41af-91ab-2d7cd011db47`) instead of the project tenant (`7960be14-fc91-4f30-8ca1-237851909103`). This happened because VS credential or other ambient credential sources defaulted to the wrong tenant. The Azure OpenAI endpoint rejected the token with HTTP 400 "Tenant provided in token does not match resource token."

**Fix (v1 — superseded by v2 below):** Added `AZURE_TENANT_ID` as a separate config key. This was redundant because the tenant ID already existed in Key Vault as `AzureAd__TenantId` and config-sync already pulls it.

### 2026-03-19 — Simplified Tenant ID to Single Config Key (AzureAd:TenantId)

**Problem with v1:** The previous fix introduced `AZURE_TENANT_ID` as a duplicate config key when the tenant ID was already available via `AzureAd__TenantId` from Key Vault (mapped to `AzureAd:TenantId` in .NET config). Config-sync was also given an unnecessary extra Key Vault mapping for `AZURE-TENANT-ID`.

**Fix (v2):**
- `src/simple-agent/Program.cs` — reads `AzureAd:TenantId` from configuration (already populated by config-sync)
- `src/seed-data/Program.cs` — same pattern, reads `AzureAd:TenantId`
- `src/config-sync/Program.cs` — reverted to plain `new DefaultAzureCredential()`, removed `AZURE-TENANT-ID` mapping. Config-sync already maps `ENTRA-TENANT-ID` → `AzureAd__TenantId` which covers the tenant ID. Config-sync runs with `az login` against the correct tenant; no special handling needed.

**Lesson:** Don't create parallel config keys for the same value. The tenant ID flows: Key Vault (`AzureAd__TenantId`) → config-sync → appsettings.json (`AzureAd:TenantId`) → app reads `configuration["AzureAd:TenantId"]`. One key, one path. All future projects should read `AzureAd:TenantId` from configuration and pass it to `DefaultAzureCredentialOptions.TenantId`.

### 2026-03-19 — Fixed Deploy Scripts: Tenant ID Env Var for .NET Apps

**Problem:** After the C# apps (simple-agent, seed-data) were updated to read tenant ID from `AzureAd:TenantId` (env var `AzureAd__TenantId`), the deploy scripts (`deploy.sh`, `deploy.ps1`) still passed `AZURE_TENANT_ID` when invoking `dotnet run` for seed-data. The apps silently ignored the old variable, meaning `DefaultAzureCredential` could pick up the wrong tenant.

**Fix:** Changed 4 lines across 2 deploy scripts:
- `infra/deploy.sh` line 781: `AZURE_TENANT_ID="$TENANT_ID"` → `AzureAd__TenantId="$TENANT_ID"` (seed-data run)
- `infra/deploy.sh` line 834: same change (Entra user linking run)
- `infra/deploy.ps1` line 800: `$env:AZURE_TENANT_ID` → `$env:AzureAd__TenantId` (seed-data run)
- `infra/deploy.ps1` line 847: same change (Entra user linking run)

**Not touched:** `deploy.sh` line 234 and `deploy.ps1` line 393 — those set `AZURE_TENANT_ID` for Azure CLI usage, not for .NET app config.

**Lesson:** When renaming config keys in application code, always grep the deploy/infra scripts for the old key name. Env vars passed inline to `dotnet run` must match the .NET configuration binding pattern (`Section__Key`).

### 2026-03-23 — CRM API (Component 1) Fully Implemented

**What:** Built the complete CRM API — the foundation service that all other components depend on. 11 REST endpoints serving CRM data from Cosmos DB, plus health check endpoints.

**Project structure:**
```
src/crm-api/
├── Program.cs              — Composition root (DI, middleware, endpoints)
├── Contoso.CrmApi.csproj   — .NET 9 Minimal API (SDK-inherited props)
├── appsettings.template.json
├── Models/                 — 6 entity models + CreateTicketRequest DTO
├── Services/               — ICosmosService + CosmosService (Cosmos DB SDK)
├── Endpoints/              — 5 endpoint groups (extension method pattern)
├── Middleware/              — GlobalExceptionHandler (ProblemDetails RFC 9457)
├── Dockerfile              — Multi-stage, non-root, port 8080
├── chart/                  — Helm chart (from helm-base template)
└── deploy.ps1              — Build → ACR push → Helm deploy
```

**Endpoints (11):**
1. GET /api/v1/customers
2. GET /api/v1/customers/{id}
3. GET /api/v1/customers/{id}/orders
4. GET /api/v1/orders/{id} (cross-partition query)
5. GET /api/v1/orders/{id}/items
6. GET /api/v1/products (query, category, in_stock_only filters)
7. GET /api/v1/products/{id}
8. GET /api/v1/promotions
9. GET /api/v1/promotions/eligible/{customerId}
10. GET /api/v1/customers/{id}/tickets (open_only filter)
11. POST /api/v1/tickets (with validation)

**Key patterns established:**
- Cosmos DB partition keys: /id (Customers, Products, Promotions), /customer_id (Orders, SupportTickets), /order_id (OrderItems)
- DefaultAzureCredential with tenant pinning from AzureAd:TenantId
- JSON console logging for AKS structured log aggregation
- ProblemDetails (RFC 9457) for all error responses with traceId
- Health checks: /health (liveness, always 200), /ready (readiness, Cosmos DB connectivity)
- CosmosSerializationOptions with CamelCase (matches seed-data's camelCase property naming)
- Newtonsoft.Json explicit dependency required by Microsoft.Azure.Cosmos 3.46.1

**Build verification:** `dotnet build` succeeds at both project and solution level — 0 errors, 0 warnings with TreatWarningsAsErrors=true.

**NuGet versions locked:** Azure.Identity 1.19.0, Microsoft.Azure.Cosmos 3.46.1, Microsoft.Extensions.Http.Resilience 10.4.0, Newtonsoft.Json 13.0.3.

**Lesson:** Microsoft.Azure.Cosmos 3.46.1 requires explicit Newtonsoft.Json >= 10.0.2 reference (enforced via MSBuild target, not just NuGet dependency resolution). Add `<PackageReference Include="Newtonsoft.Json" Version="13.0.3" />` to any project using this Cosmos SDK version.

**Lesson:** The `dotnet new webapi` scaffold generates properties already in Directory.Build.props (TargetFramework, Nullable, ImplicitUsings). Always strip these from the generated csproj and keep only project-specific properties + package references.
