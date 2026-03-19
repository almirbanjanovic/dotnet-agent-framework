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

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction
### 2026-03-19T16:15: User directive
**By:** Almir Banjanovic (via Copilot)
**What:** Every squad member must use Claude Opus 4.6 (1M context) (`claude-opus-4.6-1m`) as the default model. This overrides the standard model selection hierarchy (Layer 1 — User Override).
**Why:** User request — captured for team memory
# Decision: Agent Identity v2 via msgraph

## Context
The Entra Agent ID platform requires Graph beta types for Agent Identity Blueprints and Blueprint Principals, which the AzureAD provider cannot create. The existing v1 module used regular Entra app registrations/service principals that do not represent the specialized Agent Identity objects. Cosmos DB RBAC for the agents was also missing, leaving only the BFF principal assigned.

## Decision
Move agent identity provisioning to a new `agent-identity/v2` module built on the `microsoft/msgraph` provider and Graph beta endpoints. The lifecycle is: create Agent Identity Blueprint → create Blueprint Principal → runtime Agent Identity instances are created by the blueprint service (not Terraform). Bind the blueprint to AKS service accounts via federated identity credentials for workload identity. Update Cosmos DB RBAC to include CRM, Product, and Orchestrator agent principals alongside the BFF.

## Consequences
Terraform now provisions the correct Entra Agent ID objects, aligning with the platform requirements and enabling runtime agent identity creation. AKS workload identities remain secretless and use OIDC token exchange through FICs. Cosmos DB access is corrected for all agents, reducing authorization gaps.
