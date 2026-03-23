# Cleveland — History

## Project Context

- **Owner:** Almir Banjanovic
- **Project:** dotnet-agent-framework — 8-container agentic AI system
- **Stack:** .NET 9, Minimal APIs, Blazor WebAssembly, MudBlazor, MCP C# SDK, Microsoft.Agents.AI, Azure.AI.OpenAI, Cosmos DB, Azure AI Search, Terraform, AKS, Helm, Docker
- **Containers:** Blazor WASM UI, BFF API, CRM API, CRM MCP, Knowledge MCP, CRM Agent, Product Agent, Orchestrator Agent
- **Infra location:** `infra/` — Terraform, Helm, deploy scripts
- **Joined:** 2026-03-23 — added to perform infrastructure and security audit before code implementation begins

## Learnings

### 2026-03-23: Critical Stage Security Review (T-01 + T-02)
- Reviewed all 9 NetworkPolicy manifests in `infra/k8s/network-policies/`. Design is sound: default deny, per-service ingress/egress, DNS access, PE subnet egress. One high-severity label mismatch: policies use `app: X` but Helm templates produce `app.kubernetes.io/name: X`. Must reconcile before deployment.
- Reviewed Dockerfile.template and Helm base chart in `docs/templates/`. Both follow security best practices: non-root user, readOnlyRootFilesystem, capabilities drop ALL, seccomp RuntimeDefault, resource limits, no inline secrets. Approved with two low-severity informational notes.
- Key insight: NetworkPolicies and Helm charts are authored by different tracks (infra vs. app). Label conventions must be agreed cross-team to avoid silent failures.

### 2026-03-23T15:42: Follow-up — NetworkPolicy Label Fix
- Joe completed the follow-up task (commit ff8d5ad): updated all 8 per-service NetworkPolicy files to use `app.kubernetes.io/name: {service-name}` selectors instead of `app: {service-name}`. This aligns with Kubernetes standard label conventions and matches what Helm chart templates naturally produce.
- Finding 1 (HIGH severity) from Cleveland's review now resolved. NetworkPolicies will correctly match Helm-deployed pods under default-deny baseline.
- Deployment gate cleared. Critical stage complete.
