# Project Context

- **Owner:** Almir Banjanovic
- **Project:** .NET Agent Framework — 8-container agentic AI system with Contoso Outdoors (Blazor WASM UI, BFF API, CRM API, CRM MCP, Knowledge MCP, CRM Agent, Product Agent, Orchestrator Agent)
- **Stack:** .NET 9, Minimal APIs, Blazor WebAssembly, MudBlazor, ModelContextProtocol C# SDK, Microsoft.Agents.AI, Azure.AI.OpenAI, Cosmos DB, Azure AI Search, Terraform, AKS, Helm, Docker
- **Created:** 2026-03-19

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

### 2026-03-19 — Full Infrastructure Analysis

**Module Inventory (21 modules, all v1/):**
- `acr` — Azure Container Registry (Premium, conditional create/reference)
- `agc` — App Gateway for Containers (ALB + Frontend + Subnet Association)
- `agent-identity` — Entra Agent ID blueprints + service principals + FIC for 3 agents (CRM, Product, Orchestrator)
- `aks` — AKS cluster (Azure CNI, system + workload node pools, workload identity, OIDC issuer)
- `cosmosdb` — Cosmos DB account + database + containers (used twice: CRM + Agents)
- `entra` — Entra app registration (SPA/PKCE), Customer app role, 5 test users
- `eventgrid` — Event Grid system topic + Logic App bridge → triggers AI Search indexer on blob upload
- `foundry` — AI Services account + GPT-4.1 chat + text-embedding-3-small deployments
- `identity` — User-assigned managed identities (5: bff, crm_api, crm_mcp, know_mcp, kubelet)
- `keyvault` — Key Vault (RBAC auth, soft-delete, network ACLs)
- `keyvault-secrets` — Bulk secret writer (40+ secrets including endpoints, keys, identity client IDs)
- `knowledge-source` — AI Search knowledge source via REST API (index + data source + skillset + indexer)
- `private-dns-zones` — 6 Private DNS zones (cognitiveservices, cosmosdb, search, blob, keyvault, acr)
- `private-endpoint` — Reusable PE module (used 6 times: 2x Cosmos, Foundry, Search, Storage, KV, ACR)
- `rbac/acr` — AcrPull role
- `rbac/aks` — Contributor for AKS control plane
- `rbac/cosmosdb` — Cosmos DB Data Owner (used 2x: CRM, Agents)
- `rbac/foundry` — Cognitive Services OpenAI User
- `rbac/keyvault` — Secrets Officer + Secrets User + Certificates Officer (3-tier)
- `rbac/search` — Search Index Data Reader
- `rbac/storage` — Storage Blob Data Reader
- `search` — Azure AI Search service (Standard tier, semantic ranker, system-assigned identity)
- `storage` — Storage Account + containers (shared key disabled, OAuth only)
- `storage-uploads` — Data-plane blob uploads (product images + SharePoint PDFs)
- `tls-cert` — Self-signed TLS cert in Key Vault (RSA 2048, 12mo, auto-renew)
- `vnet` — VNet with 4 subnets (AKS system, AKS workload, AGC, private endpoints)
- `workload-identity` — Federated credentials (AKS OIDC → managed identities for 4 non-agent services)

**Identity Model — Dual-track:**
- 5 managed identities (non-agent services): bff, crm_api, crm_mcp, know_mcp, kubelet → federated via `workload-identity` module
- 3 Entra Agent ID identities (AI agents): crm_agent, prod_agent, orch_agent → federated via `agent-identity` module
- 7 Kubernetes service accounts created (1 per workload identity)
- All use OIDC token exchange (no secrets in pods)

**Deployment Pipeline — 7 phases (deploy.ps1/deploy.sh):**
- Pre-flight: Purge soft-deleted Cognitive Services + Key Vaults
- Phase 1: Open resource firewalls (deployer IP whitelisting)
- Phase 2: terraform init + Entra user state import
- Phase 3: terraform validate
- Phase 4: terraform plan
- Phase 5: terraform apply (with Azure Policy diagnostic on failure)
- Phase 6: Seed CRM data (temp pod in AKS with seed-data tool)
- Phase 7: Link Entra users → Cosmos DB customers (temp pod)
- Always: Close all firewalls (runs on failure too)

**Bootstrap (init.ps1/init.sh) — 5 phases:**
- Phase 1: Authenticate (Azure + optionally GitHub)
- Phase 2: Entra app registration + OIDC federated credential for GitHub Actions
- Phase 3: GitHub environment + secrets (AZURE_CLIENT_ID, TENANT_ID, SUBSCRIPTION_ID)
- Phase 4: Azure backend resources (RG, storage account with deny-by-default, tfstate container, RBAC)
- Phase 5: Generate backend.hcl + {env}.tfvars

**Provider Configuration:**
- Terraform >= 1.14.7
- AzureRM ~> 4.63.0, AzureAD ~> 3.4.0, AzAPI ~> 2.8.0, Kubernetes ~> 3.0.1, Kubectl ~> 1.19.0
- Backend: azurerm (Azure Blob with AAD auth)
- CAE tokens disabled (ARM_DISABLE_CAE, AZURE_DISABLE_CAE, HAMILTON_DISABLE_CAE)

**CI/CD — 10 GitHub Actions workflows:**
- 6 infrastructure: terraform-plan, terraform-apply, orchestrator (plan-approve-apply-seed-data), seed-data, purge-soft-deleted, cleanup-deployer-ip
- 4 squad management: heartbeat, issue-assign, triage, sync-squad-labels
- OIDC auth to Azure (no stored credentials)
- Manual approval gate between plan and apply
- Firewall cleanup runs after every stage

**Security Posture:**
- ✅ All identities are managed (no client secrets anywhere)
- ✅ OIDC workload identity for both AKS pods and GitHub Actions
- ✅ Key Vault RBAC authorization (no access policies)
- ✅ 6 private endpoints with 6 Private DNS zones (full network isolation)
- ✅ All PaaS firewalls default-deny with deployer IP whitelisting
- ✅ Storage shared key access disabled (Azure AD only)
- ✅ AI Services local auth disabled (RBAC only)
- ✅ Entra SPA uses PKCE (no client secrets)
- ✅ TLS termination via self-signed cert in Key Vault
- ⚠️ Cosmos DB primary keys stored in Key Vault as secrets (nonsensitive in TF)
- ⚠️ Search admin key used by knowledge-source module (API key in provisioner)
- ⚠️ No Dockerfiles exist yet — containerization not started
- ⚠️ No Helm charts exist yet — K8s deployment approach TBD
- ⚠️ AKS control plane gets Contributor on entire resource group (broad)
- ⚠️ Self-signed TLS cert not suitable for production

### 2026-03-19 — Cross-Team Finding: Full Codebase Analysis Complete

**Team Update (from all 5 agents):** Architecture is fully specced and infrastructure is provisioned, but **zero application code exists yet.** This is the intended state at end of Phase 1 (infrastructure/tooling complete). All 5 agents confirm: Dockerfiles and Helm charts are the next gate before AKS deployment. Infrastructure itself is production-grade (dual identity model, network isolation, RBAC). No fundamental re-design of Terraform modules needed. All decisions merged into `.squad/decisions.md` with full team consensus. All agents aligned on critical path: containerization, then application build in dependency order.

### 2025-07-26 — Final Infra Audit: In-Memory Pivot Impact

**Context:** Plan rewritten — in-memory repositories replace all Docker/emulators. Audited all infra docs, Terraform modules, scripts, and CI/CD against the new plan.

**Key findings:**

1. **infra/README.md is clean.** No stale references, no mention of local dev. Will need a small addition post-implementation to reference `infra/terraform/local-dev/` and `setup-local.ps1`.
2. **Other infra .md files clean.** templates/README.md and network-policies/README.md are production-path docs, untouched by local dev changes.
3. **docs/foundry-only-deployment.md is 70% stale.** The entire document was written for the emulator-based approach (Cosmos Emulator, Azurite, dual-mode ConnectionString auth). With the in-memory pivot, §3 (Emulators), §4 (Component dual-auth code), §5 (Templates with ConnectionString), and §6 (setup-local.ps1 with emulator startup/healthcheck) all need rewriting. §2 (Terraform foundry-only) is ~90% salvageable. §8 (LocalVectorSearchService) folded into repository pattern.
4. **Foundry module verified.** `local_auth_enabled = false` IS hardcoded on main.tf line 19. `primary_access_key` output does NOT exist. `local_auth_enabled` variable does NOT exist. All three additions needed per plan are confirmed zero-impact to existing deployment (default = false, root main.tf doesn't pass the variable).
5. **init.ps1, deploy.ps1, init.sh, deploy.sh: ZERO references** to local-dev, setup-local, or in-memory. Truly untouched.
6. **CI/CD workflows: ZERO impact.** deploy-crm-api.yml is production-path (Docker/Helm/AKS). Squad workflows are management only. No workflow triggers on `infra/terraform/local-dev/**`.
7. **Multi-dev collision gap persists.** Default `rg-dotnetagent-localdev` has no user suffix. Two devs in same subscription will fight over same RG. Recommend username-based suffix in setup-local.ps1.
8. **setup-local.ps1 design gaps under new plan:** Script in spec is entirely stale (emulator-centric). New version needs only: prereq checks (az, terraform, dotnet), az login verify, terraform init/apply, retrieve outputs, generate appsettings.Local.json (Foundry-only keys), print instructions. No emulator start, no npm, no seed data step (in-memory self-seeds).

**Terraform module status:** All 20 modules untouched. Foundry module gets 3 small additive changes. No other module needed for local-dev.

### 2025-07-25 — Infrastructure Cleanup: .gitignore + EventGrid Removal

**`.gitignore` hardened:** Added a `# Terraform` section with 11 patterns (`*.tfstate`, `*.tfstate.*`, `.terraform/`, `.terraform.lock.hcl`, `override.tf`, `override.tf.json`, `*_override.tf`, `*_override.tf.json`, `*.tfvars`, `*.tfvars.json`, `backend.hcl`). These prevent accidental commits of state files, local overrides, variable files with secrets, and backend configs. Existing patterns were preserved.

**EventGrid module deleted:** Removed `infra/terraform/modules/eventgrid/` (v1/ with main.tf, variables.tf, outputs.tf). The module was never instantiated in main.tf and had a security concern — it passed AI Search admin API keys as plaintext into a Logic App HTTP action body. Grep confirmed zero references outside the module directory itself. Module count drops from 21 to 20.

### 2026-03-20 — Agent Identity v2 (msgraph)

**Agent Identity v2 architecture:** Implemented a new `agent-identity/v2` module using the `microsoft/msgraph` provider to create Agent Identity Blueprints, Blueprint Principals, and AKS federated identity credentials via Graph beta. The blueprint service now owns runtime identity creation, with Terraform only provisioning the blueprint and principal objects. Updated main.tf to use v2 and fixed Cosmos DB RBAC for agent principals.

### 2025-07-25 — Agent Identity Module Consolidation (v2 → v1)

**Consolidated agent-identity module:** Deleted the old azuread-based v1 module and promoted the msgraph-based v2 code into v1 as the single canonical version. Removed unused `var.environment` and `var.resource_group_name` from the module variables. Updated `main.tf` source path from `v2` back to `v1` and removed the corresponding `environment` and `resource_group_name` arguments from the module block. Verified no duplicate data source blocks (`azuread_client_config` and `azurerm_client_config` each appear exactly once). `terraform fmt -check -recursive` passes clean.

### 2025-07-25 — Scripts & CI/CD Comprehensive Audit

**Audit scope:** init.ps1/sh, deploy.ps1/sh, 6 GitHub Actions workflows.

**Critical findings (2):**
1. Orchestrator approval gate hardcodes "production" in issue title regardless of selected environment (`terraform-plan-approve-apply-seed-data.yaml` line 57).
2. CI/CD has no Phase 0 equivalent — `TF_VAR_msgraph_*` not set in workflows, so Agent Identity Blueprints cannot be provisioned via GitHub Actions. The deploy scripts create a temp SP + secret but workflows don't replicate this flow.

**Key warnings (11):**
- deploy.ps1 Phase 7 sets `CRM_DATA_PATH="/dev/null"` — Unix path doesn't exist on Windows.
- deploy.ps1 uses `cmd /c` for terraform init/plan but not apply — inconsistent exit code handling.
- deploy.sh has Cosmos RBAC retry loop (12×5s); deploy.ps1 uses fixed 30s wait — parity gap.
- TFLint in CI/CD not run with `--recursive` — child modules under `modules/*/v1/` skipped.
- `terraform fmt` check has `continue-on-error: true` — format violations silently ignored.
- CAE disable flags (`ARM_DISABLE_CAE` etc.) missing from all GitHub Actions workflow env blocks.
- No plan-on-PR trigger — infrastructure changes can land without automated plan review.
- Orchestrator seed-data job depends only on cleanup-after-apply, not terraform-apply — would run after a failed apply.
- 4 `STORAGE_ACCOUNT_*` GitHub env variables set by init but consumed by nothing.
- Approval gate restricted to `github.repository_owner` — may be too narrow for teams.
- Banner text in deploy scripts says "via AKS pod" but code runs dotnet directly.

**Positive findings (9):**
Firewall bracket pattern (try/finally + trap EXIT), OIDC everywhere (zero stored creds), idempotent bootstrap, CAE handling in local scripts, Agent Identity SP lifecycle (1hr secret + cleanup), policy diagnostic on failure, state storage locked down, Terraform state locking via blob lease, cross-platform parity (PS1/SH produce identical infra).

**Overall grade: B+** — Strong security posture and well-designed automation. Two critical gaps need fixing before CI/CD can fully replace local deploy scripts.

### 2025-07-25 — Deep Terraform Audit (Read-Only)

**Scope:** Full audit of root configuration + all 20 modules (13 primary + 7 RBAC sub-modules).

**Overall Grade: B+ (Strong foundation, module defaults need hardening)**

**Key findings by severity:**

**Critical (3):**
1. AKS RBAC module assigns `Contributor` on entire resource group — overly broad for control plane identity.
2. knowledge-source module uses `local-exec` provisioner with API key — fragile cross-platform and embeds key in provisioner env.
3. Module defaults not secure-by-default: cosmosdb, keyvault, foundry all default `public_network_access_enabled = true` and firewall to `Allow` when no IPs given. Root main.tf compensates (passes deployer IP + creates private endpoints), but modules themselves are insecure if reused standalone.

**Warnings (12):**
- `tags` variable in root variables.tf has no description.
- kubernetes provider declared but never configured (only kubectl used).
- Key Vault `purge_protection_enabled` defaults to `false`, `soft_delete_retention_days` defaults to 7 (too low for prod).
- VNet module creates no NSGs or route tables — subnets have no network security rules.
- AKS `drain_timeout_in_minutes = 0` (no pod drain grace during upgrades).
- AKS kubelet identity passed as 3 separate variables instead of single object reference.
- Cosmos DB RBAC module description says "Data Owner" but actually assigns "Data Contributor" (UUID 00000000-0000-0000-0000-000000000002).
- Key Vault RBAC module missing `certificate_officer_assignment_ids` output.
- storage-uploads module creates deployer role assignment that persists after destroy.
- ACR module `public_network_access_enabled` defaults to `true`.
- No variable validation rules on SKUs, IP addresses, K8s version, or container names across most modules.
- Entra module always creates test users (no toggle to disable).

**Positives (10):**
- All 7 PaaS services have private endpoints with correct DNS zones (7 PEs, 6 zones — both Cosmos accounts share zone).
- Dual identity model (managed identities + Entra Agent IDs) is clean and least-privilege.
- Workload identity federation (zero secrets in pods) for all 7 K8s service accounts.
- Storage: shared key disabled, OAuth default, network deny-by-default.
- Foundry: local auth disabled (RBAC-only).
- Cosmos DB: local auth disabled.
- Key Vault: RBAC authorization (no access policies).
- Key Vault RBAC has excellent 3-tier separation (Officer/User/CertOfficer).
- K8s manifests correct (namespace + SA with workload identity annotations).
- Module versioning (v1/) consistently applied across all 20 modules.

**RBAC completeness verified:** All service identities have correct permissions — no missing assignments that would cause runtime failures. Deployer gets Secrets Officer + CRM Cosmos Data Contributor (for seeding). Only over-permissioning is AKS Contributor on RG.

### 2025-07-25 — T-01: Dockerfile + Helm Chart Base Templates

**Created `docs/templates/` with reusable patterns for all 8 services:**

**Dockerfile.template:** Multi-stage .NET 9 build (SDK → publish → aspnet runtime). Security: runs as `app` user (UID 1654), non-root enforced. Health check via `wget` (aspnet:9.0 Debian image). OCI image labels with build args for version/date/ref. Build context is repo root so `Directory.Build.props` and `global.json` are available. Layer caching optimized (csproj restore before full source copy). PublishReadyToRun enabled for fast cold start on AKS.

**Helm chart skeleton (`helm-base/`):** Complete chart with 7 templates:
- `deployment.yaml` — securityContext (runAsNonRoot, readOnlyRootFilesystem, drop ALL caps), writable /tmp emptyDir, liveness/readiness probes, workload identity pod label, ConfigMap checksum for auto-rollout.
- `service.yaml` — ClusterIP targeting port 8080 (.NET 9 non-root default).
- `serviceaccount.yaml` — Conditional creation (defaults to `create: false` since Terraform pre-provisions SAs).
- `hpa.yaml` — autoscaling/v2, CPU + memory targets, disabled by default.
- `configmap.yaml` — Non-secret config as env vars.
- `_helpers.tpl` — Standard name/label/image helpers, 63-char truncation, app.kubernetes.io labels.
- `NOTES.txt` — Post-install instructions with kubectl commands.

**Key design choices:**
- `serviceAccount.create: false` by default — references Terraform-managed SAs (sa-crm-api, sa-bff, etc.)
- `workloadIdentity.enabled: true` — adds `azure.workload.identity/use` pod label for OIDC token exchange
- Pod UID 1654 matches aspnet:9.0 `app` user — consistent between Dockerfile USER and K8s securityContext
- Resources: 100m/128Mi requests, 500m/512Mi limits — conservative defaults to tune per service
- Probes: /health (liveness) and /ready (readiness) as agreed in decisions.md

### 2025-07-25 — T-02: Kubernetes NetworkPolicy Manifests (F-01 remediation)

**Created `infra/k8s/network-policies/` with 10 files (9 YAML + README):**

**Default deny (`default-deny.yaml`):** Namespace-wide policy matching all pods with empty ingress/egress — blocks everything unless a per-service policy explicitly allows it.

**Per-service policies (8 files):** Each targets a single service via `app: {service-name}` pod selector and declares both Ingress and Egress policy types:
- `bff-api` / `blazor-ui` — ingress from AGC namespace (`azure-alb-system`) via `namespaceSelector`
- `orchestrator-agent` — ingress from bff-api, egress to crm-agent + product-agent
- `crm-agent` / `product-agent` — ingress from orchestrator-agent, egress to respective MCP servers
- `crm-mcp` — ingress from crm-agent, egress to crm-api
- `knowledge-mcp` — ingress from product-agent, egress to PE subnet only
- `crm-api` — ingress from crm-mcp, egress to PE subnet only

**Egress patterns:**
- DNS: Every policy allows UDP+TCP/53 to `k8s-app: kube-dns` in `kube-system` (required for any hostname resolution)
- Internal: Pod-to-pod on port 8080 via pod selector within namespace
- External: Azure PaaS via private endpoint subnet `10.0.3.0/24` on port 443 (Cosmos DB, AI Search, OpenAI, Key Vault)

**Key design choices:**
- Used `kubernetes.io/metadata.name` label for cross-namespace selectors (built-in, no manual labeling needed)
- PE subnet CIDR (`10.0.3.0/24`) matches VNet module defaults — documented in README that this must update if CIDRs are customized
- `blazor-ui` has the tightest policy: only DNS egress (serves static WASM, all API calls happen client-side in the browser)
- Pod selectors use `app.kubernetes.io/name: {service-name}` convention — matches Helm chart `service.selectorLabels` helper automatically

### 2025-07-25 — Fix: NetworkPolicy Label Mismatch (Cleveland Security Review)

**Issue:** Cleveland's security review (Finding 1, HIGH severity) identified that all 8 per-service NetworkPolicy files used `app: {service-name}` as pod selectors, but the Helm chart base templates produce pods with `app.kubernetes.io/name: {service-name}`. Result: when deployed via Helm, allow rules would never match actual pod labels — full service outage under default-deny.

**Fix (Option A):** Updated all `matchLabels` and `podSelector` entries across 8 YAML files to use `app.kubernetes.io/name: {service-name}`. This is the Kubernetes standard label convention and what Helm charts naturally produce. Updated README to document the convention and remove the old requirement to add custom `app` labels.

**Files changed:** 8 service policy YAMLs + README.md (9 files total). Default-deny unchanged (uses `podSelector: {}`, not label-specific).

**Verification:** Grep confirmed zero remaining `app: ` selectors across all policy files. 20 occurrences of `app.kubernetes.io/name` across the 8 files (correct count for all podSelector + matchLabels references).

### 2026-03-23T15:42 — Critical Stage: T-01, T-02, Security Review, Label Fix

**Stage completion:** Completed all infrastructure templates and resolved all security findings.

**T-01 (Dockerfile + Helm) → Commit c91915d:** Base templates created and committed. Multi-stage Dockerfile follows security best practices (non-root, PublishReadyToRun). Helm base chart pattern (helm-base/) ready for all 8 services to customize.

**T-02 (NetworkPolicy manifests) → Commit ad4b9a1:** 9 manifests (default-deny + 8 per-service) created with pod label selectors `app: {service-name}`. Architecture DAG flow documented and enforced.

**Cleveland Security Review:** APPROVED with notes. Critical finding: label mismatch between NetworkPolicy selectors (`app: X`) and Helm chart outputs (`app.kubernetes.io/name: X`). Would cause full service outage under default-deny on AKS. Also flagged medium-severity gap: no monitoring ingress rules (deferred, known issue).

**Label fix (follow-up) → Commit ff8d5ad:** Updated all 8 NetworkPolicy files to use `app.kubernetes.io/name: {service-name}` selectors, aligning with Kubernetes standard and Helm conventions. README updated. Finding 1 resolved. Deployment gate cleared.

### 2025-07-25 — High Stage: T-05, T-06, T-07, T-08 (Scripts & CI/CD Fixes)

**T-05 (Pin kubernetes_version) → Commit a061410:** Added `default = "1.30"` to `aks_kubernetes_version` in variables.tf. The variable previously had no default — while dev.tfvars (gitignored, generated by init) already pins to "1.34", the missing default meant a bare `terraform plan` without tfvars could let AKS pick latest GA. Safety net applied.

**T-06 (Fix approval gate hardcode) → Commit c3a5839:** Replaced hardcoded "production" in `terraform-plan-approve-apply-seed-data.yaml` line 56 with `${{ inputs.environment }}`. Also updated issue-body to include the environment name. Operators deploying to dev/staging now see the correct environment in the approval issue.

**T-07 (Fix seed-data after failed apply) → Commit ee72793:** Added `terraform-apply` to seed-data job's `needs` list (was only `[cleanup-after-apply]`). Since cleanup-after-apply runs with `if: always()`, it always succeeds — meaning seed-data would proceed even after a failed terraform-apply. Now seed-data correctly requires both terraform-apply success AND cleanup completion.

**T-08 (CAE flags in CI/CD) → Commit 8a50aa4:** Added `ARM_DISABLE_CAE`, `AZURE_DISABLE_CAE`, `HAMILTON_DISABLE_CAE` to all Terraform Init/Plan/Apply steps in both terraform-plan.yaml and terraform-apply.yaml. These match the deploy.ps1/sh pattern and prevent Continuous Access Evaluation from revoking tokens mid-operation in corporate Entra tenants with aggressive CAE policies.

### 2025-07-25 — Medium Stage: T-09, T-10, T-12 (Observability, Security Hardening, Docs)

**T-09 (Diagnostic settings) → Commit e3e5363:** Created `infra/terraform/diagnostics.tf` with `azurerm_monitor_diagnostic_setting` for Key Vault (AuditEvent), Cosmos DB CRM (DataPlaneRequests + ControlPlaneRequests), and Cosmos DB Agents (same categories). All send to the Log Analytics workspace created by the AKS module (`module.aks.log_analytics_workspace_id`).

**T-10 (Harden module defaults) → Commit 60b5799:** Flipped `public_network_access_enabled` default to `false` in cosmosdb, keyvault, acr, and foundry modules. Flipped `purge_protection_enabled` to `true` in keyvault. Added `public_network_access_enabled` variable to foundry module (previously absent). Added NSG resources to VNet module for private endpoint subnet (DenyAllInbound) and AGC subnet (AllowHTTPS + DenyAll). Updated root main.tf to pass explicit `public_network_access_enabled = true` and `purge_protection_enabled = true` where needed — ensures current deployment behavior is preserved while module reuse gets secure defaults.

**T-12 (CI/CD Agent Identity gap) → Commit 44675b5:** Added "Known Gaps" section to `docs/security.md` documenting that CI/CD cannot provision Entra Agent Identity Blueprints because the msgraph provider requires a client secret and GitHub Actions uses OIDC-only. Documented three options; accepted local-only provisioning for now.

### 2025-07-25 — Fix: kubectl provider DNS failure (stale AKS FQDN)

**Root cause:** The `gavinbunney/kubectl` provider creates a Kubernetes REST client lazily per resource read. When `kubectl_manifest` resources exist in Terraform state but the AKS cluster has been destroyed, the provider attempts DNS resolution of the stale FQDN during `terraform plan` refresh — causing `no such host` errors that block the entire plan.

**Fix (two-part, commit 9461d64):**
1. `infra/terraform/providers.tf` — Wrapped provider config with `try()` guards. Returns empty strings when AKS module outputs are null/unknown (fresh deploy). Doesn't help with stale-in-state values, but provides defense in depth.
2. `infra/deploy.ps1` + `infra/deploy.sh` — Added "Pre-plan: kubectl state guard" between validate and plan phases. Detects `kubectl_manifest.*` resources in state, checks if the AKS cluster exists in Azure via `az aks show`, and removes stale state entries if the cluster is gone. Constructs AKS name from `base_name`, `environment`, `location` (matching AKS module naming: `aks-{base_name}-{environment}-{location}`).

**Key patterns:**
- Provider blocks can only reference variables, locals, and module outputs (from state). They CANNOT reference data sources or resources — this is why the fix requires deploy-script-level state cleanup.
- `gavinbunney/kubectl` creates REST clients per-resource, not during provider init. This means `-target=module.aks` would also work (kubectl resources aren't refreshed), but state cleanup is more explicit.
- The `try()` in provider config handles: fresh deploy (unknown outputs), module errors, and null kube_config values. It does NOT handle stale-but-valid state values.
- `terraform state rm` on `kubectl_manifest` is safe when AKS is destroyed — the K8s namespace and service accounts no longer exist either.

**Files changed:** `infra/terraform/providers.tf`, `infra/deploy.ps1`, `infra/deploy.sh`

### 2025-07-25 — Fix: kubectl provider DNS failure v2 (structural fix with variable gate)

**Problem:** The prior fix (try() guards + state removal) was INSUFFICIENT. The `gavinbunney/kubectl` provider initializes eagerly — it creates a REST client and connects to the K8s API server during provider initialization, which happens BEFORE resource refresh. `try()` only catches null/error, not "valid string pointing to a dead host." State removal alone doesn't prevent provider initialization because ALL declared providers initialize during plan regardless of resources.

**Fix (structural, commit 0b58039):**
1. `infra/terraform/variables.tf` — Added `var.deploy_k8s_resources` (bool, default=true) to gate the kubectl provider and all K8s resources.
2. `infra/terraform/providers.tf` — Provider config now uses ternary: `var.deploy_k8s_resources ? try(module.aks...) : ""`. When false, provider gets empty credentials (no connection attempt).
3. `infra/terraform/main.tf` — `kubectl_manifest.namespace` gets `count = var.deploy_k8s_resources ? 1 : 0`. `kubectl_manifest.service_accounts` `for_each` wrapped with `var.deploy_k8s_resources ? {...} : {}`.
4. `infra/deploy.ps1` + `infra/deploy.sh` — Enhanced pre-plan guard with 3-step reachability check: (a) `az aks show` cluster exists, (b) DNS resolution of FQDN (PowerShell: `[System.Net.Dns]::GetHostEntry()`, bash: `host`/`nslookup`), (c) if unreachable: remove stale state + set `deploy_k8s_resources=false`. Added two-pass deployment: pass 1 creates infra with K8s deferred, pass 2 creates K8s resources after AKS is up.

**Key learnings:**
- The gavinbunney/kubectl provider DOES initialize eagerly (contradicting prior learning about lazy per-resource init). The `host` config is evaluated and a connection attempt is made during provider init, not just during resource refresh.
- Variable-gated provider config is the most reliable pattern for conditional providers in Terraform. When the variable is false, the provider gets empty/dummy config and never attempts connection.
- Two-pass deployment is necessary because Terraform can't create a resource (AKS) and then use its outputs in a provider config in the same plan — the provider config is evaluated at plan time, before any resources are created/updated.
- `count` and `for_each` conditions on resources are belt-and-suspenders with the provider gate — even if the provider somehow initializes, the resources won't be planned.
- Backward compatible: `default=true` means existing CI/CD and manual deploys that don't pass the variable work exactly as before.

**Files changed:** `infra/terraform/variables.tf`, `infra/terraform/providers.tf`, `infra/terraform/main.tf`, `infra/deploy.ps1`, `infra/deploy.sh`

### 2025-07-25 — Fix: Embedding deployment SKU mismatch in centralus

**Problem:** `terraform apply` failed with 400 Bad Request — `text-embedding-3-small` model does not support SKU `"Standard"` in `centralus` region. The chat model deployment already used `"GlobalStandard"` and worked fine.

**Fix:** Changed `embedding_sku_name` from `"Standard"` to `"GlobalStandard"` in `infra/terraform/dev.tfvars` (line 18).

**Verification:** The foundry module (`infra/terraform/modules/foundry/v1/main.tf`, line 71) correctly passes `var.embedding_sku_name` to the embedding deployment's `sku.name` block — no module wiring issue.

**Key learning:**
- In `centralus`, Azure OpenAI embedding models (e.g., `text-embedding-3-small`) require `GlobalStandard` SKU, not `Standard`. This is a region-specific constraint.
- When adding new model deployments, always verify SKU availability for the target region via `az cognitiveservices account list-skus` or the Azure docs model availability matrix.

**Files changed:** `infra/terraform/dev.tfvars`


### 2025-07-25 — Fix: Embedding SKU bug in init scripts + Remove unnecessary KV diagnostic import

**Issue 1 — Embedding SKU in init scripts:**
Both `infra/init.ps1` (line 767) and `infra/init.sh` (line 783) had `embedding_sku_name = "Standard"` hardcoded in the tfvars template. This was the root cause of the `text-embedding-3-small` SKU error — the prior fix to `dev.tfvars` would be overwritten on next `init.ps1` run. Changed both to `GlobalStandard`.

**Issue 2 — Unnecessary KV diagnostic import logic:**
The Key Vault diagnostic setting is declared in `diagnostics.tf` and fully managed by Terraform. Removed detect-and-import logic from `deploy.ps1`, `imports.tf`, and `variables.tf`. `deploy.sh` did not have this logic.

**Key learnings:**
- Init scripts that generate tfvars files are the source of truth for default values. Fixing only the generated tfvars is insufficient.
- The detect-and-import pattern is only for resources that may exist outside Terraform. Terraform-managed resources need no import logic.

**Files changed:** `infra/init.ps1`, `infra/init.sh`, `infra/deploy.ps1`, `infra/terraform/imports.tf`, `infra/terraform/variables.tf`

### Doc Accuracy Audit — Infra & Lab Docs

Audited `docs/lab-0.md`, `docs/lab-1.md`, `infra/README.md`, `docs/security.md`, `docs/config-naming-standard.md` against actual codebase.

**Findings:**
- lab-0.md: 100% accurate (5 phases, 3 secrets, 3 generated files all match).
- lab-1.md: Two discrepancies found:
  - config-sync says "Fetched 20/20 secrets" but actual unique KV secrets in Program.cs is 21 (Bff--BaseUrl was likely added later).
  - GitHub Actions section says "four stages" but lists 5 bullet points (Plan, Manual approval, Purge, Apply, Seed Data). Actual workflow has 8 jobs (5 stages + 3 cleanup).
- lab-1.md: Deploy phases (8, 0-7), identity counts (5+3), data files (15 png, 12 pdf), 6 CRM containers, gpt-4.1 deployment name, firewall commands — all accurate.
- infra/README.md: Directory tree and all module descriptions verified accurate.
- docs/security.md: Key terms, three auth flows, two-flow example — all accurate.
- docs/config-naming-standard.md: KV "--" separator and config-sync nested JSON conversion — both verified in code.

### Fix: Blazor UI Network Policy Port Mismatch (F-06)

**Problem:** `infra/k8s/manifests/network-policies/blazor-ui.yaml` allowed ingress on port 8080, but blazor-ui runs nginx which listens on port 80. This would cause AGC health probes and traffic to be blocked by the network policy.

**Fix:** Changed ingress port from 8080 to 80 and added `# nginx HTTP` comment for clarity.

**Scope check:** No other network policy references blazor-ui as an egress target (blazor-ui only receives ingress from AGC). The other services correctly use port 8080 (Kestrel default for .NET containers). The README doesn't mention per-service ports, so no README update needed.

**Files changed:** `infra/k8s/manifests/network-policies/blazor-ui.yaml`

### Key Vault Secret Name Audit — PascalCase--Hierarchy Compliance

**Audit scope:** Every file in the repo that references Key Vault secret names — deploy scripts, Terraform, CI/CD workflows, config-sync.

**Violations found (17 total, SCREAMING-KEBAB-CASE):**
- `infra/deploy.ps1` lines 1003–1007: 5× `CUSTOMER-*-ENTRA-OID` → `Customer--*EntraOid`
- `infra/deploy.sh` lines 852–856: 5× `CUSTOMER-*-ENTRA-OID` → `Customer--*EntraOid`
- `.github/workflows/seed-data.yaml` line 84: `COSMOSDB-CRM-ENDPOINT` → `CosmosDb--CrmEndpoint`
- `.github/workflows/seed-data.yaml` line 85: `COSMOSDB-CRM-DATABASE` → `CosmosDb--CrmDatabase`
- `.github/workflows/seed-data.yaml` lines 111–115: 5× `CUSTOMER-*-ENTRA-OID` → `Customer--*EntraOid`

**Confirmed correct:** `infra/terraform/main.tf` (40+ secrets), `src/config-sync/Program.cs` (22 entries), `deploy.ps1` CosmosDb reads (lines 951–952), `deploy.sh` CosmosDb reads (lines 796–797).

**Files changed:** `infra/deploy.ps1`, `infra/deploy.sh`, `.github/workflows/seed-data.yaml`

### Fix: Deployer IP Firewall Cleanup — Three Root Causes

**Problem:** After deploy, the deployer IP persisted in AI Search, Cosmos DB (both CRM and Agents accounts), and Foundry (AI Services) firewalls despite `Remove-DeployerFirewallRules` running in the finally block.

**Root causes found:**

1. **Cosmos DB `--no-wait`:** Both Add and Remove functions used `az cosmosdb update --no-wait`, making the IP removal fire-and-forget. The script exited before Azure finished processing the update, so the IP was never actually removed.

2. **CIDR format mismatch (AI Search + Cosmos DB):** The Remove function filtered IPs by exact string match (`$_.Trim() -ne $DeployerIp`), comparing plain IP `1.2.3.4`. But Azure may store the IP as `1.2.3.4/32` (CIDR notation) depending on how it was added (Terraform uses plain IPs in `allowed_ips`, but Azure normalizes differently per service). The filter missed CIDR-formatted entries.

3. **Foundry CIDR mismatch:** The Remove function only tried `az cognitiveservices account network-rule remove --ip-address "$IP/32"`. Terraform's foundry module sets `ip_rules = var.allowed_ips` with plain IPs (no CIDR). Azure stored the rule without CIDR, so the `/32` removal didn't match anything and failed silently (errors suppressed by `2>$null`).

**Fixes applied (both deploy.ps1 and deploy.sh):**
- Removed `--no-wait` from Cosmos DB updates in both Add and Remove functions
- Made Cosmos DB and AI Search IP filters CIDR-aware: filter both `$DeployerIp` and `$DeployerIp/32`
- Added second Foundry removal attempt with plain IP format (try `/32` then plain)
- Made Add function CIDR-aware too (prevents duplicates if Azure stored previous IP with CIDR)

**Files changed:** `infra/deploy.ps1`, `infra/deploy.sh`

### 2025-07-26 — Foundry Hosted Agent Private Networking Research

**Question:** Can we deploy a hosted agent (containerized .NET code) to Foundry Agent Service with BYO VNet injection for private networking?

**Verdict: NO — Hosted agents do NOT support VNet injection or private networking today.**

**Evidence from Microsoft docs (multiple sources, all consistent):**
1. Foundry network isolation limitations table: "Hosted Agents | Not supported | Hosted Agents do not have virtual network support yet." (configure-private-link#limitations-and-considerations)
2. Foundry Agent Service overview: "Private networking is available for prompt agents and workflow agents. Hosted agents don't currently support private networking during preview." (agents/overview#enterprise-capabilities)
3. Hosted agents concept page: "You can't create hosted agents by using the standard setup for network isolation within network-isolated Foundry resources." (agents/concepts/hosted-agents#limits-pricing-and-availability-preview)
4. Capability host for hosted agents requires `enablePublicHostingEnvironment: true` — the API literally requires public hosting.

**What the `Microsoft.App/environments` subnet delegation is actually for:**
- It's for the Foundry platform's internal "Agent client" runtime (prompt agents / standard agents), NOT for hosted agent containers.
- The "container injection" language in docs refers to the platform injecting its agent runtime into your VNet subnet, not your hosted agent containers.
- Hosted agents run on Microsoft-managed Container Apps infrastructure that is NOT injected into your VNet.

**What DOES work with VNet today (prompt agents):**
- Prompt agents with Standard Setup + Private Networking support full VNet injection
- MCP tools work through VNet subnet (confirmed in tool support table: "MCP Tool (Private MCP) | Supported | Through your VNet subnet")
- Azure AI Search, Code Interpreter, Function Calling all work behind VNet
- Template 15 (fully private) and Template 19 (hybrid private resources) both support this

**Alternatives for Product Agent deployment:**
1. Prompt Agent + MCP tools on VNet (Template 19 pattern)
2. Self-host in AKS (full network control, no Foundry hosting benefits)
3. Wait for GA (no timeline given, billing deferred to April 2026)
4. Hybrid: Prompt agent + private MCP servers in Container Apps on VNet
### 2025-07-26 — Audit: Script Boundary Report (init / deploy / setup-local)

**Requested by:** Almir Banjanovic
**Context:** Almir clarified that init scripts, deploy scripts, and setup-local.ps1 are three coexisting scripts with distinct purposes. This audit documents what each does and identifies guard rails for setup-local.ps1.

---

#### 1. What does init.ps1/sh do?

Creates resources for Terraform remote state backend and (optionally) GitHub CI/CD integration. Runs once per environment.

**Resources created (Phase 4):**
- **Resource Group:** `rg-{baseName}-{env}-{location}` (e.g., `rg-dotnetagent-dev-centralus`) — `init.ps1:564`, `init.sh:602`
- **Storage Account:** `st{baseName}{env}{location}` truncated to 24 chars, `--default-action Deny` — `init.ps1:578-582`, `init.sh:615-619`
- **Blob Container:** `tfstate` in that storage account — `init.ps1:610-633`, `init.sh:634-643`
- **RBAC:** Storage Blob Data Contributor on storage account for deployer OID — `init.ps1:700-715`, `init.sh:708-724`
- **RBAC:** Key Vault Secrets Officer + Certificates Officer on RG for deployer — `init.ps1:717-730`, `init.sh:726-738`

**Entra + GitHub (Phases 2-3, skipped in "local-only" mode):**
- **Entra app registration:** `github-actions-{repoName}` — `init.ps1:424-443`
- **Service principal + OIDC federated credential** — `init.ps1:445-470`
- **3 GitHub repo secrets:** `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` — `init.ps1:489-495`
- **~35 GitHub environment variables** (resource group, storage, AKS config, model params, etc.) — `init.ps1:499-544`
- **Contributor RBAC** on RG for the app registration — `init.ps1:636-658`

### A3 — Terraform Local-Dev Root Module

Created `infra/terraform/local-dev/` — a standalone root module that deploys only Foundry + model deployments using a local backend. Purpose: let developers `terraform apply` from their machine to get a personal Azure OpenAI instance with API key auth, no AKS/CosmosDB/AKV/Search overhead.

**Files created:**
- `providers.tf` — azurerm ~> 4.63, local backend, cognitive_account purge + RG force-delete features
- `variables.tf` — 8 variables with sensible defaults (location=centralus, base_name=dotnetagent, environment=localdev, gpt-4.1 @ 2025-04-14, text-embedding-3-small @ v1); resource_group_name defaults null for TF_VAR override
- `main.tf` — creates RG + invokes `../modules/foundry/v1` with local_auth_enabled=true, public_network_access_enabled=true, empty allowed_ips, embedding_capacity=120
- `outputs.tf` — exposes foundry_endpoint, foundry_api_key (sensitive), chat_deployment_name, embedding_deployment_name

**Validation:** `terraform init -backend=false` + `terraform validate` both passed. All required foundry module variables supplied. Model versions match dev.tfvars (gpt-4.1=2025-04-14, embedding=1).

**Config files generated (Phase 5):**
- `infra/terraform/backend.hcl` — remote state config — `init.ps1:740-746`
- `infra/terraform/{env}.tfvars` — all Terraform variables — `init.ps1:749-793`
- `infra/deployments/{env}-{location}.env` — consumed by deploy scripts — `init.ps1:797-808`

**NO Key Vault is created by init.** Key Vault is created by Terraform during deploy. Init only grants KV RBAC roles pre-emptively so Terraform can write secrets when it creates the KV.

---

#### 2. What does deploy.ps1/sh do?

Runs Terraform apply against the full 20+ module infrastructure. **Assumes init has already run.**

**Hard dependencies on init:**
- Requires `infra/deployments/*.env` files (created by init Phase 5) — `deploy.ps1:361-369`, `deploy.sh:197-205`
- Requires `infra/terraform/backend.hcl` (created by init Phase 5) — `deploy.ps1:603-605`, `deploy.sh:452-454`
- Reads `storage_account_name` from backend.hcl to open firewalls — `deploy.ps1:607-610`, `deploy.sh:456-459`

**Terraform init pattern:** `terraform init -upgrade -reconfigure -backend-config=backend.hcl` — `deploy.ps1:691`, `deploy.sh:640`

**Additional deploy-only work:**
- Phase 0: Agent Identity SP creation (msgraph provider credentials) — `deploy.ps1:465-550`
- Phase 1: Opens firewalls on KV, Storage, Cosmos, Foundry, AI Search — `deploy.ps1:640-652`
- Phase 2: `terraform init -backend-config=backend.hcl` — `deploy.ps1:691`
- Phases 3-5: validate, plan, apply
- Phase 6: Seed CRM data via `dotnet run` of seed-data tool
- Phase 7: Link Entra users to Customer documents in Cosmos
- Cleanup: Always removes deployer IP from all firewalls — `deploy.ps1:298-355`

---

#### 3. Does setup-local.ps1 need any of init's outputs?

**NO. Confirmed.** Almir's claim is correct.

Evidence: The spec's `providers.tf` at `docs/foundry-only-deployment.md:293-294` declares `backend "local" {}`. The setup-local script runs `terraform init -input=false` in `infra/terraform/local-dev/` (spec line 1284) with no `-backend-config`. It never reads backend.hcl, never reads `infra/deployments/*.env`, and never touches the remote storage account.

The local-dev root module creates its own resource group (`rg-dotnetagent-localdev`) — a completely different RG from the one init creates.

---

#### 4. Does init create GitHub environment secrets?

**YES.** Phase 3 creates:
- 3 **repository secrets**: AZURE_CLIENT_ID, AZURE_TENANT_ID, AZURE_SUBSCRIPTION_ID — `init.ps1:489-495`
- ~35 **environment variables** under the GitHub environment (e.g., `dev`) — `init.ps1:499-544`
- 1 **GitHub environment** created via `gh api --method PUT` — `init.ps1:486`

Does setup-local need anything analogous? **No.** Local dev has no CI/CD pipeline, no GitHub Actions, no OIDC federation. Everything runs on the developer's machine.

---

#### 5. Does setup-local need to touch infra/deployments/*.env?

**No.** The `infra/deployments/*.env` files are consumed exclusively by deploy.ps1/sh to load `ENVIRONMENT`, `LOCATION`, `BASE_NAME`, `RESOURCE_GROUP`. Setup-local has its own hardcoded/prompted values and writes `appsettings.Local.json` directly to each `src/*/` directory. It does not write to or read from `infra/deployments/`.

---

#### 6. Bash + PowerShell parity — is the missing setup-local.sh a gap?

The plan only specifies `setup-local.ps1`. This is **intentional** because:
- Cosmos DB Emulator is Windows-only (no Linux/macOS native emulator)
- The script checks for `$env:ProgramFiles\Azure Cosmos DB Emulator\` (spec line 1254)
- Azurite is cross-platform but Cosmos Emulator is the blocker

**However:** Docker-based Cosmos Emulator exists for Linux. A future `setup-local.sh` that uses the Docker emulator image is possible but not in scope for v1.

---

#### 7. Risk: could deploy.ps1 clobber local state or vice versa?

**Low risk, but not zero.** The two Terraform root modules use different directories:
- deploy.ps1 operates in `infra/terraform/` with `backend "azurerm"` → remote state in Azure Storage
- setup-local.ps1 operates in `infra/terraform/local-dev/` with `backend "local"` → local `.terraform.tfstate` file

**They cannot share state** because they are different root module directories with different backends. Running `deploy.ps1` after `setup-local.ps1` will not affect local-dev state and vice versa.

**Remaining risk:** A developer confused about which script to run could:
1. Run setup-local expecting it to deploy full infra (it won't — only 4 resources)
2. Run deploy.ps1 intending local dev (it will deploy 14+ Azure services and cost $50-100/day)

**No code-level guard rails exist today.** Both scripts run without checking for the other's artifacts.

---

#### 8. Does deploy.ps1 have backend-config patterns to follow?

**Yes.** Deploy.ps1 uses:
```
terraform init -upgrade -reconfigure -backend-config=backend.hcl
```
(deploy.ps1:691, deploy.sh:640)

Setup-local.ps1 should **NOT** follow this pattern. It uses:
```
terraform init -input=false
```
(spec line 1284)

This is correct because `providers.tf` in `local-dev/` declares `backend "local" {}` — no backend config file is needed.

---

#### Recommended Guard Rails for setup-local.ps1

1. **Check for remote .terraform directory:** Before running `terraform init` in `local-dev/`, check if `infra/terraform/.terraform/` exists with a remote backend. If it does, that's fine — it's a different directory. But if someone accidentally copied `.terraform/` into `local-dev/`, warn and abort.

2. **Verify working directory:** setup-local.ps1 should assert it's operating in `infra/terraform/local-dev/`, not `infra/terraform/`. Add:
   ```powershell
   if (Test-Path "$TerraformDir\backend.hcl") {
       Write-Error "SAFETY: Found backend.hcl in $TerraformDir. This looks like the full deployment directory. setup-local.ps1 should use infra/terraform/local-dev/."
       exit 1
   }
   ```

3. **Check for existing remote state in local-dev:** If `infra/terraform/local-dev/.terraform/terraform.tfstate` exists and contains `"backend": {"type": "azurerm"}`, refuse to run — someone manually ran `terraform init` with a remote backend in the local-dev dir.

4. **Environment label in Foundry resource:** The local-dev Terraform module uses `rg-dotnetagent-localdev` as its resource group name. This naming makes it visually distinct from `rg-dotnetagent-dev-{region}`. Good.

5. **Warn about cost if deploy.ps1 is detected:** Not practical to add to setup-local, but deploy.ps1 could add a check: if `infra/terraform/local-dev/terraform.tfstate` exists and shows resources, warn: "You have a local dev environment running. Running full deploy will create separate Azure resources at ~$50-100/day."

6. **No shared .env files:** setup-local should never write to `infra/deployments/` — this directory is exclusively for init→deploy pipeline.

**Files examined:** init.ps1, init.sh, deploy.ps1, deploy.sh, infra/deployments/dev-centralus.env, infra/README.md, docs/foundry-only-deployment.md (providers.tf spec at line 275, setup-local spec at line 1183)

### 2025-07-26 — Second-Pass Script & setup-local Audit (Opus 4.6 1M)

**Requested by:** Almir Banjanovic
**Scope:** Re-audit of init/deploy scripts + setup-local.ps1 design review from foundry-only-deployment.md spec + CI/CD workflow inventory + cross-script collision analysis.

**Confirmed from prior audit (4 items):**
1. Three scripts (init, deploy, setup-local) cleanly separated — different TF working directories, different backends, different resource targets
2. init creates remote state backend (Azure blob) + GitHub secrets (3 repo secrets + 35 env variables)
3. setup-local has zero dependencies on init outputs — no backend.hcl, no deployments/*.env, no GitHub CLI
4. CI/CD workflows have zero triggers on `infra/terraform/local-dev/**` paths

**Disputed / corrected (1 item):**
- Prior audit's guard rail "refuse if .terraform points to remote backend" — scope narrowed: only check `infra/terraform/local-dev/.terraform/terraform.tfstate`, not the parent directory's .terraform (which is *expected* to have remote backend)

**NEW findings (3 items):**
1. **Multi-dev collision:** Two developers running setup-local.ps1 with defaults in same subscription collide on `rg-dotnetagent-localdev`. Fix: inject username into default RG name (`rg-dotnetagent-localdev-{username}`)
2. **No emulator teardown:** No way to stop Cosmos Emulator + Azurite cleanly. Recommend `--Cleanup` flag on setup-local.ps1 that runs `terraform destroy` + stops emulator processes
3. **init.sh retry parity gap:** init.ps1 retries tfstate container creation 6×15s (lines 612-633); init.sh tries once (lines 635-643). Could silently fail on slow firewalls

**Recommended guard rails (3 concrete snippets):**
1. Remote backend detection: check `local-dev/.terraform/terraform.tfstate` backend.type != "local" → hard stop
2. Multi-dev avoidance: auto-detect `$env:USERNAME` and embed in default RG name via TF_VAR
3. Cleanup convenience: `--Cleanup` switch that runs terraform destroy + kills emulator processes

**Full audit written to:** `.squad/decisions/inbox/joe-script-reaudit-opus46-1m.md` (10 numbered findings with line:file evidence)

**Files examined:** init.ps1 (854 lines), init.sh (808 lines), deploy.ps1 (1097 lines), deploy.sh (full), infra/deployments/dev-centralus.env, infra/README.md, infra/terraform/providers.tf, docs/foundry-only-deployment.md (2001 lines, focused on sections 2, 6, 9, 10), plan.md (session state), all 11 .github/workflows/ files (triggers verified via grep)

### 2026-04-20 — Cross-Platform + Docker Emulator Audit (v3)

**Requested by:** Almir Banjanovic
**Directives:** (1) No Windows MSI — Cosmos DB Emulator must be Docker-based, all scripts cross-platform. (2) Self-contained config (appsettings.{env}.json only, no layering).

**Scope:** Re-evaluate setup-local script strategy, Docker-based Cosmos DB Emulator, docker-compose for emulators, init/deploy parity audit, multi-dev collision fix, teardown story, prerequisites list.

**Key Recommendations:**

1. **Hybrid script strategy:** docker-compose.yml for emulators (Cosmos + Azurite) + paired scripts (setup-local.ps1 + setup-local.sh) for Terraform/config/seeding. Matches existing init/deploy pattern. Rejected pwsh-only (adds dependency) and Makefile (foreign pattern).

2. **Cosmos DB Emulator image:** `mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-preview` — next-gen Linux emulator. Only 2 ports (8081 gateway + 1234 explorer), supports ARM64 (M1/M2 Macs), must use `--protocol https` for .NET SDK. Old image (10250-10255 ports) is deprecated.

3. **docker-compose.local.yml:** Two services (cosmosdb + azurite), health checks, persistent volume for Azurite, named containers. Replaces MSI install + npm global install + platform-specific process management. Single command: `docker compose up -d`.

4. **Prerequisites reduced:** Docker replaces both Node.js (Azurite) and Cosmos MSI. Final list: az CLI, Terraform, Docker (with Compose V2), .NET 9 SDK. Four tools, all cross-platform.

5. **Multi-dev collision:** Username suffix via `TF_VAR_resource_group_name` — `rg-dotnetagent-localdev-{username}`. Detected via `[Environment]::UserName` (PS) / `whoami` (bash).

6. **Teardown:** `-Cleanup` flag on setup-local scripts. Runs `docker compose down -v` + `terraform destroy` + removes appsettings.Local.json files. Not a separate script (no precedent in project).

**Script parity gaps found:**
- init.sh: single-attempt tfstate container creation (ps1 has 6×15s retry)
- init.sh: 5s firewall wait after IP add (ps1 has 30s)
- Both init scripts: GitHub env var `EMBEDDING_SKU_NAME="Standard"` but tfvars use `"GlobalStandard"` — CI/CD would get wrong SKU
- deploy.ps1: Phase 7 uses `/dev/null` (Unix path on Windows)
- deploy.ps1: inconsistent `cmd /c` usage for terraform commands

**Prior findings status:** All 5 prior findings still valid. "No setup-local.sh" upgraded from defensible to gap per Almir's directive. Two new parity gaps found (EMBEDDING_SKU_NAME env var, firewall wait times).

**Full audit written to:** `.squad/decisions/inbox/joe-audit-v3-crossplatform-docker.md`

**Files examined:** init.ps1, init.sh, deploy.ps1, deploy.sh, infra/README.md, docs/foundry-only-deployment.md (sections 3, 6, full setup-local.ps1 spec), plan.md, all .github/workflows/ files, Microsoft Cosmos DB emulator Linux docs (learn.microsoft.com), Azurite docs (learn.microsoft.com)

### A1 — Foundry Module: local_auth_enabled Variable

- Added `local_auth_enabled` variable (bool, default `false`) to foundry module `variables.tf`
- Changed `main.tf` line 19 from hardcoded `false` to `var.local_auth_enabled`
- Added `primary_access_key` sensitive output to `outputs.tf`
- Default `false` ensures zero impact on existing deployments — root `main.tf` doesn't pass this variable, so it inherits the safe default
- This enables local dev scenarios (API key auth) while keeping production locked to Entra/managed identity

### A5 — Config Templates + Setup Scripts

**Deliverables (13 files):**

- Created 9 `appsettings.Local.json.template` files (one per component: crm-api, simple-agent, crm-mcp, knowledge-mcp, crm-agent, product-agent, orchestrator-agent, bff-api, blazor-ui)
- Created `infra/setup-local.ps1` — PowerShell script: prereq checks (dotnet, az, terraform), `az account show` login check, username-based RG suffix, terraform init/apply, output retrieval, template → appsettings.Local.json generation with placeholder replacement, port map summary, `-Cleanup` switch for teardown
- Created `infra/setup-local.sh` — Bash equivalent with identical logic, LF line endings verified
- Updated `.gitignore` — added explicit `infra/terraform/local-dev/.terraform/` and `infra/terraform/local-dev/terraform.tfstate*` patterns (note: `appsettings.*.json` was already covered by existing glob)

**Template design:**
- Self-contained per environment (no base config layering) per decisions.md
- `DataMode=InMemory` on crm-api (the only component with InMemory support)
- Foundry placeholders (`{{FOUNDRY_ENDPOINT}}`, `{{FOUNDRY_API_KEY}}`, `{{CHAT_DEPLOYMENT_NAME}}`, `{{EMBEDDING_DEPLOYMENT_NAME}}`) on 5 components that use AI services
- Static templates (crm-api, crm-mcp, bff-api, blazor-ui) copied as-is without replacement
- Kestrel URL bindings match port map: 5001–5008
- Inter-service URLs hardcoded to localhost with correct ports (e.g., crm-mcp → crm-api on 5001)

**Script design:**
- Cross-platform: paired .ps1/.sh with identical logic and output
- No Docker, no emulators — uses Terraform local-dev root to provision only Azure AI Services
- Cleanup mode destroys Terraform resources and removes all generated appsettings.Local.json files
- Both scripts verified for syntax (PowerShell parser + bash -n)

