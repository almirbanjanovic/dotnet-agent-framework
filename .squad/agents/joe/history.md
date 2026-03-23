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
- `foundry` — AI Services account + GPT-4.1 chat + text-embedding-ada-002 deployments
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
