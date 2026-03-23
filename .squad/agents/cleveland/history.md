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

### 2026-03-23T16:30: High Stage Security Review (T-05 through T-08)
- Reviewed 4 commits across infra/terraform and CI/CD workflows for correctness and security.
- **T-05 (a061410):** Pin kubernetes_version — default `"1.30"` added to `variables.tf`. Format is correct `major.minor` for AzureRM AKS provider. Prevents silent version drift when `AKS_KUBERNETES_VERSION` var is unset. APPROVED.
- **T-06 (c3a5839):** Fix approval gate hardcode — replaced hardcoded `"production"` with `${{ inputs.environment }}` in issue-title/body. No injection risk: `inputs.environment` is `type: choice` constrained to `[dev, staging, production]`. APPROVED.
- **T-07 (ee72793):** Fix seed-data dependency — added `terraform-apply` to seed-data `needs:` list alongside `cleanup-after-apply`. Correct fix: `cleanup-after-apply` uses `if: always()` so it could succeed even when apply fails, which would have allowed seed-data to run against non-existent infrastructure. `cleanup-after-apply` itself is unaffected (still runs with `if: always()`). APPROVED.
- **T-08 (8a50aa4):** CAE disable flags — added `ARM_DISABLE_CAE`, `AZURE_DISABLE_CAE`, `HAMILTON_DISABLE_CAE` to all 4 Terraform API-calling steps (plan init, plan, apply init, apply). Matches `deploy.ps1` parity (lines 400-402). `terraform validate` and `terraform fmt` intentionally excluded (local-only, no Azure API calls). No missed steps. APPROVED.
- All 4 commits approved. No security issues found. High stage gate cleared.
