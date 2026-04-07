# Project Context

- **Owner:** Almir Banjanovic
- **Project:** .NET Agent Framework — 8-container agentic AI system with Contoso Outdoors (Blazor WASM UI, BFF API, CRM API, CRM MCP, Knowledge MCP, CRM Agent, Product Agent, Orchestrator Agent)
- **Stack:** .NET 9, Minimal APIs, Blazor WebAssembly, MudBlazor, ModelContextProtocol C# SDK, Microsoft.Agents.AI, Azure.AI.OpenAI, Cosmos DB, Azure AI Search, Terraform, AKS, Helm, Docker
- **Created:** 2026-03-19

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

### 2025-07-24 — Full Architecture Analysis

**State of the codebase:** Phase 1 (infra + tooling) is complete. None of the 8 core containers have been implemented. The solution file contains only 3 dev-tool projects: `simple-agent`, `config-sync`, `seed-data`.

**Architecture decisions confirmed:**
- 8 independent containers, no shared project references, HTTP/JSON only between services
- Three-tier identity: user (Entra ID/MSAL), service (managed identity/workload identity), agent (Entra Agent ID Blueprints)
- BFF owns conversation persistence; agents are stateless
- MCP servers are thin protocol adapters (no business logic)
- Config: Key Vault → config-sync → appsettings.json (local), env vars via Helm (AKS)
- `DefaultAzureCredential` everywhere — works for `az login` dev and managed identity prod

**Key file paths:**
- Solution: `dotnet-agent-framework.sln` (3 projects, flat structure)
- Config sync: `src/config-sync/Program.cs` (22 Key Vault secrets → appsettings.json)
- Seed tool: `src/seed-data/CrmSeeder.cs` (6 Cosmos containers, partition keys defined)
- Agent pattern: `src/simple-agent/Program.cs` (AzureOpenAIClient → GetChatClient → AsAIAgent)
- Security model: `docs/security.md` (RBAC matrix, consent model, network isolation)
- Business scenarios: `business-scenario.md` (8 test scenarios, MCP tool specs, data model)
- Infra: `infra/terraform/main.tf` (22 modules), `infra/deploy.ps1` (7-phase deployment)

**Concerns raised:**
- C1: Solution file needs solution folders when 8 containers are added
- C2: No resilience/retry strategy documented for inter-service HTTP
- C3: Shared appsettings.json gives every component every secret locally
- C4: Microsoft.Agents.AI at RC2 — API may change
- C5: Seed data missing 3 customers referenced in scenarios 6-8
- C6: No API versioning strategy for CRM API
- C7: No OpenTelemetry/distributed tracing planned
- C8: Test projects declared in docs but not created

**Implementation order recommended:** CRM API → CRM MCP → Knowledge MCP → CRM Agent → Product Agent → Orchestrator → BFF API → Blazor UI (follows dependency DAG bottom-up)

**NuGet versions established:** Azure.AI.OpenAI v2.1.0, Microsoft.Agents.AI v1.0.0-rc2, Microsoft.Azure.Cosmos v3.46.1, Azure.Identity v1.13-14.x, ModelContextProtocol C# SDK (TBD)

### 2026-03-19 — Cross-Team Finding: Full Codebase Analysis Complete

**Team Update (from all 5 agents):** Architecture is fully specced and infrastructure is provisioned, but **zero application code exists yet.** This is the intended state at end of Phase 1 (infrastructure/tooling complete). All 5 agents confirm the critical path: CRM API first, then MCP servers, then agents, then BFF, then UI. No fundamental re-design needed. All decisions merged into `.squad/decisions.md` with consensus on next steps.

### 2025-07-25 — Folder Structure Reorganization

**Problem:** Two structural issues flagged by Almir: (1) Dockerfile + Helm templates wrongly placed in `docs/templates/` — docs/ should only contain documentation; (2) K8s manifests split between `infra/terraform/manifests/` and `infra/k8s/network-policies/`.

**Actions taken:**
1. Moved `docs/templates/` → `infra/templates/` (Dockerfile.template + helm-base chart skeleton). docs/ now contains only documentation files.
2. Moved `infra/terraform/manifests/` → `infra/k8s/manifests/` to consolidate all Kubernetes YAML under `infra/k8s/`. Updated Terraform `templatefile()` paths from `${path.module}/manifests/` to `${path.module}/../k8s/manifests/` — verified Terraform resolves relative paths correctly.
3. Updated all cross-references across 7 files (main.tf, security.md, templates README, serviceaccount.yaml, network-policies README, infra README, decisions.md).

**Key constraint respected:** Terraform's `kubectl_manifest` resources use `templatefile()` with a relative path. The manifests must remain accessible to Terraform — the `../k8s/manifests/` relative path from the terraform directory satisfies this.

**Final structure:**
- `docs/` — documentation only (architecture, labs, security)
- `infra/templates/` — reference Dockerfile + Helm patterns
- `infra/k8s/manifests/` — namespace + service accounts (Terraform-applied)
- `infra/k8s/network-policies/` — NetworkPolicy YAMLs (manually applied)

### 2025-07-25 — Architecture Review: CRM API (Component 1)

**Reviewed:** Brian's complete CRM API implementation (`src/crm-api/`).

**Verdict:** ⚠️ APPROVED WITH NOTES — architecturally sound, one fix required.

**What passed (11/12 clean):** Project structure (Models/Services/Endpoints/Middleware), self-contained (zero project refs), all 11 REST endpoints with /api/v1/ prefix, Cosmos DB partition keys match seed data exactly, ProblemDetails error handling with GlobalExceptionHandler, /health + /ready health checks, configuration with DefaultAzureCredential tenant pinning, multi-stage Dockerfile matching template, deploy script with proper parameters, CI/CD with path filter + OIDC + CAE flag, independence (extractable to standalone repo).

**Issue found:** Service account name mismatch — Helm `values.yaml` uses `crm-api-sa` but Terraform provisions `sa-crm-api`. Will break workload identity federation in AKS. Brian to fix.

**Minor notes:** Two unused NuGet packages (Newtonsoft.Json, Microsoft.Extensions.Http.Resilience) should be removed. `GetAllPromotionsAsync` only returns active promotions — naming could be clearer.

**Full review:** `.squad/decisions/inbox/stewie-crm-api-review.md`

### 2025-07-25 — Documentation Accuracy Audit (README.md + implementation-plan.md)

**Scope:** Full factual verification of every claim in README.md and the first 100 lines of docs/implementation-plan.md against the actual codebase.

**Key findings:**
- README is ~90% accurate. Most numeric claims (11 endpoints, 10 tools, 15 PNGs, 6 CSVs, 20 modules, 8 phases) are correct.
- **DAG error in implementation-plan.md:** Knowledge MCP shown as depending on CRM API — it depends only on AI Search (confirmed by network policy, README traffic paths, and knowledge-mcp README). This is misleading.
- **DAG incomplete:** BFF API → CRM API direct dependency (Traffic Path 1) not shown in the DAG.
- **Module count discrepancy:** README says "20 modules" (correct for module directories), but main.tf has 34 module blocks (includes RBAC, private-endpoint, and other parameterized instantiations). history.md's "22 modules" claim is also wrong.
- **contoso-sharepoint structure:** README says "TXT + PDF" flat, but actual structure has 3 subdirectories (guides/, policies/, procedures/) with 12 PDFs + 12 TXTs.
- **Terraform files understated:** README lists 6 .tf files but imports.tf also exists (not listed).
- **"8 managed identities" is technically correct** (5 managed + 3 agent = 8) but the table doesn't mention id-kubelet.
- **SA mismatch noted in history.md is resolved** — crm-api Helm chart now correctly uses sa-crm-api.
- **history.md "7-phase deployment" is stale** — actual implementation has 8 phases (0-7).
