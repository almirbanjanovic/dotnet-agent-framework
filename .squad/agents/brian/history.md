# Project Context

- **Owner:** Almir Banjanovic
- **Project:** .NET Agent Framework — 8-container agentic AI system with Contoso Outdoors (Blazor WASM UI, BFF API, CRM API, CRM MCP, Knowledge MCP, CRM Agent, Product Agent, Orchestrator Agent)
- **Stack:** .NET 9, Minimal APIs, Blazor WebAssembly, MudBlazor, ModelContextProtocol C# SDK, Microsoft.Agents.AI, Azure.AI.OpenAI, Cosmos DB, Azure AI Search, Terraform, AKS, Helm, Docker
- **Created:** 2026-03-19

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

### 2026-03-19 — Full Backend Analysis (Read-Only)

**Current state**: Lab 1 complete. Infrastructure fully provisioned via Terraform. No backend application code exists yet — only 3 utility projects.

**Existing projects (src/)**:
- `src/simple-agent/` — Azure OpenAI validation console app (Azure.AI.OpenAI 2.1.0, Microsoft.Agents.AI 1.0.0-rc2)
- `src/config-sync/` — Key Vault → `src/appsettings.json` bridge (Azure.Security.KeyVault.Secrets 4.7.0)
- `src/seed-data/` — CSV → Cosmos DB seeder (Microsoft.Azure.Cosmos 3.46.1, CrmSeeder.cs)

**Planned projects (not yet created)**:
- `src/crm-api/` — .NET Minimal API, 11 endpoints (customers, orders, products, promotions, support tickets)
- `src/crm-mcp/` — MCP Server, 10 tools wrapping CRM API endpoints
- `src/knowledge-mcp/` — MCP Server, 1 tool: search_knowledge_base (Azure AI Search)
- `src/crm-agent/` — CRM specialist agent (Microsoft.Agents.AI + Azure.AI.OpenAI)
- `src/product-agent/` — Product specialist agent
- `src/orchestrator-agent/` — Intent classification + routing
- `src/bff-api/` — JWT validation, CRM proxy, image proxy, chat, Cosmos DB conversation persistence
- `src/blazor-ui/` — Blazor WASM SPA (Lois's domain)

**Cosmos DB containers (CRM)**:
- `Customers` (PK: /id) — 10 customers, loyalty tiers Bronze/Silver/Gold/Platinum
- `Orders` (PK: /customer_id) — 12 orders, statuses: shipped/delivered/processing/returned
- `OrderItems` (PK: /order_id) — 19 line items
- `Products` (PK: /id) — 15 products across footwear/clothing/tents/backpacks/accessories/cooking
- `Promotions` (PK: /id) — 5 active promotions with tier eligibility
- `SupportTickets` (PK: /customer_id) — 5 tickets (4 open, 1 closed)

**Cosmos DB containers (Agents)**:
- `conversations` (PK: /sessionId) — conversation persistence, BFF sole writer/reader

**Knowledge base**: 12 PDFs in AI Search (5 guides, 4 policies, 3 procedures) — auto-vectorized via Knowledge Source

**Key Vault secrets**: 18 mapped (OpenAI, Cosmos×2, Storage, Search, Entra)

**Config pattern**: appsettings.json (gitignored) + env vars, ConfigurationBuilder priority order

**Auth**: 3-layer — User JWT (Entra/MSAL/PKCE), Managed Identity (DefaultAzureCredential), Agent Identity (Entra Agent ID)

**Infrastructure**: Terraform 20 modules + RBAC modules, deployed via deploy.ps1 (7 phases) or GitHub Actions

**Endpoint inventory (planned for CRM API — 11 endpoints)**:
1. GET /customers — all customers
2. GET /customers/{id} — customer detail
3. GET /customers/{id}/orders — customer orders
4. GET /orders/{id} — order detail (with line items JOIN)
5. GET /products — search/browse products (query, category, in_stock_only)
6. GET /products/{id} — product detail
7. GET /promotions — all active promotions
8. GET /promotions/eligible/{customerId} — tier-filtered promotions
9. GET /customers/{id}/support-tickets — customer tickets (open_only filter)
10. POST /support-tickets — create support ticket
11. (possible 11th: GET /orders/{id}/items or similar)

**MCP tools (planned for CRM MCP — 10 tools)**:
get_all_customers, get_customer_detail, get_customer_orders, get_order_detail, get_products, get_product_detail, get_promotions, get_eligible_promotions, get_support_tickets, create_support_ticket

**Data files**:
- `data/contoso-crm/` — 6 CSV files
- `data/contoso-sharepoint/` — 12 PDFs + 12 TXT source files (guides/, policies/, procedures/)
- `data/contoso-images/` — 15 PNG product images

### 2026-03-19 — Cross-Team Finding: Full Codebase Analysis Complete

**Team Update (from all 5 agents):** Architecture is fully specced and infrastructure is provisioned, but **zero application code exists yet.** This is the intended state at end of Phase 1 (infrastructure/tooling complete). All 5 agents confirm the critical path: CRM API is the foundation — all downstream components depend on it. No fundamental re-design needed. All decisions merged into `.squad/decisions.md` with consensus on next steps.
