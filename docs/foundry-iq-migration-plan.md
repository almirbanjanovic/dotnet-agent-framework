# Foundry IQ Migration Plan

> **Status:** DRAFT — not yet implemented. Updated 2026-06-11 with
> findings from a two-model adversarial review (GPT-5.5 + MAI-Code-1-Flash).
> Pending stakeholder approval before any code changes land.
>
> **Last updated:** 2026-06-11 (post-review revision)

## 1. TL;DR

Replace the hand-rolled `src/knowledge-mcp` service (which wraps Azure AI
Search directly) with **Foundry IQ**, Microsoft's managed knowledge-base
layer that sits on top of the existing AI Search service.

Foundry IQ does **not** remove the Azure AI Search dependency. Per the
[official concept doc](https://learn.microsoft.com/en-us/azure/foundry/agents/concepts/what-is-foundry-iq):

> "Azure AI Search provides the underlying indexing and retrieval
> infrastructure."

What Foundry IQ adds on top of what we have today:

| Layer | Today | With Foundry IQ |
|---|---|---|
| Indexing | AI Search **Knowledge Source** (PUT via preview data-plane API, auto-generates index/skillset/indexer) | Same — Knowledge Sources are reused as-is |
| Grouping | n/a — agents query one named index | **Knowledge Base** — collection of one or more knowledge sources with retrieval-behavior params |
| Query engine | Single semantic search call from `AzureSearchService` | **Agentic retrieval** — LLM-planned subquery decomposition, parallel execution, semantic rerank, citation synthesis |
| Tool surface | Custom MCP server (`src/knowledge-mcp`) exposing `search_knowledge_base` | Hosted MCP endpoint on the search service: `https://{search}.search.windows.net/knowledgebases/{kb}/mcp?api-version=2026-05-01-preview` exposing `knowledge_base_retrieve` |
| Permissioning | App identity reads the entire index | Per-source ACL sync, Purview labels, query-time enforcement under caller's Entra identity (preview-gated) |

The migration is mostly **deletion**: with retrieval handled by a managed
MCP endpoint, the bespoke knowledge-mcp deployment becomes optional.

## 2. Out-of-scope

- Replacing Cosmos DB, the CRM MCP, or any other component.
- Migrating Foundry IQ's underlying search service to a different SKU.
- Changing the agent framework or LLM deployments.
- Onboarding new knowledge sources (SharePoint, OneLake, web). Initial
  migration keeps the same `azureBlob` source we have today.

## 3. References (verified 2026-06-11)

- [What is Foundry IQ?](https://learn.microsoft.com/en-us/azure/foundry/agents/concepts/what-is-foundry-iq) — concept doc, last updated 2026-05-19.
- [Connect a Foundry IQ knowledge base to Foundry Agent Service](https://learn.microsoft.com/en-us/azure/foundry/agents/how-to/foundry-iq-connect) — how-to, last updated 2026-06-02.
- REST API surface: `2026-04-01` GA core + `2026-05-01-preview` for the full feature set including the MCP endpoint.
- Existing code: [`src/knowledge-mcp/`](../src/knowledge-mcp/), [`infra/terraform/modules/knowledge-source/v1/`](../infra/terraform/modules/knowledge-source/v1/), [`infra/terraform/main.tf`](../infra/terraform/main.tf) (search/RBAC/KV-secrets blocks).

## 4. Architecture before / after

### Before

```
agent (crm/product/orch) ──HTTP──► knowledge-mcp (AKS pod)
                                      │
                                      └──Azure.Search.Documents SDK──► AI Search index
                                                                          ▲
storage (sharepoint-docs) ─── Knowledge Source indexer (5-min) ───────────┘
```

### After (Option A — recommended)

```
agent (crm/product/orch) ──MCP (project connection)──► AI Search /knowledgebases/contoso-kb/mcp
                                                          │
                                                          ├── knowledge_base_retrieve tool
                                                          ├── LLM subquery planning (gpt-4.1-mini)
                                                          └── references Knowledge Source(s)
                                                                  ▲
storage (sharepoint-docs) ─── Knowledge Source indexer (5-min) ───┘
```

### After (Option B — hybrid for local-dev)

```
agent (cloud)  ──MCP──► AI Search /knowledgebases/contoso-kb/mcp
agent (local)  ──MCP──► knowledge-mcp (in-process / Aspire) ──► InMemorySearchService
```

## 5. Infrastructure changes

### 5.1 New Terraform module: `modules/knowledge-base/v1`

Mirrors the existing
[`modules/knowledge-source/v1`](../infra/terraform/modules/knowledge-source/v1/main.tf)
pattern: a single `terraform_data` resource with a `local-exec`
provisioner that PUTs to the AI Search data plane.

```hcl
PUT {search_endpoint}/knowledgebases/{kb_name}?api-version=2026-05-01-preview
{
  "name": var.kb_name,                   // human name of the knowledge BASE
  "knowledgeSources": [{
    "name": var.knowledge_source_name    // the NAME of the knowledge SOURCE,
                                          // NOT the generated index name. Today
                                          // module.knowledge_source is created
                                          // with name = var.search_index_name;
                                          // the generated index is
                                          // "<search_index_name>-index". Do NOT
                                          // confuse the two — using the index
                                          // name here makes the KB validate but
                                          // retrieve nothing.
  }],
  "retrievalInstructions": var.retrieval_instructions,
  "models": [{
    "kind": "azureOpenAI",
    "azureOpenAIParameters": {
      "resourceUri":  var.foundry_endpoint,
      "deploymentId": var.planner_model_deployment_name,
      "modelName":    var.planner_model_name
    }
  }]
}
```

**Auth (open question — must verify in spike):** the adversarial review
flagged that the `knowledgebases` REST surface may support RBAC
(OAuth2 `https://search.azure.com/.default`), even though the
`knowledgesources` endpoint we use today does not. **Action:** confirm
during the .NET-SDK spike (§6.3) and prefer Entra auth if supported.
If RBAC is unavailable, reuse the same admin-key pattern and extend
the accepted-risk entry in [docs/security.md](security.md).

### 5.2 New project-connection resource

Create a Foundry project `RemoteTool` connection so agents can target
the MCP endpoint with `authType=ProjectManagedIdentity` (per the
[foundry-iq-connect doc](https://learn.microsoft.com/en-us/azure/foundry/agents/how-to/foundry-iq-connect)):

```hcl
resource "azapi_resource" "kb_mcp_connection" {
  type      = "Microsoft.MachineLearningServices/workspaces/connections@2025-10-01-preview"
  parent_id = module.foundry.project_id
  name      = "contoso-kb-mcp"
  body = {
    properties = {
      authType    = "ProjectManagedIdentity"
      category    = "RemoteTool"
      target      = "${module.search.endpoint}/knowledgebases/${var.kb_name}/mcp?api-version=2026-05-01-preview"
      audience    = "https://search.azure.com/"
      isSharedToAll = true
      metadata    = { ApiType = "Azure" }
    }
  }
}
```

### 5.3 RBAC additions

In [`modules/rbac/search`](../infra/terraform/modules/rbac/search/v1/):

- Add the Foundry project's **system-assigned managed identity** to the
  `Search Index Data Reader` role assignments.
  **Prerequisite:** add a `project_principal_id` output to
  [`modules/foundry/v1/outputs.tf`](../infra/terraform/modules/foundry/v1/outputs.tf)
  — currently it exposes `project_id`, `project_name`, and
  `project_endpoint` only. Sourced from
  `azurerm_cognitive_account_project.default.identity[0].principal_id`.
- **If the .NET SDK gap (§6.3) forces the direct-REST fallback,** each
  agent's managed identity must ALSO be granted `Search Index Data
  Reader` because the agent — not the Foundry project — is the
  caller. The current `module.rbac_search` block only grants `know_mcp`;
  add `crm_agent`, `prod_agent`, `orch_agent`, and (per §6.0)
  `fraud_workflow` to the `principal_ids` map.
- The existing `know_mcp` user-assigned identity becomes **redundant
  for retrieval**. Keep it if Option B (hybrid) is chosen; remove it
  with Option A only after every consumer is cut over.

### 5.4 RBAC for SharePoint sources (deferred)

If/when a SharePoint knowledge source is added, the agent must inject
`x-ms-query-source-authorization` per request. Foundry Agent Service
**does not yet support per-request MCP headers** (preview limitation
called out in the doc); workaround is the Azure OpenAI Responses API.
Not blocking for initial migration since we only have blob sources.

### 5.5 Key Vault + k8s-secrets + config-sync

The configuration touches **four** files, not one. Missing any of
them leaves pods without the new contract:

1. [`main.tf` `module "keyvault_secrets"`](../infra/terraform/main.tf)
   — the canonical source.
   - **Remove:** `Search--Endpoint`, `Search--IndexName`, `Search--ApiKey`
     (if present).
   - **Add:** `KnowledgeBase--SearchEndpoint`, `KnowledgeBase--Name`,
     `KnowledgeBase--ProjectConnectionName`.
2. [`infra/terraform/k8s-secrets.tf`](../infra/terraform/k8s-secrets.tf)
   currently bootstraps `Search--Endpoint` and `Search--IndexName`
   (lines 39–40). Add the new keys; remove the old ones with Option A.
3. [`src/config-sync/Program.cs`](../src/config-sync/Program.cs) maps
   secrets to per-service `appsettings`:
   - Lines 107–110 — the `["knowledge-mcp"]` block reads
     `Search--Endpoint` → `Search:Endpoint` and `Search--IndexName` →
     `Search:IndexName`. Delete the whole block under Option A; rewrite
     for `KnowledgeBase:*` under Option B.
   - Lines 122 and 130 — `KnowledgeMcp--BaseUrl` is mapped for
     `crm-agent` and `product-agent`. With Option A, replace with
     `KnowledgeBase--SearchEndpoint`, `KnowledgeBase--Name`,
     `KnowledgeBase--ProjectConnectionName`. Also add a mapping for
     **`fraud-workflow`** (currently uses `KnowledgeMcp:BaseUrl` via
     the same pattern — see §6.0).
4. [`src/config-sync/README.md`](../src/config-sync/README.md) — update
     the per-service secret table (lines 45–47).
5. Each affected Helm chart's `values.yaml` (`crm-agent`, `product-agent`,
   `orchestrator-agent`, `fraud-workflow`, and `knowledge-mcp` itself if
   kept) — update `secretRefs` and any non-secret config keys.

Naming follows the `PascalCase--Hierarchy → Section:Key` standard in
[docs/config-naming-standard.md](config-naming-standard.md).

### 5.6 Networking

The Foundry IQ MCP endpoint lives on the existing AI Search service
URL, so the existing `pe_search` private endpoint
([main.tf line 787](../infra/terraform/main.tf#L787)) already covers
it. **No new private endpoint, private DNS zone, or vnet rule.**

## 6. Application changes

### 6.0 Affected consumers (corrected after review)

The initial draft listed only `crm-agent`, `product-agent`, and
`orchestrator-agent`. The adversarial review surfaced **two more
consumers** that must be in scope:

- **`src/fraud-workflow/`** —
  [Program.cs lines 49–55, 81–83](../src/fraud-workflow/Program.cs)
  register `knowledge-mcp` as a named HttpClient + provider +
  ready-tagged health check, and
  [`Agents/ReturnConditionAgent.cs`](../src/fraud-workflow/Agents/ReturnConditionAgent.cs)
  explicitly calls `search_knowledge_base`. **Must migrate or break
  refund-risk fan-out.**
- **`src/AppHost/Program.cs`** lines 30/36/41/55 wire
  `Projects.Contoso_KnowledgeMcp` and `.WithReference(knowledgeMcp)`
  into the orchestrator, product-agent, and fraud-workflow. Cleanup
  is required for Aspire startup to succeed under Option A.
- **`src/bff-api/`** — verify; if it surfaces knowledge tooling at the
  HTTP edge it also needs config-sync changes.

### 6.0.1 CI/CD references

- [`.github/workflows/deploy-all-services.yml`](../.github/workflows/deploy-all-services.yml)
  lists `knowledge-mcp` at lines 106, 153, 236, 693.
- [`.github/workflows/deploy-knowledge-mcp.yml`](../.github/workflows/deploy-knowledge-mcp.yml)
  is the dedicated build/deploy pipeline.

Option A requires deleting `deploy-knowledge-mcp.yml` and pruning the
service from `deploy-all-services.yml`. Option B leaves both in place.

### 6.1 Option A — delete `src/knowledge-mcp`

The hosted MCP endpoint fully replaces:

- [`AzureSearchService.cs`](../src/knowledge-mcp/Services/AzureSearchService.cs) — gone
- [`InMemorySearchService.cs`](../src/knowledge-mcp/Services/InMemorySearchService.cs) — gone unless Option B is chosen
- [`KnowledgeTools.cs`](../src/knowledge-mcp/Tools/KnowledgeTools.cs) — gone
- [`SearchServiceWarmupHostedService.cs`](../src/knowledge-mcp/Services/SearchServiceWarmupHostedService.cs) — gone (AI Search has no per-process warm-up)
- [`HealthChecks/`](../src/knowledge-mcp/HealthChecks/) — gone
- Helm chart, Dockerfile, service account, workload-identity federation — gone

In each agent (`crm-agent`, `product-agent`, `orchestrator-agent`),
register the Foundry-IQ MCP tool inline. Per the
[component-independence edict](../.github/copilot-instructions.md),
inlining is correct — no shared helper project.

Sketch (subject to .NET-SDK gap noted in §6.3):

```csharp
var mcpTool = McpTool.Create(
    serverLabel: "knowledge-base",
    serverUrl:   $"{searchEndpoint}/knowledgebases/{kbName}/mcp?api-version=2026-05-01-preview",
    requireApproval: false,
    allowedTools: ["knowledge_base_retrieve"],
    projectConnectionId: "contoso-kb-mcp");
agentBuilder.Tools.Add(mcpTool);
```

### 6.2 Option B — keep `knowledge-mcp` as a thin proxy (hybrid)

Swap `AzureSearchService` for a `FoundryIqSearchService` that POSTs to
the agentic-retrieval endpoint
(`POST /knowledgebases/{kb}/retrieve?api-version=2026-04-01`).

Pros: preserves hand-tuned tool description / topK clamp behavior in
[`KnowledgeTools.cs`](../src/knowledge-mcp/Tools/KnowledgeTools.cs)
(lines 19–25), keeps uniform observability via existing health checks,
keeps `InMemorySearchService` viable for local-dev.

Cons: keeps the AKS deployment and identity; the in-process MCP tool
wraps a remote MCP tool — debatable value.

### 6.3 .NET SDK gap (important)

The official Foundry-IQ MCP samples are **Python-only** (`azure-ai-projects >= 2.0.0`).
For .NET 9 + `Microsoft.Agents.AI`:

1. Create the project connection via Terraform/CI (REST), **not** from
   the .NET app.
2. Register the MCP tool via the generic `ModelContextProtocol` C# SDK
   pointed at the search-service MCP URL with a bearer token from
   `DefaultAzureCredential` for `https://search.azure.com/.default`.
3. If the C# SDK doesn't yet support `project_connection_id`-style
   delegation, fall back to direct REST POST against the
   `retrieve` endpoint (Option B internals).

**Validation gate:** before merging migration code, confirm the C#
agent framework + MCP SDK actually pair correctly with the Foundry IQ
MCP endpoint end-to-end. If it doesn't, **Option B becomes mandatory**
until the .NET SDK catches up.

## 7. Local-dev impact

Today's `DataMode=InMemory` flag (set by
[`infra/setup-local.{ps1,sh}`](../infra/setup-local.ps1)) lets devs
run the whole stack with zero Azure infra beyond Foundry embeddings,
via [`InMemorySearchService`](../src/knowledge-mcp/Services/InMemorySearchService.cs).

Foundry IQ has **no local emulator** — same constraint already
documented for AI Search itself. Three sub-options:

1. **Require a real Foundry IQ KB in dev tenants.** Matches the
   current full-Azure track; breaks the 5-min local track in
   [docs/labs/local/](labs/local/).
2. **Keep `knowledge-mcp` only for `DataMode=InMemory`** (Option B for
   local, Option A for cloud). Local lab keeps working, cloud track
   sheds the AKS deployment.
3. **Build a fake MCP server** that mimics `knowledge_base_retrieve`.
   Net-new code; probably not worth it.

**Recommendation:** sub-option 2.

## 8. Tests to update (expanded after review)

| File | Lines | Change |
|---|---|---|
| [`src-tests/Contoso.AppHost.Tests/FitnessTests.cs`](../src-tests/Contoso.AppHost.Tests/FitnessTests.cs) | 54, 88–90 | Remove `"knowledge-mcp"` from `ChartedServices`; rewrite the `Search__IndexName` assertion to validate `KnowledgeBase__Name` against `var.kb_name`. |
| [`src-tests/Contoso.AppHost.Tests/ProjectRegistrationTests.cs`](../src-tests/Contoso.AppHost.Tests/ProjectRegistrationTests.cs) | 18 | Remove `"knowledge-mcp"` from the expected-projects list under Option A. |
| [`src-tests/Contoso.AppHost.Tests/LocalDevTemplateTests.cs`](../src-tests/Contoso.AppHost.Tests/LocalDevTemplateTests.cs) | 57 | Remove `"knowledge-mcp"` from the template-coverage list (Option A) or update template (Option B/hybrid). |
| [`src-tests/Contoso.AppHost.Tests/ComponentIndependenceTests.cs`](../src-tests/Contoso.AppHost.Tests/ComponentIndependenceTests.cs) | — | Update project list if knowledge-mcp is deleted. |
| [`src-tests/Contoso.KnowledgeMcp.Tests/`](../src-tests/Contoso.KnowledgeMcp.Tests/) | — | Delete with Option A; rewrite around `FoundryIqSearchService` for Option B. |
| [`src-tests/Contoso.FraudWorkflow.Tests/`](../src-tests/Contoso.FraudWorkflow.Tests/) | — | Replace `KnowledgeMcpHealthCheck` / `KnowledgeMcpClientProvider` mocks with the new MCP-tool registration pattern. |
| Per-agent test suites (`Contoso.CrmAgent.Tests`, `Contoso.ProductAgent.Tests`, `Contoso.OrchestratorAgent.Tests`) | — | Add MCP-tool-registration assertions per agent (mitigates the inline-drift risk from §13 R7). |
| **New** | — | Fitness test that pins KB ↔ knowledge-source name mapping (knowledge-base must reference the source by name, never by `*-index` generated name). Prevents the trap called out in §5.1. |

## 9. Cost & operational impact (corrected after review)

**Cost change is NOT zero.** The initial draft claimed near-parity
because it assumed planner-LLM tokens replaced "per-query embedding
cost." That was wrong on two counts:

1. Today's [`AzureSearchService.cs`](../src/knowledge-mcp/Services/AzureSearchService.cs#L39-L48)
   issues a **single semantic-search call** with the raw query string.
   Vector embeddings are computed at indexing time (in the Knowledge
   Source skillset), not per query — so there is no per-query embedding
   cost to displace.
2. Per the [Azure AI Search agentic-retrieval billing docs](https://learn.microsoft.com/en-us/azure/search/search-agentic-retrieval-concept#billing),
   agentic retrieval bills:
   - **Azure AI Search retrieval tokens** for each sub-query that is
     planned, executed, and reranked.
   - **Azure OpenAI input + output tokens** for the planner LLM call(s)
     and any synthesis the KB performs.

**Revised cost framing:**

- Per-query cost goes UP (planner LLM + multi-sub-query retrieval
  vs. single semantic call). Magnitude depends on the
  `retrievalReasoningEffort` setting (`minimal` / `low` / `medium`) and
  the average sub-query fan-out.
- AI Search SKU is unchanged (Standard, semantic GA).
- **Action before approval:** model expected per-query cost using
  representative production traffic (estimated daily queries × average
  sub-queries × planner-model token rate). Update the budget section
  of this plan with concrete numbers.
- Free-tier allocations for POC testing are real but irrelevant to the
  production cost analysis.

**Operational delta:** unchanged from prior draft — Option A
**removes** one AKS deployment, Helm chart, Dockerfile, identity,
federation, ready probe, warmup hosted service (~700 LOC + one Helm
release). **Adds** a managed endpoint with no per-process state to
operate. Loss of readiness signal is a real regression (see §13 R8).

## 10. Documentation & architecture-diagram updates

This is a sweep across **every** doc surface that mentions the
soon-to-be-defunct `knowledge-mcp` service, the `search_knowledge_base`
tool name, the `KnowledgeMcp:BaseUrl` config key, or the old "Knowledge
(RAG) MCP" box in diagrams. Missing any of them leaves the public
narrative out of sync with the running system and breaks the labs.

### 10.1 Architecture diagrams (Option A only \u2014 Option B leaves the box)

| File | Change |
|---|---|
| [`docs/architecture.drawio`](architecture.drawio) | (a) Rename / replace cell `mcp-knowledge` (line ~107, label "Knowledge (RAG)&#xa;MCP") with a Foundry IQ knowledge-base node placed in the Foundry / AI Search swim-lane (it is NOT an AKS pod anymore). (b) Update edges `e-fraud-knowmcp` (line ~97) and `85` (line ~255) so the four consumer agents (`crm-agent`, `product-agent`, `orchestrator-agent`, `fraud-workflow`) point to the new node via the **MCP-over-HTTPS** label rather than the in-cluster ClusterIP arrow. (c) Drop the `Knowledge MCP` row from the AKS swim-lane label list. |
| `docs/architecture.png` | **Re-export from the updated .drawio.** Owner must open `architecture.drawio` in [diagrams.net](https://app.diagrams.net) (or the VS Code extension) and File \u2192 Export As \u2192 PNG over the same canvas region. Commit the regenerated PNG in the same PR. README and labs embed this PNG by relative path. |
| [`infra/k8s/manifests/network-policies/README.md`](../infra/k8s/manifests/network-policies/README.md) | Lines 21, 24, 39\u201340, 42 \u2014 the ASCII traffic diagram and the policy table both call out `knowledge-mcp`. Remove the row + the egress entries from `crm-agent.yaml` / `product-agent.yaml` policy descriptions (and delete `knowledge-mcp.yaml` policy itself under Option A). |

### 10.2 Top-level + ops docs

| File | Lines | Change |
|---|---|---|
| [`README.md`](../README.md) | 19, 52, 96, 214 | Component table, tool table, AKS service table, and the `src/` tree listing all reference "Knowledge MCP" / `knowledge-mcp/` / `id-know-mcp` / `search_knowledge_base`. Reframe as "Foundry IQ knowledge base" with the new tool name `knowledge_base_retrieve`. Remove the `id-know-mcp` row (Option A) or restate its purpose (Option B). |
| [`docs/business-scenario.md`](business-scenario.md) | 188 | Tool catalog row \u2014 replace `search_knowledge_base(query, top_k?)` with `knowledge_base_retrieve(messages, dataSource?)` and update the signature description to match the Foundry IQ tool schema. |
| [`docs/e2e-verification.md`](e2e-verification.md) | 125, 147, 230, 249, 282, 300, 332, 351, 431, 451 | Five customer scenarios each list "Expected MCP Tools" and a "Knowledge MCP logs show" check. Rewrite tool name (`search_knowledge_base` \u2192 `knowledge_base_retrieve`) and replace the pod-log assertion with an Application Insights / Search service log assertion (the new endpoint runs in the search service, not in AKS). |
| [`docs/foundry-only-deployment.md`](foundry-only-deployment.md) | 409, 449\u2013492, 606 | Two tables (component-by-component local-vs-cloud, port map) and a full subsection on "Knowledge MCP" \u2014 needs Option A vs Option B fork. Under Option A delete the subsection; under Option B retain it but flag the cloud path as a thin proxy. |
| [`docs/implementation-plan.md`](implementation-plan.md) | 128, 135, 138, 188, 641\u2013734, 805, 843, 908, 943 | Heaviest single file \u2014 contains the full "Knowledge MCP" build spec, Dockerfile checklist, Helm chart checklist, env-var lists. Under Option A this whole subtree is deleted and replaced with a one-paragraph "Foundry IQ Knowledge Base (managed)" callout linking to this plan. Under Option B/hybrid the spec stays but every `KnowledgeMcp:BaseUrl` env-var bullet must become `KnowledgeBase:SearchEndpoint`, `KnowledgeBase:Name`, `KnowledgeBase:ProjectConnectionName`. |
| [`docs/local-development.md`](local-development.md) | 75, 76, 173, 189 | (a) Drop `knowledge-mcp` from the agents-getting-Foundry-secrets bullet, (b) drop the `DataMode = InMemory` bullet under Option A or retain under Option B (hybrid case in \u00a77), (c) remove row 3 from the local-port table, (d) remove the `Knowledge MCP : 5003 : search_knowledge_base` row. |
| [`docs/security.md`](security.md) | 648, 649, 651, 769; plus known-gaps section | Two changes: (1) Update the egress matrix \u2014 `crm-agent`, `product-agent`, `knowledge-mcp` rows must drop `knowledge-mcp:8080` and add the AI Search private endpoint (already there for other services); (2) replace the `knowledge-mcp \u2192 Search--Endpoint` config-sync row (line 769) with the new `KnowledgeBase--*` mappings for the four consumers; (3) extend the existing "knowledge-source admin-key" accepted-risk entry to cover `knowledgebases` PUT (or remove if \u00a712 q2 confirms RBAC is supported). |

### 10.3 Lab guides

| File | Lines | Change |
|---|---|---|
| [`docs/labs/local/lab-1.md`](labs/local/lab-1.md) | 213 | Drop the `5003 knowledge-mcp/` row from the ports table; reframe in the narrative as "Foundry IQ exposes the knowledge base over MCP \u2014 no local pod under Option A." |
| [`docs/labs/local/lab-2.md`](labs/local/lab-2.md) | 44, 87, 184, 198, 215, 297, 312, 403, 461, 572 | Lab-2 is built around runtime tool discovery using `crm-mcp + knowledge-mcp` as the canonical example. Either (a) keep the lab as written under Option B (hybrid \u2014 local pod still exists), or (b) under Option A, rewrite the trace tree, the count of "9 services green", the `WithReference(knowledgeMcp)` AppHost snippet, the `KnowledgeMcp: BaseUrl` settings snippet, the `Services/Mcp/KnowledgeMcpClientProvider.cs` reference, and the tool-name expectation (`search_knowledge_base` \u2192 `knowledge_base_retrieve`). |
| [`docs/labs/full-azure/lab-1.md`](labs/full-azure/lab-1.md) | 158, 308 | Drop `knowledge-mcp/appsettings.Development.json` from the config-sync output preview; remove `deploy-knowledge-mcp.yml` from the per-service workflow list. |
| [`docs/labs/full-azure/lab-2.md`](labs/full-azure/lab-2.md) | 10, 44, 71, 86, 164, 374 | Same shape as lab-2 local \u2014 update the canonical-example narrative, the diagram, the Container Insights KQL snippet (drop `"knowledge-mcp"` from the `ContainerName in (...)` list), the `KnowledgeMcpClientProvider.cs` reference, and the network-policy guidance for the "add a new agent" extension exercise. |
| [`docs/labs/full-azure/lab-3.md`](labs/full-azure/lab-3.md) | 230 | The DTS-agent extension exercise mentions adding the same `crm-mcp + knowledge-mcp` egress \u2014 update to reflect AI Search private-endpoint egress only. |

### 10.4 Per-component READMEs (Option A: delete `src/knowledge-mcp/README.md`)

| File | Change |
|---|---|
| [`src/README.md`](../src/README.md) lines 50, 56, 57, 59 | Delete the `knowledge-mcp/` row from the MCP-servers table; update the `crm-agent / product-agent / fraud-workflow` rows to list "Foundry IQ knowledge base (via MCP)" instead of "Knowledge MCP". |
| [`src/knowledge-mcp/README.md`](../src/knowledge-mcp/README.md) | Delete with Option A; rewrite around the Foundry IQ-backed `FoundryIqSearchService` for Option B. |
| [`src/product-agent/README.md`](../src/product-agent/README.md) lines 2, 14, 37 | Subtitle + config-key table + "what it does" paragraph all mention `KnowledgeMcp:BaseUrl` and "knowledge-mcp (semantic search)". Replace with `KnowledgeBase:*` keys. |
| [`src/crm-agent/README.md`](../src/crm-agent/README.md) | Same shape as product-agent \u2014 verify and update any `KnowledgeMcp:*` references. |
| [`src/orchestrator-agent/README.md`](../src/orchestrator-agent/README.md) | Verify and update any `KnowledgeMcp:*` references; orchestrator only delegates, so impact is narrow. |
| [`src/fraud-workflow/README.md`](../src/fraud-workflow/README.md) lines 30, 60 | Architecture diagram (`ReturnConditionAgentExecutor (LLM + crm-mcp + knowledge-mcp)`) and config-key table (`KnowledgeMcp:BaseUrl`). |
| [`src/config-sync/README.md`](../src/config-sync/README.md) lines 45\u201347 | Per-service secret-count + secret-list table \u2014 drop the `knowledge-mcp` row, swap `KnowledgeMcp--BaseUrl` for the new `KnowledgeBase--*` triple on the `crm-agent` and `product-agent` rows, and add a `fraud-workflow` row for the same. |
| [`infra/templates/README.md`](../infra/templates/README.md) line 12 | Drop the `knowledge-mcp` template-target row. |

### 10.5 Agent system prompts (behaviour-critical, NOT just docs)

These ship with the agents and influence model behaviour at runtime,
so they MUST be updated in lock-step with the tool-name change. Today's
prompts hand-tune retry behaviour around the **old** tool name.

| File | Lines | Change |
|---|---|---|
| [`src/crm-agent/Prompts/system-prompt.md`](../src/crm-agent/Prompts/system-prompt.md) | 17 | Replace the `search_knowledge_base` references with `knowledge_base_retrieve`, AND re-validate the "don't re-call with a rephrased query, raise topK to 10 instead" guidance against the Foundry IQ tool schema (which exposes different knobs \u2014 `retrievalReasoningEffort`, `subQueryLimit`). Keep the eval set in \u00a76.1 honest by A/B-testing the rewritten prompt. |
| [`src/product-agent/Prompts/system-prompt.md`](../src/product-agent/Prompts/system-prompt.md) | 15 | Same change as crm-agent. |

### 10.6 Doc-coverage gate

Add a CI grep step (or extend an existing fitness test) that fails the
build if any of these strings remain in the repo after Option A
completes: `search_knowledge_base`, `KnowledgeMcp:BaseUrl`,
`KnowledgeMcp--BaseUrl`, `Projects.Contoso_KnowledgeMcp`,
`knowledge-mcp/chart`. Required because doc drift is silent \u2014 there
is no compiler that catches stale `KnowledgeMcp:BaseUrl` in markdown.

## 11. Migration phasing (tightened after review)

| Phase | Scope | Reversible? |
|---|---|---|
| 0 | Adversarial review of this plan ✅ DONE 2026-06-11; decision on Option A vs hybrid. | n/a |
| 0.5 | **.NET SDK spike** — half-day validation that `Microsoft.Agents.AI` + the C# MCP SDK can talk to the Foundry IQ MCP endpoint via either the project-connection pattern or direct REST. Outcome gates Option A vs forced-Option-B. Also verifies whether the `knowledgebases` REST surface supports RBAC (§5.1). | n/a |
| 1 | Add `knowledge-base` module + project connection + RBAC delta + Foundry `project_principal_id` output. **Run side-by-side with existing `knowledge-mcp`** in a single dev environment. **Guardrail:** both services share the same AI Search instance and Knowledge Source — monitor query-unit consumption and planner token spend during the overlap to avoid surprise bills (cumulative load on a Standard tier can throttle). | Yes — `terraform destroy module.knowledge_base` |
| 2 | End-to-end validation: stand up a sandbox agent that targets the new MCP endpoint; compare retrieval quality vs current implementation on the existing eval set. Verify readiness-probe replacement (§13 R8). | Yes |
| 3 | Cut over one agent at a time. Order: `product-agent` (smallest blast radius) → `crm-agent` → `orchestrator-agent` → `fraud-workflow` (largest, since it fans out to all three knowledge calls in a single refund). | Yes via revert |
| 4 | After all four consumers are green, delete `src/knowledge-mcp` (or downgrade to InMemory-only per §7), prune `deploy-knowledge-mcp.yml`, prune the service from `deploy-all-services.yml`, and remove redundant identity/RBAC. | Reverting requires re-deploying the deleted chart and re-adding the workflow — friction increases. |
| 5a | **Docs/diagram prep PR** (option-neutral) — introduces Foundry IQ wording in places where both old and new can coexist (e.g. "Knowledge access (Foundry IQ in Option A; Knowledge MCP in Option B)"), wires up the doc-coverage CI gate (§10.6) initially in **warn-only mode**, and scripts `docs/architecture.png` regeneration (§10.1). Decouples doc review from code/infra review. | Yes |
| 5b | **Cleanup PR** (lands together with phase 4 code/infra deletion) — final option-A-only edits: delete `src/knowledge-mcp/README.md`, remove all remaining stale strings from §10.2–10.5, regenerate `architecture.png`, update both agent system prompts (and re-run the eval set per §10.5), promote the doc-coverage gate from warn-only to fail-the-build, and remove `Search--*` / `KnowledgeMcp--BaseUrl` from `keyvault_secrets`. Must ship in the same release window as phase 4 so runtime and docs flip together. | Reverting still needs a docs-revert PR plus the code-revert PR, but the surface area of each PR is reviewable in isolation. |

## 12. Open questions

1. **.NET SDK readiness.** Does `Microsoft.Agents.AI` + `ModelContextProtocol` C# SDK fully support the project-connection-based MCP authentication pattern? If not, Option B is the only viable choice until it does. **Resolved by phase 0.5 spike.**
2. **Knowledge-base data-plane auth.** Does PUT `/knowledgebases/{name}?api-version=2026-05-01-preview` accept Entra (RBAC) auth, or is admin-key still required like the `knowledgesources` endpoint? GPT-5.5 reviewer claimed RBAC is supported; the public docs are ambiguous. **Resolved by phase 0.5 spike.** Outcome decides whether \S5.1 stays admin-key or switches to MSI + `Search Service Contributor`.
3. **Knowledge-base versioning.** Knowledge Sources are immutable in some fields (embedding model) — what's the upgrade story for the knowledge base itself? Same delete-and-recreate dance from the existing `knowledge-source` module?
4. **Local lab impact.** Are workshop attendees expected to provision real Foundry IQ KBs, or do we maintain the in-memory shim? Decision affects how much of `knowledge-mcp` we keep.
5. **Caller-identity propagation.** Today agents read with their app identity. Foundry IQ's "run queries under the caller's Entra identity" requires user OBO tokens to reach the agent. Out of scope for blob sources; mandatory if SharePoint is ever added.
6. **Multi-region / data residency.** Foundry IQ adds the planner LLM to the read path; deployment must be in a region that has the planner model deployed.
7. **Concrete cost numbers.** Replace the qualitative \S9 with a per-query token estimate based on actual production traffic before approving phase 1.

## 13. Risk register (expanded after review)

| # | Risk | Mitigation |
|---|---|---|
| R1 | Preview-only API (`2026-05-01-preview`) for the MCP endpoint changes shape before GA. | Pin api-version; subscribe to Microsoft Foundry changelog; revisit at each preview rev. |
| R2 | .NET SDK doesn't yet support the project-connection pattern (§6.3). | Validation gate in phase 0.5; fall back to Option B if blocked. |
| R3 | Per-request MCP headers unsupported in Foundry Agent Service preview. | Only matters when SharePoint source is added; defer until then. Even for blob-only, document the caller-identity model so future SharePoint onboarding isn't surprised. |
| R4 | Admin-key provisioning for `knowledge-base` PUT — **if** the new endpoint also lacks RBAC. Verify in phase 0.5. | Reuse the accepted-risk entry in [docs/security.md](security.md) and extend it; prefer RBAC if available. |
| R5 | Local-dev divergence if `InMemorySearchService` is removed. | Sub-option 2 in §7 (hybrid). |
| R6 | Loss of hand-tuned tool docstring in [`KnowledgeTools.cs`](../src/knowledge-mcp/Tools/KnowledgeTools.cs) affects model behavior. | Encode equivalent guidance in agent system prompts; A/B against the existing eval set in phase 2. |
| R7 | Component-independence edict tempts a shared "FoundryIq" helper across the four consumers. | Inline registration code per consumer; per-agent tests assert identical critical MCP settings (see §8). |
| R8 | **Readiness regression.** Today every consumer has a `KnowledgeMcpHealthCheck` tagged `ready` (e.g. [crm-agent](../src/crm-agent/HealthChecks/), [fraud-workflow](../src/fraud-workflow/HealthChecks/)). Deleting `knowledge-mcp` without a replacement turns knowledge-base outages into chat-time errors instead of pod-not-ready. | Add a per-consumer readiness probe that issues a cheap `knowledge_base_retrieve` against the new MCP endpoint, OR an Azure Monitor synthetic probe with alerting. |
| R9 | **Cost surprise** (corrected §9). Per-query cost rises; magnitude depends on planner reasoning effort. | Quantify before approval; set `retrievalReasoningEffort=minimal` as the initial value; add a token-spend dashboard. |
| R10 | KB references the wrong Knowledge Source name (uses generated `*-index` instead of the source name). | Fitness test in §8 pins the mapping. |
| R11 | **CI/CD drift.** `deploy-all-services.yml` and `deploy-knowledge-mcp.yml` will still try to build/deploy the deleted service. | Phase 4 of §11 explicitly prunes both files. |
| R12 | **AppHost startup break.** `src/AppHost/Program.cs` references `Projects.Contoso_KnowledgeMcp` at four call sites. | Phase 4 prunes; gated by Aspire smoke test. |
| R13 | **Shared-search load during side-by-side.** Phase 1 doubles read traffic against one AI Search Standard instance. | Monitor QUs; consider a separate KB-only search service for the overlap window if traffic is heavy. |
| R14 | **Doc drift.** §10 spans 25+ files including the architecture diagram, both agent system prompts, ten lab pages, [infra/README.md](../infra/README.md), and the security narrative. The compiler catches none of it; stale labs will look like broken behaviour to workshop attendees, and stale security tables will mislead reviewers about which identities exist. | Mandatory doc-coverage grep gate (§10.6) with explicit allow-list for the plan itself + `.squad/**`; split docs across two PRs per phase 5a/5b in §11; spot-check `docs/architecture.png` rendering in the PR preview. |
| R15 | **System-prompt regression.** Today's prompts hand-tune retry behaviour for `search_knowledge_base` and pin `topK ≤ 10` as a manual fan-out cap. A blind find-replace to `knowledge_base_retrieve` will leave guidance that *suppresses* Foundry IQ's value (the planner already expands sub-queries; the "first off-topic result → give up" rule will short-circuit recovery). | Treat prompt updates as code changes; rewrite guidance around Foundry IQ knobs (`retrievalReasoningEffort`, `subQueryLimit`, citation handling); re-run the agent eval set (§6.1) before each phase-3 cut-over and again at phase 5b. |
| R16 | **Architecture-PNG regeneration is manual.** [docs/architecture.png](architecture.png) is the only image embedded by README. Today's plan says "open in diagrams.net and re-export" — not reproducible, no diff in CI. | Script the export per §10.1 (`drawio` CLI), check the script into `infra/templates/scripts/`, and optionally add a text Mermaid sibling so reviewers can diff the architecture without trusting a binary. |

## 14. Decision checkpoints before implementation

1. Approve Option A (clean cut) vs hybrid (Option A cloud + Option B local).
2. Validate .NET SDK pairing in a throwaway spike (~half-day).
3. Confirm planner-model deployment region availability for our target environments.
4. Two-reviewer adversarial debate per
   [`.github/copilot-instructions.md`](../.github/copilot-instructions.md)
   on the implementation PR — not just on this plan.
