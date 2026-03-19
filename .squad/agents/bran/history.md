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
