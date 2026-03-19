# Squad Decisions

## Active Decisions

### Architecture & Component Design

**Decision (Tyrion):** Production-grade architecture is sound. 8-container microservice design with clean boundaries, proper DAG dependency flow, and well-defined communication patterns. No fundamental redesign needed. Implementation should follow the specified order: CRM API → MCP Servers → Agents → BFF → UI.

**Decision (Tyrion):** Solution file will need solution folders when 8 containers are added. Organize by domain: `Domain APIs`, `MCP Servers`, `Agents`, `Frontend` to maintain clarity as the project grows.

**Decision (Tyrion):** Resilience strategy must be defined before implementing inter-container communication. Plan for circuit breakers (Polly), timeout policies, retry with exponential backoff, and health checks (`/healthz`) on every service for Kubernetes readiness probes.

**Decision (Arya):** CRM API is the critical path. No other backend component can progress until it exists. Build CRM API first (11 endpoints, Cosmos DB integration, cross-partition query handling).

**Decision (Arya):** Error handling standards must be established before CRM API implementation. Define a standard error response model with consistent HTTP status codes.

**Decision (Arya):** Agent prompt specifications are not documented. These must be written before CRM Agent, Product Agent, and Orchestrator Agent implementation begins.

**Decision (Arya):** Seed data gap: Business scenario references customers 106 (Mike Johnson), 107 (Anna Roberts), 108 (Tom Garcia) who are missing from customers.csv. Add these three customers to seed data before scenarios 6-8 can be tested.

**Decision (Arya):** Cross-partition query strategy for GET /orders/{id}: Orders are partitioned by `/customer_id` but the endpoint uses order ID. Decision: accept the cross-partition query cost (small dataset, acceptable). Document as a known trade-off.

### Backend & Data

**Decision (Arya):** Configuration strategy for local dev is sound (Key Vault → appsettings.json via config-sync). Acceptable trade-off that all 8 containers see all secrets locally — this is a workshop framework, not production. In AKS, Helm values naturally scope per chart.

**Decision (Arya):** No API versioning strategy is defined. Recommend URL prefix versioning (`/api/v1/`) from the start so CRM MCP tools and other consumers are prepared for schema changes.

**Decision (Arya):** Health check endpoints must be added to CRM API and BFF API. Implement `/health` (liveness) and `/ready` (readiness with dependency checks) for Kubernetes integration.

**Decision (Arya):** Logging strategy is missing. For 8 containers in AKS, implement: structured logging (ILogger + JSON), correlation IDs across services, and OpenTelemetry or Application Insights integration.

**Decision (Arya):** Rate limiting should be added to BFF API chat endpoint to prevent Azure OpenAI cost spikes from untrusted callers.

### Frontend & UI

**Decision (Sansa):** Blazor WASM UI is fully specified but has zero implementation. Start with scaffolding (dotnet new blazorwasm + MudBlazor + MSAL + SignalR.Client + Markdig packages, Dockerfile). Priority order: Auth → Shell → Chat Core → SignalR → State → Error Handling → Accessibility → Testing → Polish.

**Decision (Sansa):** Image URL rewriting pattern is well-defined. Markdown images (`![alt](filename.png)`) must be rewritten by ChatMessage.razor to BFF proxy URLs (`/api/images/{filename}`). Implement with Markdig post-processing or custom extension.

**Decision (Sansa):** State management in Blazor WASM should use scoped services (not Fluxor/Redux). The app is focused (chat + data views), so simple service injection (ConversationState) is sufficient. Events flow up via EventCallback, state flows down via parameters.

### Testing & Quality

**Decision (Varys):** Zero tests exist today. This is acceptable at infrastructure/tooling phase. When application code lands, tests must land simultaneously. Never implement a feature without tests.

**Decision (Varys):** CrmSeeder is the only immediately testable logic (218 lines). Unit tests should be added for CSV parsing, type conversion, and Entra ID mapping before any other work begins. This guards the critical data path.

**Decision (Varys):** Test framework consensus: xUnit + FluentAssertions + NSubstitute for unit/integration tests. WebApplicationFactory for API integration tests. bUnit for Blazor component rendering. None of these are referenced yet; add to solution when first test project is created.

**Decision (Varys):** Per-component test priority: CRM API (most logic) → BFF API (security boundary) → Orchestrator (routing correctness) → MCP Servers (tool contracts) → Agents (behavior with mocks) → Blazor UI (component rendering).

### Infrastructure & Deployment

**Decision (Bran):** 21 Terraform modules are production-grade and require no redesign. Dual-track identity model (managed identities + Entra Agent IDs) is clean and forward-looking. Module versioning (v1/v2 convention) is correct.

**Decision (Bran):** No Dockerfiles exist. This is a blocking issue before AKS deployment. All 8 components need multi-stage Dockerfiles. Create alongside application code, not after.

**Decision (Bran):** No Helm charts exist. K8s deployment requires Helm chart (or set of charts per component) with proper Deployments, Services, Ingress rules, ConfigMaps, Secrets, resource limits, health probes, and HPA configuration.

**Decision (Bran):** AKS Contributor role assigned to control plane identity is too broad. Scope to specific resources or create custom role with minimal permissions (cluster upgrade, node pool management).

**Decision (Bran):** Cosmos DB and AI Search use API keys in some integration paths. Long-term: migrate to full Azure AD authentication to eliminate key dependency and reduce secret management burden.

**Decision (Bran):** K8s security posture is incomplete. Add: NetworkPolicy rules for pod-to-pod isolation, PodSecurityStandards (restricted profile), and OPA/Gatekeeper for admission control.

**Decision (Bran):** EventGrid module is defined but not integrated into main.tf. Decision: wire it in when knowledge source auto-indexing refresh is needed, or remove to reduce configuration noise.

**Decision (Bran):** Deployment pipeline (deploy.ps1/sh) is well-designed with safety guardrails (firewall bracketing, policy diagnostics, soft-delete purge). No changes needed.

**Decision (Bran):** Bootstrap scripts support local dev and GitHub Actions CI/CD. OIDC federation is used everywhere (zero stored credentials). Security posture is strong. No changes needed.

### Cross-Team

**Decision (Whole Team):** Architecture is specced, infrastructure is provisioned, zero application code exists. This is the intended state at end of Phase 1. All 5 agents agree on the critical path: CRM API first, then MCP servers, then agents, then BFF, then UI. No fundamental redesign needed.

**Decision (Whole Team):** Business scenario is comprehensive and test cases are deterministic. 8 customer scenarios map to specific data. This is a strength for automated validation.

**Decision (Whole Team):** The RC2 status of Microsoft.Agents.AI SDK should be monitored. Pin the version and abstract the agent construction pattern. Be prepared for breaking changes in GA release.

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction
