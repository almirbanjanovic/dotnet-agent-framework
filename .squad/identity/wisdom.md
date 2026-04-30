---
last_updated: 2026-04-22T00:00:00.000Z
---

# Team Wisdom

Reusable patterns and heuristics learned through work. NOT transcripts — each entry is a distilled, actionable insight.

## Patterns

**Pattern:** After any large implementation push, run an intent-vs-implementation audit before declaring "done." Read every README, charter, doc, and `.copilot`/`.squad` config; compare against the actual code in `src/`. Doc drift is the first sign of stale assumptions baked into agent behavior. **Context:** end of any milestone where multiple components were built or reworked.

**Pattern:** Architectural directives must be machine-enforced or they will drift. The "self-contained components" rule was advisory for ~13 months and a `ServiceDefaults` shared project quietly accumulated 7 consumers. The fix was (a) inline the shared code, (b) add a fitness test that asserts zero `ProjectReference` elements in non-AppHost projects under `src/`, (c) wire the test into a CI workflow gated on `src/**/*.csproj` changes, and (d) document the rule in `.github/copilot-instructions.md` so coding agents see it on every interaction. The unit test alone is not enough — a CI workflow turns it into a hard merge gate. **Context:** any time a directive starts with "all X must be Y" — write the assertion, not just the doc.

**Pattern:** Per-component containerization checklist — every new .NET service in `src/<name>/` needs five things before it's deploy-ready: (1) Dockerfile that copies `Directory.Build.props` + `Directory.Packages.props` + the service's own csproj so `dotnet restore` works under Central Package Management (the service is fully self-contained — no `ProjectReference` to anything else under `src/`, see the [component-independence edict](../../src/README.md#architectural-edict--component-independence)); (2) Helm chart at `src/<name>/chart/` (clone `src/crm-api/chart/` and customise Chart.yaml + values.yaml only — the `templates/` are generic); (3) Terraform-managed service account name (`sa-<short>`) referenced in `values.yaml`; (4) `Logging`, `ASPNETCORE_URLS`, and component-specific config keys in the chart's ConfigMap; (5) secret refs for any Key Vault-backed values. **Context:** adding a new microservice or fixing one that was scaffolded without deploy assets.

**Pattern:** When introducing a new request field across a request/response chain (BFF → orchestrator → specialist agents), make it an optional positional record parameter (`IReadOnlyList<HistoryMessage>? History = null`) so existing positional `new ChatRequest("a","b")` callers compile unchanged, and JSON property-name matching across project boundaries does the wire wiring without shared models. Add tests at the wire layer (`OrchestratorClientTests`) AND the integration layer (`ChatPipelineIntegrationTests`) so a regression in either the serialization or the population logic fails fast. **Context:** any cross-service contract change to a record-shaped DTO.
