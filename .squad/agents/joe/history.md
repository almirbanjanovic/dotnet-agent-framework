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