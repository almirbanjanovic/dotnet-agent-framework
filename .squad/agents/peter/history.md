# Project Context

- **Owner:** Almir Banjanovic
- **Project:** .NET Agent Framework — 8-container agentic AI system with Contoso Outdoors (Blazor WASM UI, BFF API, CRM API, CRM MCP, Knowledge MCP, CRM Agent, Product Agent, Orchestrator Agent)
- **Stack:** .NET 9, Minimal APIs, Blazor WebAssembly, MudBlazor, ModelContextProtocol C# SDK, Microsoft.Agents.AI, Azure.AI.OpenAI, Cosmos DB, Azure AI Search, Terraform, AKS, Helm, Docker
- **Created:** 2026-03-19

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

### 2026-03-19 — Initial Test Analysis

- **Test projects: 0.** The solution has 3 utility projects (simple-agent, seed-data, config-sync) and zero test projects. None of the 7 planned test projects (crm-api.tests, crm-mcp.tests, knowledge-mcp.tests, crm-agent.tests, product-agent.tests, orchestrator-agent.tests, bff-api.tests) exist yet because their corresponding application components haven't been built.
- **Test frameworks: not referenced.** xUnit, FluentAssertions, NSubstitute, WebApplicationFactory, and bUnit are listed in the README but not yet in any .csproj.
- **`dotnet test` result:** Build succeeds (exit 0), zero tests discovered, zero tests run.
- **Only testable logic today:** `CrmSeeder.cs` in seed-data — CSV parsing (`ParseCsv`, `ParseCsvLine`), type conversion (`ConvertValue`), Cosmos DB seeding (`SeedAsync`), Entra ID linking (`LinkEntraIdsAsync`). ~218 lines with multiple edge-case-rich code paths and zero test coverage.
- **Coverage: 0%.** When components land, CRM API and BFF API should be first priorities (WebApplicationFactory integration tests). Orchestrator routing logic is the next critical path.
- **Full analysis written to:** `.squad/decisions/inbox/peter-test-analysis.md`

### 2026-03-19 — Cross-Team Finding: Full Codebase Analysis Complete

**Team Update (from all 5 agents):** Architecture is fully specced and infrastructure is provisioned, but **zero application code exists yet.** This is the intended state at end of Phase 1 (infrastructure/tooling complete). All 5 agents confirm: tests must land with code (never after). CrmSeeder should be tested immediately (guards critical data path). When components are built, test priority: CRM API → BFF API → Orchestrator Agent. No fundamental re-design needed. All decisions merged into `.squad/decisions.md` with full consensus.
