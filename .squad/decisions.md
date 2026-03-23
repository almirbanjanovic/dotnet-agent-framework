# Squad Decisions

## Active Decisions

### Architecture & Component Design

**Decision (Stewie):** Production-grade architecture is sound. 8-container microservice design with clean boundaries, proper DAG dependency flow, and well-defined communication patterns. No fundamental redesign needed. Implementation should follow the specified order: CRM API → MCP Servers → Agents → BFF → UI.

**Decision (Stewie):** Solution file will need solution folders when 8 containers are added. Organize by domain: `Domain APIs`, `MCP Servers`, `Agents`, `Frontend` to maintain clarity as the project grows.

**Decision (Stewie):** Resilience strategy must be defined before implementing inter-container communication. Plan for circuit breakers (Polly), timeout policies, retry with exponential backoff, and health checks (`/healthz`) on every service for Kubernetes readiness probes.

**Decision (Brian):** CRM API is the critical path. No other backend component can progress until it exists. Build CRM API first (11 endpoints, Cosmos DB integration, cross-partition query handling).

**Decision (Brian):** Error handling standards must be established before CRM API implementation. Define a standard error response model with consistent HTTP status codes.

**Decision (Brian):** Agent prompt specifications are not documented. These must be written before CRM Agent, Product Agent, and Orchestrator Agent implementation begins.

**Decision (Brian):** Seed data gap: Business scenario references customers 106 (Mike Johnson), 107 (Anna Roberts), 108 (Tom Garcia) who are missing from customers.csv. Add these three customers to seed data before scenarios 6-8 can be tested.

**Decision (Brian):** Cross-partition query strategy for GET /orders/{id}: Orders are partitioned by `/customer_id` but the endpoint uses order ID. Decision: accept the cross-partition query cost (small dataset, acceptable). Document as a known trade-off.

### Backend & Data

**Decision (Brian):** .NET Build Foundation established. Directory.Build.props centralizes TargetFramework, LangVersion, Nullable, ImplicitUsings, and TreatWarningsAsErrors. global.json pins SDK to 9.0.100. .editorconfig enforces .NET coding conventions. TreatWarningsAsErrors=true is global. All 5 developers' new projects automatically inherit correct settings. Build verified clean.

**Decision (Brian):** Configuration strategy for local dev is sound (Key Vault → appsettings.json via config-sync). Acceptable trade-off that all 8 containers see all secrets locally — this is a workshop framework, not production. In AKS, Helm values naturally scope per chart.

**Decision (Brian):** No API versioning strategy is defined. Recommend URL prefix versioning (`/api/v1/`) from the start so CRM MCP tools and other consumers are prepared for schema changes.

**Decision (Brian):** Health check endpoints must be added to CRM API and BFF API. Implement `/health` (liveness) and `/ready` (readiness with dependency checks) for Kubernetes integration.

**Decision (Brian):** Logging strategy is missing. For 8 containers in AKS, implement: structured logging (ILogger + JSON), correlation IDs across services, and OpenTelemetry or Application Insights integration.

**Decision (Brian):** Rate limiting should be added to BFF API chat endpoint to prevent Azure OpenAI cost spikes from untrusted callers.

### Frontend & UI

**Decision (Lois):** Blazor WASM UI is fully specified but has zero implementation. Start with scaffolding (dotnet new blazorwasm + MudBlazor + MSAL + SignalR.Client + Markdig packages, Dockerfile). Priority order: Auth → Shell → Chat Core → SignalR → State → Error Handling → Accessibility → Testing → Polish.

**Decision (Lois):** Image URL rewriting pattern is well-defined. Markdown images (`![alt](filename.png)`) must be rewritten by ChatMessage.razor to BFF proxy URLs (`/api/images/{filename}`). Implement with Markdig post-processing or custom extension.

**Decision (Lois):** State management in Blazor WASM should use scoped services (not Fluxor/Redux). The app is focused (chat + data views), so simple service injection (ConversationState) is sufficient. Events flow up via EventCallback, state flows down via parameters.

### Testing & Quality

**Decision (Peter):** Zero tests exist today. This is acceptable at infrastructure/tooling phase. When application code lands, tests must land simultaneously. Never implement a feature without tests.

**Decision (Peter):** CrmSeeder is the only immediately testable logic (218 lines). Unit tests should be added for CSV parsing, type conversion, and Entra ID mapping before any other work begins. This guards the critical data path.

**Decision (Peter):** Test framework consensus: xUnit + FluentAssertions + NSubstitute for unit/integration tests. WebApplicationFactory for API integration tests. bUnit for Blazor component rendering. None of these are referenced yet; add to solution when first test project is created.

**Decision (Peter):** Per-component test priority: CRM API (most logic) → BFF API (security boundary) → Orchestrator (routing correctness) → MCP Servers (tool contracts) → Agents (behavior with mocks) → Blazor UI (component rendering).

### Infrastructure & Deployment

**Decision (Joe):** 21 Terraform modules are production-grade and require no redesign. Dual-track identity model (managed identities + Entra Agent IDs) is clean and forward-looking. Module versioning (v1/v2 convention) is correct.

**Decision (Joe):** No Dockerfiles exist. This is a blocking issue before AKS deployment. All 8 components need multi-stage Dockerfiles. Create alongside application code, not after.

**Decision (Joe):** No Helm charts exist. K8s deployment requires Helm chart (or set of charts per component) with proper Deployments, Services, Ingress rules, ConfigMaps, Secrets, resource limits, health probes, and HPA configuration.

**Decision (Joe):** AKS Contributor role assigned to control plane identity is too broad. Scope to specific resources or create custom role with minimal permissions (cluster upgrade, node pool management).

**Decision (Joe):** Cosmos DB and AI Search use API keys in some integration paths. Long-term: migrate to full Azure AD authentication to eliminate key dependency and reduce secret management burden.

**Decision (Joe):** K8s security posture is incomplete. Add: NetworkPolicy rules for pod-to-pod isolation, PodSecurityStandards (restricted profile), and OPA/Gatekeeper for admission control.

**Decision (Joe):** EventGrid module removed. Module defined auto-indexing via blob trigger but was never instantiated in main.tf. Security anti-pattern: passed AI Search admin API key as plaintext in Logic App action. If auto-indexing is needed later, re-implement with managed identity auth. Module count: 21 → 20. No infrastructure impact (never deployed).

**Decision (Joe):** Deployment pipeline (deploy.ps1/sh) is well-designed with safety guardrails (firewall bracketing, policy diagnostics, soft-delete purge). No changes needed.

**Decision (Joe):** Bootstrap scripts support local dev and GitHub Actions CI/CD. OIDC federation is used everywhere (zero stored credentials). Security posture is strong. No changes needed.

### Cross-Team

**Decision (Whole Team):** Architecture is specced, infrastructure is provisioned, zero application code exists. This is the intended state at end of Phase 1. All 5 agents agree on the critical path: CRM API first, then MCP servers, then agents, then BFF, then UI. No fundamental redesign needed.

**Decision (Whole Team):** Business scenario is comprehensive and test cases are deterministic. 8 customer scenarios map to specific data. This is a strength for automated validation.

**Decision (Whole Team):** The RC2 status of Microsoft.Agents.AI SDK should be monitored. Pin the version and abstract the agent construction pattern. Be prepared for breaking changes in GA release.

**Decision (Brian):** Pin Microsoft.Extensions.AI versions explicitly when using Microsoft.Agents.AI SDK. Microsoft.Agents.AI 1.0.0-rc2 declares transitive dependency on Microsoft.Extensions.AI 10.3.0, but when combined with Microsoft.Extensions.AI.OpenAI 10.4.1, NuGet resolves to a version skew that causes runtime TypeLoadException. Solution: explicitly pin Microsoft.Extensions.AI 10.4.1 and Microsoft.Extensions.AI.Abstractions 10.4.1 in all projects referencing this SDK. All future agent projects (CRM Agent, Product Agent, Orchestrator Agent) must include these explicit references. Re-evaluate when Microsoft.Agents.AI reaches GA.

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction
### 2026-03-19T16:15: User directive
**By:** Almir Banjanovic (via Copilot)
**What:** Every squad member must use Claude Opus 4.6 (1M context) (`claude-opus-4.6-1m`) as the default model. This overrides the standard model selection hierarchy (Layer 1 — User Override).
**Why:** User request — captured for team memory

# Decision: AZURE_TENANT_ID in DefaultAzureCredential

## Context
`DefaultAzureCredential()` picks up tokens from the wrong Azure AD tenant when developers have multiple tenant credentials cached (e.g., Visual Studio credential defaulting to Microsoft corp tenant). This causes HTTP 400 errors on Azure OpenAI, Cosmos DB, and Key Vault.

## Decision
All projects using `DefaultAzureCredential` must read an optional `AZURE_TENANT_ID` from configuration and pass it via `DefaultAzureCredentialOptions { TenantId = tenantId }`. When not set, plain `DefaultAzureCredential()` is used (no breaking change). Config-sync maps `AZURE-TENANT-ID` from Key Vault to `AZURE_TENANT_ID` in appsettings.json.

## Consequences
- All 3 existing projects (simple-agent, seed-data, config-sync) now support tenant pinning
- All future projects (CRM API, MCP servers, agents, BFF) must follow this pattern
- Developers set `AZURE_TENANT_ID` env var or rely on config-sync populating appsettings.json
- No hardcoded tenant IDs in source code

# Decision: Scripts & CI/CD Audit Findings

## Context
Comprehensive audit of all deployment automation: bootstrap scripts (init.ps1/sh), deploy scripts (deploy.ps1/sh), and 6 GitHub Actions workflows.

## Key Decisions Needed

### 1. CI/CD Agent Identity Gap (Critical)
The deploy scripts create a temporary SP + secret for the msgraph provider (Agent Identity Blueprints). The CI/CD workflows have no equivalent. Either:
- Add a pre-apply job that creates a temp secret for the OIDC SP and passes `TF_VAR_msgraph_*` via outputs, OR
- Store the OIDC SP secret in GitHub Secrets (less secure, contradicts zero-stored-creds posture), OR
- Accept that Agent Identity is local-deploy-only for now.

### 2. Approval Gate Hardcodes "Production" (Critical)
`terraform-plan-approve-apply-seed-data.yaml` line 57: `issue-title: "Approve deployment to production"`. Should use `${{ inputs.environment }}`.

### 3. Seed-Data After Failed Apply (Warning)
Orchestrator seed-data job depends on cleanup-after-apply (which always succeeds) but not terraform-apply. If apply fails, seed-data still runs. Fix: add terraform-apply to seed-data's `needs` list.

### 4. CAE Flags Missing From CI/CD (Warning)
Deploy scripts set ARM_DISABLE_CAE, AZURE_DISABLE_CAE, HAMILTON_DISABLE_CAE. Workflows don't. Corporate tenants with aggressive CAE policies could see intermittent Terraform failures.

## Consequences
Until these are addressed, CI/CD cannot fully replace local deploy scripts. The deploy scripts remain the reliable path for full deployments including Agent Identity. The approval gate cosmetic issue could confuse operators deploying to dev/staging.

# Decision: Terraform Module Defaults Need Security Hardening

## Context
Deep audit of all 20 Terraform modules revealed that while the root configuration (main.tf) correctly compensates — passing deployer IPs to firewalls and creating 7 private endpoints — the individual modules are not secure-by-default. If any module is reused outside this project without explicit overrides, PaaS services would be exposed to the public internet.

## Findings
- cosmosdb, keyvault, foundry, acr modules all default `public_network_access_enabled = true`
- Firewall logic in cosmosdb/keyvault/foundry sets `default_action = "Deny"` only when `allowed_ips` is non-empty; empty list = open
- Key Vault `purge_protection_enabled = false` and `soft_delete_retention_days = 7`
- AKS RBAC assigns `Contributor` on entire resource group (should be scoped to cluster)
- VNet module creates no NSGs (subnets have no ingress/egress rules)
- knowledge-source module uses `local-exec` provisioner with API key (fragile, not idempotent)

## Decision
When modules are next updated (not now — this was a read-only audit):
1. Flip module defaults to secure: `public_network_access_enabled = false`, purge protection on, firewall deny-by-default always
2. Replace AKS `Contributor` with `Azure Kubernetes Service Contributor` scoped to cluster resource
3. Add NSG resources to VNet module (at minimum for PE and AGC subnets)
4. Add variable validation rules for SKUs, IP addresses, and Kubernetes versions
5. Fix Cosmos DB RBAC module description ("Data Owner" → "Data Contributor")

## Impact
No immediate deployment impact (root config already compensates). These are defense-in-depth improvements for module reusability and compliance audits.

## Status
Proposed — requires team review before implementation.

# Decision: Agent Identity v2 via msgraph

## Context
The Entra Agent ID platform requires Graph beta types for Agent Identity Blueprints and Blueprint Principals, which the AzureAD provider cannot create. The existing v1 module used regular Entra app registrations/service principals that do not represent the specialized Agent Identity objects. Cosmos DB RBAC for the agents was also missing, leaving only the BFF principal assigned.

## Decision
Move agent identity provisioning to a new `agent-identity/v2` module built on the `microsoft/msgraph` provider and Graph beta endpoints. The lifecycle is: create Agent Identity Blueprint → create Blueprint Principal → runtime Agent Identity instances are created by the blueprint service (not Terraform). Bind the blueprint to AKS service accounts via federated identity credentials for workload identity. Update Cosmos DB RBAC to include CRM, Product, and Orchestrator agent principals alongside the BFF.

## Consequences
Terraform now provisions the correct Entra Agent ID objects, aligning with the platform requirements and enabling runtime agent identity creation. AKS workload identities remain secretless and use OIDC token exchange through FICs. Cosmos DB access is corrected for all agents, reducing authorization gaps.
