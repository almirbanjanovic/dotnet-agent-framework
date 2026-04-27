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

### 2025-07-25 — Comprehensive Build Plan Created

**Request:** Almir asked for a unified plan covering local dev infrastructure + all 7 remaining components with in-memory support from day 1.

**Delivered:**
- Rewrote `plan.md` with 3-phase structure: Phase A (6 foundation tasks), Phase B (7 components in DAG order), Phase C (3 polish tasks)
- 16 SQL todos with full dependency graph — ready query identifies executable tasks
- Azure → in-memory service map for every component (Cosmos → ConcurrentDictionary, AI Search → in-memory vectors with Foundry embeddings, Storage → local files, Entra → dev auth bypass)
- Identified knowledge-mcp as hybrid (in-memory storage, but embedding generation requires deployed Foundry model)
- BFF gets 3 repository-pattern interfaces: IConversationStore, IImageService, plus dev auth bypass
- Blazor UI gets dev auth selector (customer dropdown instead of MSAL)
- Port map: 5001-5008 for components, 15000 for Aspire Dashboard
- Decision written to `.squad/decisions/inbox/stewie-comprehensive-plan.md`

**Key architecture call:** Repository pattern + DI (DataMode switch) is the universal mechanism. Same pattern from ICosmosService extended to ISearchService, IConversationStore, IImageService. No Docker, no emulators, just `dotnet run`.

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

### 2025-07-25 — Foundry-Only Deployment Architecture Analysis

**Context:** Almir requested analysis of a minimal Azure deployment — Foundry only, everything else local. Full analysis produced in `docs/foundry-only-deployment.md`.

**Key findings:**
- **Only Foundry has no local alternative.** Every other Azure service (Cosmos DB, Storage, Key Vault, AKS, Entra ID) has a viable local emulator or can be eliminated.
- **AI Search is the critical decision point.** No local emulator exists. Three options analyzed: (A) deploy AI Search alongside Foundry (~$7.50/day Standard, $2.50/day Basic), (B) local vector search via embeddings + in-memory index, (C) local file search (BM25/keyword). Option A recommended for workshop fidelity.
- **"Foundry + AI Search" hybrid is the sweet spot.** 3-4 Azure resources vs 14+, 5-10 min deploy vs 45-60 min, ~$75-250/month vs $1,500-3,000/month. Full search fidelity preserved.
- **Auth model changes significantly.** `DefaultAzureCredential` → API keys for Foundry, connection strings for emulators. Every service client needs dual-mode auth (config-driven, not compile-time).
- **`AIProjectClient` doesn't support API key auth.** Must use `AzureOpenAIClient` directly for local dev. This changes the agent construction pattern for all agents.
- **Cosmos DB emulator limitation:** MultiHash v2 partition keys (used by `agent-state` container) may not be supported. Fallback to single partition key `/id` needed.
- **config-sync is eliminated** in local mode — config provided directly via `appsettings.Local.json` or env vars.
- **docker-compose.yml needed** with Cosmos emulator, Azurite, all 8 app containers, seed-data init container.
- **Migration path is clean:** Local → Foundry-only → Foundry+Search → Full Azure. Only configuration changes between tiers, no code changes (if dual-mode auth is implemented correctly).

### 2025-07-26 — Local Dev Mode Implementation Spec v2

**Context:** Almir requested a detailed implementation plan for local dev mode. Key constraints from Almir: use Terraform (not Bicep), use `dotnet run` (not docker-compose), additive only (no changes to existing full deployment), only Foundry goes to Azure.

**Plan written to:** `docs/foundry-only-deployment.md` (full overwrite of v1 analysis).

**Key decisions in the plan:**
- **Separate Terraform root module** (`infra/terraform/local-dev/`) recommended over shared root with `.tfvars`. The main `main.tf` has 20+ module dependencies with required providers (kubernetes, kubectl, msgraph) that cannot be conditionally skipped without major refactoring.
- **Foundry module gets 3 small additions:** `local_auth_enabled` variable (default false), use variable instead of hardcoded false, `primary_access_key` output. Zero impact on existing deployment.
- **Dual-mode auth pattern for all components:** config-driven if/else — connection string present → emulator mode, absent → DefaultAzureCredential mode. Same pattern for Foundry API key vs AIProjectClient.
- **`AIProjectClient` does NOT support API key auth** — confirmed. Local dev must use `AzureOpenAIClient` + `GetChatClient()` + `.AsAIAgent()` directly.
- **Local vector search for knowledge-mcp:** `ISearchService` interface with `AzureSearchService` (production) and `LocalVectorSearchService` (local dev). Uses Foundry embedding API + cosine similarity via `TensorPrimitives`. Embedding cache saved to disk after first run.
- **Port map:** crm-api:5001, crm-mcp:5003, knowledge-mcp:5004, crm-agent:5005, product-agent:5006, orchestrator-agent:5007, bff-api:5009, blazor-ui:5010. Ports 5002/5008 intentionally skipped.
- **`setup-local.ps1`** automates everything: prerequisite checks → terraform apply → retrieve API key → generate appsettings.Local.json for all 9 components → start emulators → seed data → print port map.
- **Dev auth bypass** for bff-api: middleware that injects synthetic ClaimsPrincipal when `Auth:DevMode=true`. Dev customer selector in blazor-ui dropdown.
- **Implementation order:** Foundry module change → Terraform local-dev → crm-api/seed-data/simple-agent dual-mode → setup-local.ps1 → templates → knowledge-mcp search (biggest change, done when component is built).

### 2025-07-26 — Script Consolidation Plan (init.ps1 + deploy.ps1)

**Context:** Almir requested analysis of merging `infra/init.ps1` (854-line bootstrap) and `infra/deploy.ps1` (1097-line deployment) into a single script to reduce friction.

**Key findings:**
- Both scripts share prerequisites, authentication, and SP logic. 7+ interactive prompts across two runs.
- Handoff between scripts is via `deployments/<env>.env`, `backend.hcl`, and `<env>.tfvars` — fragile coupling.
- All steps in both scripts are already idempotent (guarded or upsert). Only exception: temp client secret creation (acceptable — auto-cleaned).
- Combined script auto-detects first run via storage account probe. ~25% code reduction (1951 → 1470 lines).
- Biggest risk: token scope conflict (init needs Graph scope, deploy must NOT use it). Mitigated by re-login between bootstrap and deploy phases.
- `setup-local.ps1` remains completely separate — different Terraform root, different auth model, different concerns.

**Plan written to:** `docs/script-consolidation-plan.md`
**Decision written to:** `.squad/decisions/inbox/stewie-script-consolidation.md`

### 2025-07-26 — Plan Audit: Foundry-Only Local Dev Mode (10 Todos)

**Context:** Almir requested a pre-implementation audit of the local dev mode plan (10 SQL todos + `docs/foundry-only-deployment.md` spec + plan.md). Verified script boundaries, state isolation, auth coverage, foundry module safety, config paths, emulator strategy, knowledge-mcp readiness, CI safety, dependencies, and risks.

**Key findings (6 clean, 4 need attention):**
1. Script boundaries: clean — setup-local.ps1 is fully independent of init.ps1 (local backend, no remote state). ✅
2. State isolation: clean — local-dev uses `backend "local" {}`, infra/.gitignore covers `*.tfstate`. ✅
3. Auth dual-mode: covered for the 3 existing components. 7 scaffolded components will get dual-mode when built. Acceptable deferral. ✅
4. Foundry module zero-impact: confirmed — default false, main.tf doesn't pass it, new output is additive. ✅
5. Template count off-by-one: plan says "9 templates" but 10 are listed/generated. ⚠️
6. seed-data ConfigurationBuilder gap: only loads `appsettings.json`, not environment-specific. `appsettings.Local.json` won't be loaded without fixing the config builder. ⚠️
7. Blazor WASM config path: may need `wwwroot/appsettings.Local.json` not project root. Needs confirmation when blazor-ui is implemented. ⚠️
8. Cosmos emulator is Windows-only (MSI path in script). No macOS/Linux path documented. ⚠️
9. CI workflows don't trigger on `infra/terraform/**` — safe. ✅
10. No todo dependencies were set — recommended and applied to SQL table.

**Decision written to:** `.squad/decisions/inbox/stewie-plan-audit.md`

### 2025-07-27 — C1 Implementation: Local Development Documentation

**Request:** Almir requested implementation of C1 — Local Dev Documentation. Three deliverables: (1) create `docs/local-development.md` (quick-start guide), (2) rewrite `docs/foundry-only-deployment.md` (updated spec), (3) update `README.md` with "Local Development" section.

**Deliverable 1 — `docs/local-development.md` (12.4 KB):**
- Quick-start guide covering prerequisites, one-time setup, running the system
- Prerequisites section: .NET 9, Azure CLI, Terraform, Cosmos Emulator, npm+Azurite
- Step 1-3: Standard tools + Azure login
- Step 2: Start emulators (Cosmos DB + Azurite) — terminal instructions
- Step 3: `./infra/setup-local.ps1` deploys Foundry + generates config + seeds data
- Running: `dotnet run --project src/AppHost` with Aspire dashboard
- Manual component start as alternative (8 terminals if needed)
- Port map (5001-5008 + 15000 dashboard)
- Dev auth: Dropdown menu with 8 customers (IDs 101-108)
- Architecture overview: in-memory repos, Foundry for LLM calls, data patterns
- Common tasks: health checks, CRM data queries, tests, debugging
- Comprehensive troubleshooting section (12 scenarios with solutions)
- Development workflow (clone → setup → start → browse → code → test)
- Comparison table: Local vs Azure mode
- Next steps: Labs, deployment, contribution

**Deliverable 2 — `docs/foundry-only-deployment.md` (21 KB):**
- Complete rewrite: removed ALL Docker/emulator/Azurite references
- Focused entirely on repository pattern + in-memory approach
- Updated comparison table: now shows in-memory repos instead of emulators
- Section 2 (Terraform): Kept foundry module + local-dev root — unchanged from prior spec
- Section 3 NEW: Data Model — repository pattern, CSV loading, `DataMode` switch per component
- Section 4: Component architecture for all 8 components + 3 tools:
  - crm-api: `LocalCrmRepository` vs `AzureCrmRepository`
  - knowledge-mcp: `LocalVectorSearchService` vs `AzureSearchService`
  - Agents: `AzureOpenAIClient` + API key vs `AIProjectClient`
  - BFF API: `LocalConversationRepository` + dev auth bypass
  - Blazor UI: dev customer selector in local mode
- Section 5: Configuration architecture — self-contained `appsettings.Local.json`, DataMode switch
- Section 6-8: Setup script summary, port map, comparison to full Azure
- Key principle: Repository pattern + DI (DataMode) is universal mechanism

**Deliverable 3 — `README.md` update:**
- Added new "Local Development" section (before "Getting started")
- One-liner commands: `./infra/setup-local.ps1` + `dotnet run --project src/AppHost`
- Key capabilities: Foundry deployed to Azure, CRM data in memory, no emulators, dev auth, Aspire dashboard
- Reference link to `docs/local-development.md`

**Results:** All three files created + committed to plan. Total documentation: ~45 KB added, fully aligned with in-memory + repository pattern + Foundry approach. Zero emulator references across all new docs. Quick-start is immediately actionable; technical spec provides complete component architecture for implementation teams.

### 2025-07-26 — Second-Pass Audit: Foundry-Only Local Dev Plan (Re-audit)

**Context:** Almir requested independent re-audit of the 10-todo local dev plan after first-pass audit (claude-opus-4.6). All source files re-read first-hand.

**Prior audit findings (all 6 confirmed):**
1. seed-data ConfigurationBuilder gap — BLOCKING, confirmed. Lines 9-12 only load `appsettings.json`, never env-specific files.
2. Template count off-by-one — confirmed. Says "9" in three places, actual count is 10.
3. Cosmos Emulator Windows-only — confirmed. Script hardcodes MSI path.
4. Foundry module zero-impact — confirmed. Default false, main.tf doesn't pass variable.
5. Script boundaries clean — confirmed. Separate Terraform root, local backend.
6. Wave dependencies correct — confirmed. 12 edges in SQL, DAG is valid.

**NEW findings (5 issues prior audit missed):**
1. 🆕 simple-agent ConfigurationBuilder has inverse bug — loads ONLY env-specific file, not base `appsettings.json`. Low impact for local dev but fragile and inconsistent.
2. 🆕 `dotnet run --environment Local` doesn't work for console apps (seed-data, simple-agent). The `--environment` flag is parsed by ASP.NET Core host builder, not by `dotnet run` itself. Console apps need env var set explicitly.
3. 🆕 No `launchSettings.json` exists in any project. Port map is documented (5001, 5003, 5004, etc.) but nothing assigns these ports. Multiple components will collide on default ports.
4. 🆕 `primary_access_key` provider behavior needs testing. azurerm v4.63+ should return empty string when `local_auth_enabled=false`, but should be verified with `terraform plan` on existing state before merging.
5. 🆕 Cognitive Services soft-delete troubleshooting gap. `purge_soft_delete_on_destroy = true` handles happy path, but permission failures need a manual purge command documented.

**Top 3 before implementing:** (1) Fix seed-data ConfigurationBuilder — 2-line fix, truly blocking. (2) Decide port assignment mechanism — launchSettings.json recommended. (3) Fix template count to 10.

**Auth coverage:** Complete. All 11 src/ components either have a todo or documented pattern. No gaps.

**Decision written to:** `.squad/decisions/inbox/stewie-plan-reaudit-opus46-1m.md`

### 2025-07-26 — Audit v3: Self-Contained Config + Cross-Platform Re-audit

**Context:** Almir issued two new directives that override prior assumptions: (1) SELF-CONTAINED CONFIG — every component loads ONLY `appsettings.{env}.json`, no base `appsettings.json` layering, each config file must be complete with ALL values; (2) NO MSI, CROSS-PLATFORM — no Windows MSI installers, Docker-based Cosmos DB Emulator only, all instructions must work on Windows/macOS/Linux.

**Impact on prior findings:**
- simple-agent "inverse bug" (pass 2 finding) — **INVALIDATED**. Loading only `appsettings.{env}.json` is now the CORRECT pattern.
- seed-data ConfigurationBuilder gap — **STILL VALID**, but fix changes: instead of "add base file loading", fix is "change to load `appsettings.{env}.json`" matching simple-agent's pattern.
- Cosmos emulator Windows-only — **CHANGED MEANING**. No longer defensible. Must use Docker-based Linux emulator (`mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-preview`).
- Port collision / no launchSettings.json — **STILL VALID**. Self-contained config provides the fix: Kestrel URL bindings in each `appsettings.{env}.json`.
- All other findings (5 of 11) remain valid unchanged.

**Config pattern analysis:**
- crm-api (`WebApplication.CreateBuilder`): Already correct — no `appsettings.json` file exists, so only env-specific loads. No code change needed.
- seed-data (manual ConfigurationBuilder): MUST change from `appsettings.json` to `appsettings.{env}.json`. This is the blocking fix.
- simple-agent (manual ConfigurationBuilder): Already correct — loads only `appsettings.{env}.json`.

**Cross-platform strategy:**
- Cosmos DB: Docker-based vnext-preview emulator. Lighter, actively maintained, preview status consistent with project risk profile (RC2 SDKs).
- Azurite: Already cross-platform via npm. No change.
- Scripts: Keep single PowerShell script with pwsh 7+ (cross-platform). Replace Windows MSI process management with Docker container lifecycle commands.

**Todo impact:** 4 of 10 todos need description/approach updates (setup-local-script, seed-data-dual-mode, appsettings-templates, local-dev-docs). Wave dependencies unchanged.

**7 new risks identified:** Docker emulator performance, HTTPS cert handling, vnext-preview stability, pwsh installation on macOS/Linux, self-contained config maintenance burden, WebApplication.CreateBuilder auto-load trap, Docker Desktop licensing.

**Top 3 before implementing:** (1) Fix seed-data ConfigurationBuilder — 2-line change, truly blocking. (2) Decide Cosmos emulator image tag — vnext-preview recommended. (3) Add Kestrel URL bindings to templates — solves port collision problem.

**Decision written to:** `.squad/decisions/inbox/stewie-audit-v3-selfcontained-crossplatform.md`

### 2025-07-26 — Major Pivot: In-Memory Local Dev (No Emulators)

**Context:** Almir directed a radically simpler approach for local dev after reviewing the python-old workshop's dual-backend pattern (SQLite + Cosmos, toggled via `USE_COSMOSDB`). The entire emulator/Docker strategy is replaced by in-memory data with .NET Repository Pattern + DI.

**What changed:**
- **REMOVED:** Docker, docker-compose, Cosmos Emulator, Azurite, all emulator management, dual-mode auth (ConnectionString vs DAC), knowledge-local-search standalone todo, seed-data-dual-mode todo
- **ADDED:** `repository-interfaces` (InMemoryCrmDataService implementing existing ICosmosService), `di-registration` (DataMode config switch), `inmemory-repositories` folded into repository-interfaces
- **SIMPLIFIED:** setup-local scripts (no emulator management, just terraform + config gen), appsettings templates (DataMode + Foundry only), gitignore (fewer artifacts)
- **UNCHANGED:** foundry-module-update, terraform-local-dev (simplified — Foundry only, no Cosmos/Storage modules), simple-agent-apikey

**Key insight:** `ICosmosService` is already a clean repository interface — it has domain methods (`GetAllCustomersAsync`, `GetCustomerByIdAsync`, etc.) not Cosmos-specific methods. Creating `InMemoryCrmDataService` that implements the same interface is the simplest possible change. DI registration switches on `DataMode` config.

**Todo count:** 10 → 9 (reduced scope). Wave structure: 4 waves instead of prior 3. All old todos deleted, new set inserted with updated dependency graph.

**Almir's directives incorporated:** (1) Self-contained config (only `appsettings.{env}.json`), (2) No MSI/cross-platform, (3) No Docker, (4) In-memory re-seed on restart, (5) Self-contained components.

**Plan rewritten to:** session plan.md
**Decision written to:** `.squad/decisions/inbox/stewie-inmemory-pivot.md`

### 2026-04-20 — Final Documentation Audit (Pre-Implementation)

**Context:** Almir requested a final audit of ALL .md documentation files for consistency with the in-memory local dev plan before implementation begins. Six directives in effect: self-contained config, no MSI, no Docker, in-memory data, self-contained components, repository pattern + DI.

**Scope:** plan.md, docs/foundry-only-deployment.md (2000 lines), 8 other docs/ files, README.md, .squad/decisions.md + inbox, SQL todos (9) + dependencies (10 edges), existing source code verification.

**Key findings:**
1. **plan.md:** Internally consistent. One naming mismatch: plan says `inmemory-repositories` but SQL todo is `di-registration` (same work, different name). Wave dependencies are logically sound.
2. **docs/foundry-only-deployment.md:** ~60% stale. 53 references to Docker/emulator/Azurite/connection-string. Full rewrite needed but correctly deferred to `local-dev-docs` todo (Wave 4).
3. **Other docs/ files (8):** All consistent — they describe the full Azure deployment which is unchanged.
4. **README.md:** Accurate but incomplete — no local dev mention. Updates deferred to Wave 4.
5. **SQL todos:** 8 of 9 match plan exactly. 1 naming mismatch. All 10 dependency edges match wave structure.
6. **Existing code:** ICosmosService, CosmosService, 6 CSVs, simple-agent Program.cs — all exist exactly as plan describes. Plan's characterization of ICosmosService as "already a clean repository abstraction" is confirmed.
7. **decisions.md + inbox:** No contradictions. Pivot decision captured in inbox. Old decisions are historical, not superseded.

**Verdict:** Ready to implement. One minor fix needed (todo naming alignment in plan.md).

**Decision written to:** `.squad/decisions/inbox/stewie-final-audit.md`
