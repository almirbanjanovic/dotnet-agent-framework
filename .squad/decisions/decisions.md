# Decisions & Architecture Directives

## Configuration & Infrastructure

### 2026-03-24 — Unified config naming standard (Architecture decision, Stewie + Almir approval)

**Decision:** Standardized configuration naming across all 8 components and all layers.

- **Key Vault secrets:** `PascalCase--Hierarchy` (e.g., `CosmosDb--CrmEndpoint`)
- **Environment variables** (Helm/K8s): `Section__Key` (e.g., `CosmosDb__Endpoint`)
- **.NET code:** `Section:Key` (e.g., `CosmosDb:Endpoint`)
- **appsettings.json:** nested JSON structure
- **Per-component config:** Each service gets its own `appsettings.json` with only its keys (via config-sync manifest)
- **Development template:** Rename `appsettings.template.json` → `appsettings.Development.json` (checked into git with safe defaults)

**Impact:** Requires Key Vault secret rename in Terraform, config-sync rewrite, CRM API config update. Breaking change — needs `terraform apply`.

**Status:** CRM API (Component 1) complete. Config standard locked in for all future components.

**Pending (other agents):**
- Joe (Infra): Rename KV secrets in Terraform to `PascalCase--Hierarchy` format. Update Helm chart value files.
- All future components: Use hierarchical config keys from day one. config-sync manifest has entries for all 8 components.

---

### 2026-07-25 — Remove unnecessary Key Vault diagnostic import pattern

**Date:** 2026-07-25
**Author:** Joe (DevOps/Infra)
**Status:** Implemented

**Context:** The deploy script (`deploy.ps1`) contained logic to detect an existing Key Vault diagnostic setting (`diag-keyvault-to-law`) and import it into Terraform state via `TF_VAR_import_diag_keyvault_id`. A corresponding `import` block existed in `imports.tf` and a variable declaration in `variables.tf`.

**Problem:** The diagnostic setting is fully declared in `infra/terraform/diagnostics.tf` as `azurerm_monitor_diagnostic_setting.keyvault`. Terraform creates and manages it end-to-end. The detect-and-import pattern is designed for resources that may exist *outside* Terraform (e.g., pre-registered providers, pre-existing Entra users). Applying it to a Terraform-managed resource adds unnecessary complexity, slows down deploys (extra Azure CLI calls), and confuses the ownership model.

**Decision:** Remove the diagnostic import pattern entirely:
1. **`deploy.ps1`** — Remove the ~16-line detection block that queries `az keyvault list` and `az monitor diagnostic-settings list`
2. **`imports.tf`** — Remove the `import` block targeting `azurerm_monitor_diagnostic_setting.keyvault`
3. **`variables.tf`** — Remove the `import_diag_keyvault_id` variable

**Principle:** Only use detect-and-import for resources that can exist outside Terraform. If Terraform declares and creates the resource, it owns the full lifecycle — no import logic needed.

**Impact:**
- Faster deploys (skip unnecessary Azure CLI calls)
- Cleaner deploy scripts and Terraform config
- No behavioral change — Terraform still creates/manages the diagnostic setting

---

## User Directives

### 2026-03-23T15:53 — Folder structure clarity (Almir)

**What:** docs/ folder should only contain documentation. Dockerfile/Helm templates don't belong there. K8s manifests should not be split between infra/terraform/manifests/ and infra/k8s/. Folder structure must be logical and easy to maintain.

**Why:** User request — clear file organization improves maintainability.

---

### 2026-03-23T20:07 — Self-contained, independently deployable components (Almir)

**What:** All 8 components must be fully self-contained and independently deployable. No shared library, no shared NuGet package, no project references between services. Each service copies the common patterns (~50-80 lines) into its own codebase. Copy-paste with discipline. If a service needs to move to another repo, copy the folder and go.

**Why:** True microservice independence. Workshop readability. Shared code surface is too small to justify package infrastructure.

**Decision:** Option A — copy-paste with discipline, no shared library.

**Rationale:** Shared surface is ~50-80 lines. Monorepo makes breaking changes a single-PR grep. 4 distinct component categories (API, MCP, Agent, UI) share very little bootstrap code. NuGet package overhead exceeds benefit. Workshop readability requires visible, self-contained code.

**Impact:** Phase 0 unchanged. CRM API establishes patterns, each service copies what applies.

---

### 2026-03-23T16:18 — Drop T-03 & T-04 (Almir)

**What:** Drop T-03 (deployer Cosmos DB access scoping) and T-04 (KV purge protection) from the audit fix list. This is a workshop project, not a production deployment. Production-only hardening is out of scope.

**Why:** User request — workshop scope prioritizes feature delivery over production RBAC/compliance hardening.

---

### 2026-03-23T16:35 — Drop T-11 (Almir)

**What:** Drop T-11 (Replace AKS Contributor with scoped role). Workshop project — production RBAC scoping is out of scope.

**Why:** User request — workshop scope constraint.

---

### 2026-03-23T16:36 — Drop T-13 (Almir)

**What:** Drop T-13 (resource_provider_registrations). Convenience wins for a workshop — extended registration avoids cryptic provider errors.

**Why:** User request — workshop convenience > production hardening.

---

### 2026-03-23T20:26 — Independent component deploy scripts + master orchestrator (Almir)

**What:** Each component gets its own independent deploy script (deploy.ps1/sh) and CI/CD pipeline (deploy-{service}.yml). Plus a master deploy-all script and workflow that deploys all 8 in parallel. Hybrid timing: CRM API gets its deploy script alongside code (validates pattern), remaining 7 + master orchestrator are batched after all code is written.

**Why:** True independent deployability per component. Master orchestrator for speed.

---

### 2026-04-07T13:16 — Keep logic in Terraform, keep deploy scripts thin (Almir)

**What:** All infrastructure fixes should always work with CI/CD as well as deploy scripts. Keep most logic in Terraform so that deploy scripts don't get huge.

**Why:** User request — ensures deploy.ps1/deploy.sh remain thin orchestration wrappers while Terraform handles resource logic (imports, conditionals, SKU selection, etc.).

---

## Component Reviews

### 2026-03-23 — CRM API (Component 1) Architecture Review (Stewie)

**Verdict:** ⚠️ APPROVED WITH NOTES

**Key Decisions:**
- Cosmos DB models use `System.Text.Json` `[JsonPropertyName]` attributes matching exact CSV column names (snake_case). `CosmosClientOptions` uses `CosmosPropertyNamingPolicy.CamelCase` to align with how seed-data wrote the documents.
- Health check pattern: `/health` (liveness, always 200 — no dependency checks) and `/ready` (readiness — checks Cosmos DB connectivity). Matches Kubernetes probe semantics for all services.
- Endpoint organization uses extension methods (`app.MapCustomerEndpoints()`) with `RouteGroupBuilder` for URL prefix grouping. One file per resource group in `Endpoints/` directory.
- POST /api/v1/tickets generates ticket IDs as `ST-{unix_timestamp_ms}` to avoid collisions without a sequence generator.
- Promotion eligibility computed in-memory (fetch all active, filter by tier hierarchy). Acceptable for 5 promotions.
- Cross-partition query for GET /orders/{id} — accepted trade-off. Orders partitioned by `/customer_id` but endpoint queries by order ID. Small dataset makes acceptable.
- `Newtonsoft.Json 13.0.3` is explicit dependency required by `Microsoft.Azure.Cosmos 3.46.1`.

**Issues Found:**
1. **Service Account Name Mismatch (Blocking Fix):** `values.yaml` sets `serviceAccount.name: "crm-api-sa"` but Terraform provisions `sa-crm-api`. Breaks workload identity. Fix: Change `values.yaml` line 16 to `name: "sa-crm-api"`. (Assigned to Brian)

2. **Unused NuGet Packages (Minor):** `Newtonsoft.Json` and `Microsoft.Extensions.Http.Resilience` are in csproj but never imported. Remove to reduce image size. (Assigned to Brian)

3. **Naming Clarity (Low Priority):** `GetAllPromotionsAsync` filters `WHERE c.active = true` but named "GetAll." Consider renaming to `GetActivePromotionsAsync`. (Assigned to Brian)

**All 11 Endpoints Present & Verified:**
✅ GET `/api/v1/customers` | ✅ GET `/api/v1/customers/{id}` | ✅ GET `/api/v1/orders/{id}` (cross-partition, documented) | ✅ GET `/api/v1/customers/{id}/orders` | ✅ GET `/api/v1/orders/{id}/items` | ✅ GET `/api/v1/products` (search/filter) | ✅ GET `/api/v1/products/{id}` | ✅ GET `/api/v1/promotions` (active only) | ✅ GET `/api/v1/promotions/eligible/{customerId}` (tier-filtered) | ✅ GET `/api/v1/customers/{id}/tickets` | ✅ POST `/api/v1/tickets` (with validation)

---

### 2026-03-23 — CRM API Security Review (Cleveland, Security Engineer)

**Verdict:** ⚠️ APPROVED WITH NOTES

**Checklist Results:** 9/10 passed clean.

| Check | Result |
|-------|--------|
| No secrets in code | ✅ |
| Credential handling (DefaultAzureCredential, tenant pinning) | ✅ |
| Input validation (whitelist, RFC 7807 ProblemDetails) | ✅ |
| Error info leakage | ⚠️ Medium |
| Dockerfile security (multi-stage, non-root, readOnlyRootFilesystem) | ✅ |
| Helm chart security (podSecurityContext, securityContext, emptyDir /tmp) | ✅ |
| CI/CD security (OIDC federation, zero secrets, path-scoped trigger) | ✅ |
| Deploy script (parameterized, az acr login, error handling) | ✅ |
| Health endpoints (/health always 200, /ready checks connectivity) | ✅ |
| appsettings.template.json (safe defaults only) | ✅ |

**Medium Finding:** Exception message leakage in `GlobalExceptionHandler.cs` line 28. `Detail = exception.Message` exposes raw exception messages in ProblemDetails in ALL environments (including Production). Cosmos DB, network, and framework exceptions contain internals (hostnames, connection info). Recommend: Suppress in non-Development:
```csharp
Detail = env.IsDevelopment() ? exception.Message : null,
```
The `title` field already provides context. Exception is logged server-side.

**Severity:** Medium. API is internal (cluster-only, NetworkPolicy), limiting exposure. But defense-in-depth says don't leak internals.

**Additional Notes:**
- No authentication on API endpoints — acceptable (internal microservice, protected by NetworkPolicy). Auth belongs at BFF boundary.
- No max-length validation on string fields in `CreateTicketRequest` — low risk (Cosmos 2MB limit). Consider `[StringLength]` if exposed more broadly.
- Cosmos queries well-constructed — all use `QueryDefinition` with `WithParameter()`, no string interpolation.

---

## Implementation Status: CRM API

**Component 1 — CRM API (Brian):** ✅ Complete
- All 11 endpoints implemented and tested
- Cosmos DB integration with correct partition keys
- RFC 7807 error handling with traceId correlation
- Multi-stage Dockerfile with health checks
- Helm chart with security hardening (runAsNonRoot, readOnlyRootFilesystem, CAE-compatible OIDC)
- CI/CD workflow with path-scoped trigger and OIDC federation
- Deploy script with error handling and parameterization
- Fully self-contained; can be extracted to standalone repo

**Blocking Fix (Before Merge):** Service account name in Helm chart (`sa-crm-api` not `crm-api-sa`)

**Minor Fixes (Before Production):** Remove unused NuGet packages; suppress exception.Message in non-Development ProblemDetails

---

## What This Unblocks

- ✅ Config naming standard locked in for all 8 components
- ✅ Health check pattern (Kubernetes-aligned) for all services
- ✅ Endpoint organization pattern (RouteGroupBuilder, extension methods)
- ✅ Cosmos DB integration pattern (models, serialization, error handling)
- ✅ CI/CD pattern (4-stage: build → test → docker-build-push → helm-deploy)
- ✅ Dockerfile & Helm chart templates
- ✅ Deploy script pattern
- ⏳ CRM MCP Server (Component 2) — can now call the 11 endpoints
- ⏳ BFF API (Component 7) — can proxy CRM data to frontend
- ⏳ Integration tests for all 8 business scenarios
