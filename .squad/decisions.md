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

**Decision (Joe):** Terraform kubectl provider DNS resolution failure when AKS cluster is destroyed/missing requires two-layered fix. (1) Provider config (providers.tf): Wrap AKS output references with `try()` guards to handle fresh deploys and missing clusters. (2) Deploy scripts (deploy.ps1/deploy.sh): Add pre-plan kubectl state guard that detects destroyed AKS via `az aks show` and removes stale `kubectl_manifest.*` resources from state. Resources idempotently recreate on next apply. No impact to normal deployments (AKS exists, state valid). Alternatives considered (alekc/kubectl provider switch, two-phase apply with -target, separate Terraform root) deferred as more invasive.

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

### 2026-03-23T15:12: User directive
**By:** Almir Banjanovic (via Copilot)
**What:** Helm charts and Dockerfiles for each src component live inside their respective component folder in src/ (e.g., src/crm-api/Dockerfile, src/crm-api/chart/). Not in a separate infra location.
**Why:** User request — captured for team memory

### 2026-03-23T15:16: User directive
**By:** Almir Banjanovic (via Copilot)
**What:** Fix all audit findings in staged order: Critical → High → Medium → Implementation. Test each stage and get user approval before moving to next stage. Each task must be committed to git separately (one commit per task).
**Why:** User request — captured for team memory

# Decision: Tenant ID via AzureAd:TenantId (simplified)

## Context
The original fix for DefaultAzureCredential tenant mismatch introduced `AZURE_TENANT_ID` as a separate config key. This was redundant — the tenant ID already existed in Key Vault as `AzureAd__TenantId`, and config-sync already pulls all secrets including that one. Having two keys for the same value violated single-source-of-truth.

## Decision
All projects using `DefaultAzureCredential` read the tenant ID from `configuration["AzureAd:TenantId"]` — the standard config key already populated by config-sync from Key Vault. No new env vars, no new Key Vault mappings, no `AZURE_TENANT_ID` references. Config-sync itself uses plain `DefaultAzureCredential()` (developer must `az login` to the correct tenant).

## Consequences
- Single config key for tenant ID: `AzureAd:TenantId`
- Flow: Key Vault (`AzureAd__TenantId`) → config-sync → appsettings.json → app reads `AzureAd:TenantId`
- All future projects (CRM API, MCP servers, agents, BFF) must follow this pattern
- No duplicate config keys for the same value

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

# Decision: Dockerfile + Helm Chart Base Patterns (T-01)

## Context
No Dockerfiles or Helm charts existed. All 8 services need consistent containerization and K8s deployment patterns before AKS deployment can proceed.

## Decisions Made

### 1. Dockerfile Pattern
- Multi-stage build: SDK 9.0 → publish → aspnet 9.0 runtime
- Non-root execution: `USER app` (UID 1654 from aspnet:9.0)
- PublishReadyToRun enabled for AKS cold start performance
- Health check via `wget` (available in Debian-based aspnet image, no curl needed)
- Build context is repo root (not service directory) so Directory.Build.props is accessible
- OCI labels via build args for registry/scanner integration

### 2. Helm Chart Pattern
- Service accounts default to `create: false` — Terraform owns SA lifecycle
- Workload identity enabled by default (pod label + SA annotation)
- Security context: runAsNonRoot, readOnlyRootFilesystem, drop ALL capabilities
- Writable /tmp via emptyDir (required for ASP.NET temp files with read-only root fs)
- Pod UID 1654 aligns with aspnet:9.0 `app` user across Dockerfile and K8s
- HPA disabled by default, configurable per service
- ConfigMap checksum annotation forces rollout on config changes

### 3. Blazor UI Exception
The Blazor WASM UI will need a modified Dockerfile pattern (nginx or dotnet static file server). The base template works for all 7 backend .NET Minimal API services directly.

## Impact
- All service developers use `infra/templates/README.md` as the starting guide
- Per-service Dockerfiles and Helm charts will be created as each service is built
- Helm charts go under `infra/helm/<service-name>/`

## Status
Implemented — templates committed.

# Decision: NetworkPolicy Manifests for contoso Namespace (T-02)

## Date
2026-03-23

## Author
Joe (DevOps/Infra)

## Context
Security finding F-01 (HIGH): AKS cluster has Azure Network Policy engine enabled but zero NetworkPolicy manifests. All pods in the `contoso` namespace can communicate freely, violating least-privilege network segmentation.

## Decision
Created 9 NetworkPolicy manifests in `infra/k8s/network-policies/`:
- 1 default-deny-all (namespace-wide)
- 8 per-service policies with explicit ingress/egress allowlists

### Team-Relevant Details

1. **Pod label requirement:** All Helm deployments MUST apply `app.kubernetes.io/name: {service-name}` as a pod label (Kubernetes standard). This is the selector used by NetworkPolicy rules. If a pod lacks this label, it gets only the default-deny policy (no traffic allowed).

2. **Port 8080 assumed:** All inter-service egress rules target port 8080 (the .NET 9 non-root default from the Helm templates). If any service changes its listen port, the corresponding NetworkPolicy must be updated.

3. **PE subnet CIDR hardcoded:** Egress to Azure PaaS uses `10.0.3.0/24` (the private endpoint subnet default from the VNet module). If Terraform variables override this CIDR, the network policies must be updated in tandem.

4. **AGC namespace:** Ingress for bff-api and blazor-ui uses `namespaceSelector` targeting `azure-alb-system`. If the ALB Controller is installed in a different namespace, update the selector in both policies.

## Consequences
- Pods can no longer freely communicate — only the documented traffic flow is allowed
- New services added to the namespace need a corresponding NetworkPolicy file
- Helm chart values must include `app.kubernetes.io/name: {service-name}` pod label
- CI/CD should apply these manifests as part of the deployment pipeline

## Status
Implemented and refined post-security review.

# Decision: NetworkPolicy Selectors Aligned to Helm Standard Labels (Label Fix)

## Context
Cleveland's security review identified a label mismatch between NetworkPolicy pod selectors and Helm chart templates (Finding 1, HIGH severity). NetworkPolicies initially used `app: {service-name}` but Helm produces `app.kubernetes.io/name: {service-name}`. Under default-deny, this mismatch would block all inter-service traffic when deployed via Helm — causing a full service outage.

## Options Considered
- **Option A (chosen):** Update NetworkPolicy selectors to `app.kubernetes.io/name` — aligns with Kubernetes standard labels and Helm conventions. Zero changes needed in Helm templates.
- **Option B:** Add custom `app` labels to Helm templates alongside standard labels. Works but adds redundancy and deviates from convention.

## Decision
Option A — all 8 per-service NetworkPolicy files updated to use `app.kubernetes.io/name: {service-name}` as pod selectors and matchLabels. README updated to document the convention. Default-deny policy unchanged (uses `podSelector: {}`, no label dependency).

## Consequences
- NetworkPolicies now match the labels Helm charts naturally produce — no manual label overrides needed in values.yaml
- Services deployed via Helm will correctly have allow rules applied alongside the default-deny baseline
- Any future services must use Helm standard labels (which they will by default via `_helpers.tpl`)
- If any service is deployed outside Helm (e.g., raw kubectl), it must include `app.kubernetes.io/name: {service-name}` in pod labels

## Status
Implemented — committed. Resolves Cleveland security review Finding 1.

# Decision: Agent Identity v2 via msgraph

## Context
The Entra Agent ID platform requires Graph beta types for Agent Identity Blueprints and Blueprint Principals, which the AzureAD provider cannot create. The existing v1 module used regular Entra app registrations/service principals that do not represent the specialized Agent Identity objects. Cosmos DB RBAC for the agents was also missing, leaving only the BFF principal assigned.

## Decision
Move agent identity provisioning to a new `agent-identity/v2` module built on the `microsoft/msgraph` provider and Graph beta endpoints. The lifecycle is: create Agent Identity Blueprint → create Blueprint Principal → runtime Agent Identity instances are created by the blueprint service (not Terraform). Bind the blueprint to AKS service accounts via federated identity credentials for workload identity. Update Cosmos DB RBAC to include CRM, Product, and Orchestrator agent principals alongside the BFF.

## Consequences
Terraform now provisions the correct Entra Agent ID objects, aligning with the platform requirements and enabling runtime agent identity creation. AKS workload identities remain secretless and use OIDC token exchange through FICs. Cosmos DB access is corrected for all agents, reducing authorization gaps.


# High Stage Decisions

# High Stage Decisions — Joe (DevOps/Infra)

## T-05: Pin AKS Kubernetes Version

**Decision:** Added `default = "1.30"` to `aks_kubernetes_version` variable in variables.tf. The dev.tfvars (gitignored) already pins "1.34", but without a default, a bare terraform plan could let AKS auto-select latest GA. This is defense-in-depth — the tfvars value always takes precedence.

**Impact:** No deployment change (dev.tfvars overrides). Prevents accidental version drift if a new environment is created without setting this variable.

## T-06: Dynamic Environment in Approval Gate

**Decision:** Replaced hardcoded "production" in orchestrator workflow approval gate with `${{ inputs.environment }}`. Both issue-title and issue-body now reflect the actual target environment.

**Impact:** Operators deploying to dev or staging will no longer see misleading "Approve deployment to production" prompts.

## T-07: Seed-Data Depends on Terraform Apply

**Decision:** Added `terraform-apply` to the seed-data job's `needs` list alongside `cleanup-after-apply`. Previously, seed-data only depended on cleanup-after-apply (which runs with `if: always()`), so it would execute even after a failed apply.

**Impact:** Seed data will only run when infrastructure was successfully provisioned. Prevents wasted compute and confusing error logs from trying to seed into non-existent resources.

## T-08: CAE Disable Flags in CI/CD

**Decision:** Added `ARM_DISABLE_CAE`, `AZURE_DISABLE_CAE`, and `HAMILTON_DISABLE_CAE` environment variables to all Terraform Init, Plan, and Apply steps in terraform-plan.yaml and terraform-apply.yaml. These match the existing pattern in deploy.ps1/deploy.sh.

**Impact:** Corporate Entra tenants with aggressive Continuous Access Evaluation policies will no longer see intermittent token revocations during long-running Terraform operations in CI/CD. Achieves parity between local deploy scripts and GitHub Actions workflows.

## Status

All 4 tasks implemented as separate commits. CI/CD is now closer to full parity with local deploy scripts. Remaining gap: `TF_VAR_msgraph_*` credentials for Agent Identity Blueprints (Critical finding from audit — deferred, requires architectural decision).


# Cleveland — High Stage Security Review

**Reviewer:** Cleveland (Security Engineer)
**Date:** 2026-03-23
**Commits reviewed:** T-05 (a061410), T-06 (c3a5839), T-07 (ee72793), T-08 (8a50aa4)

---

## T-05: Pin kubernetes_version (a061410)

**Verdict:** ✅ **APPROVED**

- Default `"1.30"` added to `aks_kubernetes_version` in `variables.tf`
- Format `major.minor` is correct for AzureRM `azurerm_kubernetes_cluster` resource
- 1.30 is a current stable AKS version — sensible default
- CI/CD workflow already passes `TF_VAR_aks_kubernetes_version` from GitHub vars, so this default is a safety net, not the primary source
- Description updated to explain pinning rationale

---

## T-06: Fix approval gate hardcode (c3a5839)

**Verdict:** ✅ **APPROVED**

- Hardcoded `"production"` replaced with `${{ inputs.environment }}` in both `issue-title` and `issue-body`
- **Injection risk: NONE.** The `inputs.environment` is defined as `type: choice` with fixed options `[dev, staging, production]` on the `workflow_dispatch` trigger. GitHub enforces the enum — freeform text cannot be submitted
- Even in the theoretical case of injection, the values only flow into a GitHub Issue title/body (not shell commands), so the attack surface is minimal

---

## T-07: Fix seed-data dependency (ee72793)

**Verdict:** ✅ **APPROVED**

- `seed-data.needs` changed from `[cleanup-after-apply]` to `[terraform-apply, cleanup-after-apply]`
- **Why this was needed:** `cleanup-after-apply` runs with `if: always()`, meaning it succeeds even when `terraform-apply` fails. Under the old config, seed-data could run against non-existent infrastructure
- **cleanup-after-apply unaffected:** It still has `needs: [terraform-apply]` with `if: always()`, so firewall cleanup continues to run regardless of apply outcome
- **DAG correctness verified:** plan → cleanup-plan → approval → purge → apply → cleanup-apply → seed-data → cleanup-seed. All dependency chains intact

---

## T-08: CAE disable flags (8a50aa4)

**Verdict:** ✅ **APPROVED**

- Three environment variables added to all Terraform steps that make Azure API calls:
  - `terraform-plan.yaml`: init (line 125-127), plan (line 150-152)
  - `terraform-apply.yaml`: init (line 116-118), apply (line 135-137)
- **Parity with `deploy.ps1` confirmed:** Lines 400-402 set the same three variables (`ARM_DISABLE_CAE`, `AZURE_DISABLE_CAE`, `HAMILTON_DISABLE_CAE`)
- **No missed steps:** `terraform validate` and `terraform fmt -check` are local operations with no Azure API calls — correctly excluded
- **Purpose:** Prevents CAE token revocation (`TokenCreatedWithOutdatedPolicies`) in corporate Entra tenants with aggressive Conditional Access policies

---

## Overall Verdict

### ✅ ALL 4 COMMITS APPROVED

No security vulnerabilities, injection risks, or correctness issues found. All changes are precise, well-scoped, and match the intended behavior. High stage security gate is **cleared**.

---

## Infrastructure & Deployment Decisions

### Terraform kubectl Provider DNS Resolution (Joe)

**Decision (Joe):** Implement a variable-gated provider pattern with two-pass deployment to handle stale kubectl provider state when AKS is destroyed/missing or DNS-unresolved.

**Problem:** The `gavinbunney/kubectl` Terraform provider initializes eagerly, attempting to connect to the Kubernetes API server during provider initialization (before any resource refresh). A stale or unreachable FQDN causes a DNS resolution error that blocks `terraform plan` entirely. Prior `try()` guards were insufficient because they don't catch valid-looking stale FQDNs from state, and ALL providers initialize regardless of whether their resources exist.

**Solution:**
- `var.deploy_k8s_resources` (bool, default=true) gates kubectl provider config AND all `kubectl_manifest` resources
- Provider config uses ternary: when false, provider gets empty credentials (no connection attempt)
- `kubectl_manifest` resources use count/for_each conditions to skip when false
- Deploy scripts perform 3-step AKS reachability check (exists + DNS resolves), then run two-pass deployment if AKS is unreachable

**Impact:**
- Backward compatible: existing CI/CD unchanged (default=true)
- Handles all scenarios: fresh deploy, destroyed AKS, stale FQDN, unreachable cluster
- No performance impact on normal deployments (AKS exists, state valid)
- Deploy scripts auto-detect and adapt transparently

**Commits:** 0b58039, 3dc56f1

---

### Project Folder Structure Reorganization (Stewie)

**Decision (Stewie):** Move infrastructure templates out of `docs/` into `infra/templates/`, and consolidate all Kubernetes manifests under `infra/k8s/`.

**Rationale:**
1. `docs/templates/` contained Dockerfile.template and Helm chart skeleton — infrastructure artifacts, not documentation
2. Kubernetes manifests split between `infra/terraform/manifests/` (Terraform-applied) and `infra/k8s/network-policies/` (manually applied) — confusing and hard to maintain

**Implementation:**
- Move `docs/templates/` → `infra/templates/` — reference Dockerfile and Helm patterns now grouped with infrastructure
- Move `infra/terraform/manifests/` → `infra/k8s/manifests/` — all K8s YAML consolidated under `infra/k8s/`
- Update Terraform `templatefile()` paths from `${path.module}/manifests/` to `${path.module}/../k8s/manifests/`

**Final Structure:**
```
docs/                          # Documentation only
infra/
├── templates/                 # Reference Dockerfile + Helm patterns
├── k8s/                       # All Kubernetes YAML
│   ├── manifests/             # Terraform-applied (namespace, service accounts)
│   └── network-policies/      # Manually applied (NetworkPolicy YAMLs)
└── terraform/                 # Terraform modules and root config
```

**Impact:**
- Developers easily locate infrastructure templates and K8s manifests
- `docs/` is clean — documentation only
- No Terraform functional changes (path updates only)
- File history preserved via `git mv`

---

### Diagnostic Settings for Observability (Joe)

**Decision (Joe):** Key Vault and both Cosmos DB accounts send audit/control plane logs to the AKS Log Analytics workspace via `azurerm_monitor_diagnostic_setting`.

**Implementation:**
- New `diagnostics.tf` in Terraform root — keeps main.tf clean
- Reuses existing Log Analytics workspace from `module.aks.log_analytics_workspace_id`
- No new resources beyond diagnostic settings themselves

**Impact:**
- Centralized observability: audit logs for all data resources flow to AKS workspace
- Clean terraform code organization

---

### Secure-by-Default Module Defaults (Joe)

**Decision (Joe):** Flip all module defaults to secure posture while keeping the current project deployment unchanged.

**Module Changes:**
- `public_network_access_enabled` → `false` (cosmosdb, keyvault, acr, foundry modules)
- `purge_protection_enabled` → `true` (keyvault module)
- NSGs added to VNet module for private endpoint and AGC subnets

**Project Root:**
- Root `main.tf` explicitly passes `public_network_access_enabled = true` where deploy pipeline requires it

**Impact:**
- Module reuse: anyone using modules without overrides gets locked-down defaults
- Current project: unchanged (explicit overrides in root)
- Better security posture for the framework
- Trade-off acknowledged: foundry module previously had no `public_network_access_enabled` variable at all — added for consistency

---

### AI Search Admin Key — Accepted Interim Risk (Cleveland)

**Decision (Cleveland):** Accept interim risk of AI Search admin API key in `local-exec` until Azure adds RBAC support for data-plane API.

**Rationale:** Azure AI Search data-plane API (2025-11-01-preview) does not support RBAC — admin key authentication is the only current option for `local-exec` provisioning. The key is not stored in code or config; CI/CD uses OIDC with no key exposure.

**Mitigation:** Documented in `docs/security.md` under "Known Gaps"

**Future:** Revisit when Azure adds RBAC data-plane support

**Status:** Accepted workshop pattern, documented for future hardening

---

### Test User Passwords — Accepted Lab-Only Pattern (Cleveland)

**Decision (Cleveland):** Test user passwords are visible in `terraform plan` output via `nonsensitive()` in the `keyvault-secrets` module — accepted workshop pattern for lab environments.

**Justification:**
- 5 test accounts (Emma, James, Sarah, David, Lisa) are lab-only for the development workshop
- Pattern is acceptable for non-persistent environments

**Rotation Plan:** If test users persist beyond the workshop, rotate all passwords and remove `nonsensitive()` to revoke plan visibility

**Status:** Accepted workshop pattern, documented in `docs/security.md` under "Known Gaps"

