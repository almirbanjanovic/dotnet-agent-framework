# Contoso Outdoors — Component-by-Component Implementation Plan

## Overview

This plan covers the implementation of all 8 services in dependency order, designed so Almir and team can build one component at a time and verify each before moving forward. Every component follows the same patterns established in `infra/templates/`.

**Current state:** Phase 1 complete — infrastructure provisioned, tooling exists (`config-sync`, `seed-data`, `simple-agent`). CRM API (Component 1) is fully built. The remaining 7 components are scaffolded with README placeholders.

**Dependency DAG:**

```
   ┌──────────────┐          ┌──────────────┐
   │  CRM API (1) │          │Know. MCP (3) │ ← Independent (AI Search)
   └──────┬───────┘          └──────┬───────┘
          │                         │
          ▼                         │
   ┌──────────┐                     │
   │CRM MCP(2)│                     │
   └────┬─────┘                     │
        │                           │
        ▼                           ▼
   ┌──────────┐              ┌──────────────┐
   │CRM       │              │Product       │
   │Agent (4) │              │Agent (5)     │
   └────┬─────┘              └──────┬───────┘
        │                           │
        └──────────┬────────────────┘
                   ▼
          ┌─────────────────┐
          │Orchestrator (6) │
          └────────┬────────┘
                   │
                   ▼
            ┌────────────┐      ┌───────────┐
            │ BFF API (7) │─────│Blazor UI(8)│
            └────────────┘      └───────────┘
```

> *This DAG shows build order, not runtime dependencies. Knowledge MCP is runtime-independent of CRM API — it calls Azure AI Search directly.*

---

## Phase 0 — Pre-Implementation Tasks

These must be completed before the first component. They establish shared standards that every service depends on.

### T-16: Error Handling Standards

**What:** Define a standard error response model and HTTP status code conventions used by every API.

**Deliverables:**
- [ ] Standard `ProblemDetails` response model (RFC 9457) — all APIs return this on error
- [ ] HTTP status code conventions documented:
  - `400` — validation errors (bad input)
  - `401` — unauthenticated
  - `403` — unauthorized (valid identity, wrong permissions)
  - `404` — resource not found
  - `409` — conflict (duplicate create, stale update)
  - `429` — rate limited (BFF chat)
  - `500` — unhandled server error
  - `503` — dependency unavailable (Cosmos down, AI Search down)
- [ ] Global exception handler middleware pattern using `IExceptionHandler` (.NET 9)
- [ ] Structured error logging (log correlation ID, operation, error detail)

**Convention:**
```json
{
  "type": "https://tools.ietf.org/html/rfc9457",
  "title": "Not Found",
  "status": 404,
  "detail": "Customer with ID '999' was not found.",
  "instance": "/api/v1/customers/999",
  "traceId": "00-abc123..."
}
```

### T-17: Agent Prompt Specifications

**What:** Write the system prompts for each agent before building them. Prompts define agent behavior and are as critical as code.

**Deliverables:**
- [ ] **CRM Agent prompt** — persona, capabilities, tone, tool-calling conventions, image reference pattern (`imageFilename` → markdown), escalation rules
- [ ] **Product Agent prompt** — persona, catalog expertise, recommendation style, how to present promotions with eligibility, image reference pattern
- [ ] **Orchestrator Agent prompt** — intent classification rules, routing logic (CRM vs Product), when to ask clarifying questions, how to combine multi-agent responses
- [ ] Store prompts as embedded resources or `prompts/` folder within each agent project (not hardcoded strings)

**Key rules all agents must follow:**
- Reference images as `![ProductName](imageFilename.png)` — UI rewrites URLs
- Always identify the customer from context (auth token → customer ID)
- Never fabricate data — if a tool call returns empty, say so
- Use tools for every factual claim (no hallucination from training data)

### T-18: Seed Data Verification

**What:** Verify all seed data supports the 8 business scenarios.

**Status:** ✅ Customers 106-108 already exist in `customers.csv` (the concern from history.md has been resolved). All 12 knowledge base documents exist as PDF + TXT.

**Deliverables:**
- [ ] Verify all 8 scenario flows have matching data (customers, orders, order-items, products, promotions, support-tickets)
- [ ] Verify support-tickets.csv has the open ticket for Tom Garcia (Scenario 8)
- [ ] Run `seed-data` tool against Cosmos DB and verify all 6 containers populated
- [ ] Verify AI Search index has all 12 knowledge documents indexed

### T-19: Solution File Reorganization

**What:** Add solution folders so the .sln stays navigable as 16+ projects are added.

**Deliverables:**
- [ ] Add solution folders: `Tools`, `Domain APIs`, `MCP Servers`, `Agents`, `Frontend`, `Tests`
- [ ] Move existing projects: `simple-agent` → Tools, `config-sync` → Tools, `seed-data` → Tools
- [ ] Each new project gets added to the appropriate folder as it's created

### T-20: Shared Patterns Checklist

Every service will need these — establish the pattern once in CRM API, then replicate:

- [ ] **Configuration pattern**: `appsettings.{Environment}.json` generated by `config-sync` from Key Vault — loaded by default host builder
- [ ] **DefaultAzureCredential with tenant pinning**: Read `AzureAd:TenantId` from config (per decisions.md)
- [ ] **Health check endpoints**: `/health` (liveness — always 200) and `/ready` (readiness — checks dependencies)
- [ ] **Structured logging**: `builder.Logging.AddJsonConsole()` for AKS log aggregation
- [ ] **Resilience via Polly**: `Microsoft.Extensions.Http.Resilience` for all outbound HTTP calls (retry + circuit breaker + timeout)
- [ ] **Dockerfile** from `infra/templates/Dockerfile.template` — ⚠️ **Required deliverable per component, not optional.** Each component has an explicit "Containerization & Deployment" section with step-by-step Dockerfile tasks.
- [ ] **Helm chart** from `infra/templates/helm-base/` — ⚠️ **Required deliverable per component, not optional.** Each component has an explicit "Containerization & Deployment" section with step-by-step Helm chart tasks.

> **⚠️ Dockerfile and Helm chart are NOT optional add-ons.** They are first-class implementation steps for every component. A component is not "done" until its Dockerfile builds, its container runs and passes health checks, and its Helm chart lints and renders valid YAML. See the "Containerization & Deployment" section within each component below.

---

## Component 1: CRM API

> **✅ COMPLETED — see `src/crm-api/` for the actual implementation.** The sections below were the original build plan. Some details (NuGet packages, config keys, filenames) have diverged — the source code is the source of truth.

> **The foundation of everything.** A .NET 9 Minimal API serving all CRM data from Cosmos DB. Every other component depends on it — directly (BFF, CRM MCP) or transitively (agents, UI).

### What It Does
Exposes 11 RESTful endpoints for customers, orders, products, promotions, and support tickets. Reads from and writes to Cosmos DB. This is the single source of truth for all structured CRM data.

### Key Files/Folders
```
src/crm-api/
├── Program.cs                    # Minimal API host, DI, middleware, endpoint mapping
├── Contoso.CrmApi.csproj        # Project file
├── Models/
│   ├── Customer.cs
│   ├── Order.cs
│   ├── OrderItem.cs
│   ├── Product.cs
│   ├── Promotion.cs
│   ├── SupportTicket.cs
│   └── CreateTicketRequest.cs
├── Services/
│   ├── ICosmosService.cs         # Interface for Cosmos operations
│   └── CosmosService.cs          # Cosmos DB client wrapper (all container operations)
├── Endpoints/
│   ├── CustomerEndpoints.cs      # GET /api/v1/customers, GET /api/v1/customers/{id}
│   ├── OrderEndpoints.cs         # GET /api/v1/orders/{id}, GET /api/v1/customers/{id}/orders
│   ├── ProductEndpoints.cs       # GET /api/v1/products, GET /api/v1/products/{id}
│   ├── PromotionEndpoints.cs     # GET /api/v1/promotions, GET /api/v1/promotions/eligible/{customerId}
│   └── SupportTicketEndpoints.cs # GET /api/v1/customers/{id}/tickets, POST /api/v1/tickets
├── Middleware/
│   └── GlobalExceptionHandler.cs # Global ProblemDetails error handler
├── Dockerfile
└── chart/
    ├── Chart.yaml
    ├── values.yaml
    └── templates/                # (copied from helm-base/templates/)
src/crm-api.tests/
├── crm-api.tests.csproj
├── Endpoints/                    # Integration tests per endpoint group
└── Services/                     # Unit tests for CosmosService
```

### Endpoints (11 total)
| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/v1/customers` | List all customers |
| GET | `/api/v1/customers/{id}` | Get customer by ID |
| GET | `/api/v1/customers/{id}/orders` | Get all orders for a customer |
| GET | `/api/v1/orders/{id}` | Get order by ID (cross-partition query — accepted trade-off) |
| GET | `/api/v1/orders/{id}/items` | Get line items for an order |
| GET | `/api/v1/products` | List/search products (query, category, in_stock_only filters) |
| GET | `/api/v1/products/{id}` | Get product by ID |
| GET | `/api/v1/promotions` | List all active promotions |
| GET | `/api/v1/promotions/eligible/{customerId}` | Promotions matching customer's loyalty tier |
| GET | `/api/v1/customers/{id}/tickets` | Get support tickets (optional `open_only` filter) |
| POST | `/api/v1/tickets` | Create a new support ticket |
| GET | `/health` | Liveness probe |
| GET | `/ready` | Readiness probe (Cosmos DB connectivity check) |

### External Dependencies
| Dependency | NuGet Package | Version |
|------------|---------------|---------|
| Cosmos DB (CRM) | `Microsoft.Azure.Cosmos` | 3.46.1 |
| Azure Identity | `Azure.Identity` | 1.19.0 |
| JSON Serialization | `Newtonsoft.Json` | 13.0.3 |

### Azure Services
- **Cosmos DB (CRM account)** — 6 containers: Customers, Orders, OrderItems, Products, Promotions, SupportTickets
- **Key Vault** (indirectly via config-sync → appsettings.{Environment}.json)

### Dependencies
- **Depends on:** Nothing (this is the root of the dependency chain)
- **Unblocks:** CRM MCP (Component 2), BFF API (Component 7)

### Estimated Complexity: **Medium-High**
11 endpoints, Cosmos DB integration with multiple containers and partition key strategies, cross-partition queries, write operations (ticket creation), error handling patterns. This is the most endpoint-heavy service but the patterns established here are reused everywhere.

### Configuration Keys
```
CosmosDb:Endpoint        → Cosmos DB account endpoint
CosmosDb:DatabaseName    → Database name (e.g., "contoso-crm")
AzureAd:TenantId         → Tenant for DefaultAzureCredential
```

### Testing Strategy
- [ ] **Unit tests:** CosmosService with mocked `Container` — verify query construction, partition key handling, type mapping
- [ ] **Integration tests:** `WebApplicationFactory<Program>` with real Cosmos DB Emulator or in-memory mock
- [ ] **Manual verification:** `dotnet run` → hit each endpoint with `curl`/`httpie` → compare results against CSV seed data
- [ ] **Scenario verification:** Walk through Scenarios 1-8 data access patterns against the API

### "Done" Checklist
- [ ] All 11 endpoints return correct data matching seed CSV files
- [ ] POST /api/v1/tickets creates a document in SupportTickets container
- [ ] `/health` returns 200, `/ready` returns 200 when Cosmos is reachable (503 when not)
- [ ] ProblemDetails returned on all error paths (404, 400, 500)
- [ ] Structured JSON logging (correlation IDs)
- [ ] Dockerfile builds and runs (see Containerization & Deployment → Step A below)
- [ ] Helm chart validates (see Containerization & Deployment → Step B below)
- [ ] Unit tests pass (CosmosService logic)
- [ ] Integration tests pass (endpoint → response verification)
- [ ] Added to solution file under `Domain APIs` folder

### Containerization & Deployment

**Step A: Dockerfile**
- [ ] Create `src/crm-api/Dockerfile` using `infra/templates/Dockerfile.template` as base
- [ ] Customize build args for `src/crm-api/crm-api.csproj` project path
- [ ] Expose port 8080 (Kestrel default for non-root containers)
- [ ] Verify: `docker build -t crm-api:dev src/crm-api/`
- [ ] Verify: `docker run --rm -p 8080:8080 crm-api:dev` → `curl http://localhost:8080/health` returns 200

**Step B: Helm Chart**
- [ ] Create `src/crm-api/chart/` using `infra/templates/helm-base/` as base
- [ ] Customize `Chart.yaml` (name: `crm-api`, description: "Contoso Outdoors CRM REST API")
- [ ] Customize `values.yaml`:
  - Image repository/tag for crm-api
  - Environment variables: `COSMOSDB_CRM_ENDPOINT`, `COSMOSDB_CRM_DATABASE`, `AzureAd__TenantId`
  - Resource limits: 256Mi memory request / 512Mi limit, 100m CPU request / 250m limit (data API — moderate resource profile)
  - Service account name matching Terraform-provisioned SA for Cosmos DB access
  - Liveness probe: `/health`, Readiness probe: `/ready`
- [ ] Add Cosmos DB connection config to `templates/configmap.yaml`
- [ ] Verify: `helm lint src/crm-api/chart/`
- [ ] Verify: `helm template crm-api src/crm-api/chart/` renders valid YAML

---

## Component 2: CRM MCP Server

> **Protocol adapter — translates MCP tool calls into CRM API HTTP requests.** Agents discover these tools at runtime and use them to access CRM data. No business logic lives here.

### What It Does
An MCP server exposing 11 tools that map 1:1 to CRM API endpoints. Uses the ModelContextProtocol C# SDK with Streamable HTTP transport. Agents connect to this server, discover tools, and invoke them to get customer/order/product data.

### Key Files/Folders
```
src/crm-mcp/
├── Program.cs                    # MCP server host, tool registration, HTTP client setup
├── crm-mcp.csproj
├── Tools/
│   ├── CustomerTools.cs          # get_all_customers, get_customer_detail
│   ├── OrderTools.cs             # get_customer_orders, get_order_detail, get_order_items
│   ├── ProductTools.cs           # get_products, get_product_detail
│   ├── PromotionTools.cs         # get_promotions, get_eligible_promotions
│   └── SupportTicketTools.cs     # get_support_tickets, create_support_ticket
├── CrmApiClient.cs               # Typed HTTP client wrapping CRM API calls
├── Dockerfile
└── chart/
    ├── Chart.yaml
    ├── values.yaml
    └── templates/
src/crm-mcp.tests/
├── crm-mcp.tests.csproj
├── Tools/                        # Unit tests per tool (mock HTTP responses)
└── Integration/                  # End-to-end MCP protocol tests
```

### MCP Tools (11 total)
| Tool Name | CRM API Endpoint | Description |
|-----------|-----------------|-------------|
| `get_all_customers` | GET /api/v1/customers | List all customers |
| `get_customer_detail` | GET /api/v1/customers/{id} | Full customer profile |
| `get_customer_orders` | GET /api/v1/customers/{id}/orders | Customer's orders |
| `get_order_detail` | GET /api/v1/orders/{id} | Order with line items |
| `get_order_items` | GET /api/v1/orders/{id}/items | Returns line items for a specific order |
| `get_products` | GET /api/v1/products | Search/list products |
| `get_product_detail` | GET /api/v1/products/{id} | Full product info |
| `get_promotions` | GET /api/v1/promotions | Active promotions |
| `get_eligible_promotions` | GET /api/v1/promotions/eligible/{id} | Tier-filtered promotions |
| `get_support_tickets` | GET /api/v1/customers/{id}/tickets | Customer's tickets |
| `create_support_ticket` | POST /api/v1/tickets | Create ticket |

### External Dependencies
| Dependency | NuGet Package | Version |
|------------|---------------|---------|
| MCP SDK | `ModelContextProtocol` | latest stable |
| MCP ASP.NET | `ModelContextProtocol.AspNetCore` | latest stable |
| HTTP Resilience | `Microsoft.Extensions.Http.Resilience` | latest |
| Azure Identity | `Azure.Identity` | 1.19.0 |

### Azure Services
- **CRM API** (HTTP, internal ClusterIP in AKS)

### Dependencies
- **Depends on:** CRM API (Component 1) — every tool is an HTTP call to CRM API
- **Unblocks:** CRM Agent (Component 4), Product Agent (Component 5)

### Estimated Complexity: **Simple-Medium**
Thin adapter layer — each tool is a method that calls an HTTP endpoint and returns the JSON. The complexity is in correctly setting up the MCP SDK, Streamable HTTP transport, and tool schema annotations.

### Configuration Keys
```
CrmApi:BaseUrl               → CRM API internal URL (e.g., http://crm-api.contoso.svc.cluster.local)
AzureAd:TenantId         → Tenant for DefaultAzureCredential
```

### Testing Strategy
- [ ] **Unit tests:** Mock `HttpClient` → verify each tool calls the correct endpoint with correct parameters and returns properly shaped results
- [ ] **Integration tests:** Start CRM API + CRM MCP together → invoke MCP tools via test client → verify end-to-end data flow
- [ ] **Manual verification:** Use MCP Inspector CLI or a test script to connect to the MCP server, list tools, and invoke each one
- [ ] **Protocol verification:** Confirm tool discovery (list_tools) returns correct schemas with parameter types and descriptions

### "Done" Checklist
- [ ] All 11 tools registered and discoverable via MCP protocol
- [ ] Each tool returns correct data matching CRM API responses
- [ ] `create_support_ticket` tool creates a real ticket through CRM API
- [ ] Error handling: CRM API errors are translated to MCP error responses
- [ ] Polly resilience on HTTP client (retry, circuit breaker, timeout)
- [ ] `/health` returns 200, `/ready` checks CRM API reachability
- [ ] Dockerfile builds and runs (see Containerization & Deployment → Step A below)
- [ ] Helm chart validates (see Containerization & Deployment → Step B below)
- [ ] Unit tests pass
- [ ] Integration tests pass
- [ ] Added to solution file under `MCP Servers` folder

### Containerization & Deployment

**Step A: Dockerfile**
- [ ] Create `src/crm-mcp/Dockerfile` using `infra/templates/Dockerfile.template` as base
- [ ] Customize build args for `src/crm-mcp/crm-mcp.csproj` project path
- [ ] Expose port 8080 (Streamable HTTP transport)
- [ ] Verify: `docker build -t crm-mcp:dev src/crm-mcp/`
- [ ] Verify: `docker run --rm -p 8080:8080 crm-mcp:dev` → `curl http://localhost:8080/health` returns 200

**Step B: Helm Chart**
- [ ] Create `src/crm-mcp/chart/` using `infra/templates/helm-base/` as base
- [ ] Customize `Chart.yaml` (name: `crm-mcp`, description: "CRM MCP Server — protocol adapter for CRM API")
- [ ] Customize `values.yaml`:
  - Image repository/tag for crm-mcp
  - Environment variables: `CrmApi:BaseUrl`, `AzureAd:TenantId`
  - Resource limits: 128Mi memory request / 256Mi limit, 50m CPU request / 200m limit (thin adapter — lightweight profile)
  - Service account name matching Terraform-provisioned SA
  - Liveness probe: `/health`, Readiness probe: `/ready`
- [ ] Add CRM API base URL config to `templates/configmap.yaml`
- [ ] Verify: `helm lint src/crm-mcp/chart/`
- [ ] Verify: `helm template crm-mcp src/crm-mcp/chart/` renders valid YAML

---

## Component 3: Knowledge MCP Server

> **Semantic search over policies, guides, and procedures.** Calls Azure AI Search directly (no wrapper API needed for a single SDK call). The RAG pattern entry point for agents.

### What It Does
An MCP server exposing a single tool: `search_knowledge_base`. Takes a natural language query and returns the most relevant chunks from indexed PDF documents (return policies, sizing guides, warranty info, etc.) using Azure AI Search's semantic ranker.

### Key Files/Folders
```
src/knowledge-mcp/
├── Program.cs                    # MCP server host, single tool registration
├── knowledge-mcp.csproj
├── Tools/
│   └── KnowledgeSearchTool.cs    # search_knowledge_base implementation
├── Services/
│   ├── ISearchService.cs         # Interface for search operations
│   └── AzureSearchService.cs     # Azure AI Search SDK client
├── Models/
│   └── SearchResult.cs           # Chunk text, source document, relevance score
├── Dockerfile
└── chart/
    ├── Chart.yaml
    ├── values.yaml
    └── templates/
src/knowledge-mcp.tests/
├── knowledge-mcp.tests.csproj
├── Tools/                        # Mock search service → verify tool behavior
└── Services/                     # Integration tests with real AI Search
```

### MCP Tools (1 total)
| Tool Name | Parameters | Description |
|-----------|-----------|-------------|
| `search_knowledge_base` | `query` (string, required), `top_k` (int, optional, default 3) | Vector similarity search over knowledge documents |

### External Dependencies
| Dependency | NuGet Package | Version |
|------------|---------------|---------|
| MCP SDK | `ModelContextProtocol` | latest stable |
| MCP ASP.NET | `ModelContextProtocol.AspNetCore` | latest stable |
| AI Search | `Azure.Search.Documents` | latest |
| Azure Identity | `Azure.Identity` | 1.19.0 |

### Azure Services
- **Azure AI Search** — `knowledge-documents` index (semantic ranker + vector search)

### Dependencies
- **Depends on:** Nothing at the code level (calls AI Search directly). Requires AI Search index to be populated (Terraform + indexer does this).
- **Unblocks:** CRM Agent (Component 4), Product Agent (Component 5)

### Estimated Complexity: **Simple**
Single tool, single SDK call. The main work is configuring the `SearchClient` with the correct index name, semantic configuration, and formatting the chunked results back to the agent.

### Configuration Keys
```
Search:Endpoint              → AI Search service endpoint
Search:IndexName             → Index name (e.g., "knowledge-documents")
AzureAd:TenantId         → Tenant for DefaultAzureCredential
```

### Testing Strategy
- [ ] **Unit tests:** Mock `SearchClient` → verify query construction, top_k handling, result formatting
- [ ] **Integration tests:** Connect to real AI Search → search for known content (e.g., "return policy" should find return-and-refund-policy chunks)
- [ ] **Manual verification:** Invoke `search_knowledge_base` with queries from each scenario (boot sizing, return policy, warranty, gear care) → verify relevant documents are returned
- [ ] **Quality check:** Verify the result format includes chunk text, source document name, and relevance score

### "Done" Checklist
- [ ] `search_knowledge_base` tool registered and discoverable
- [ ] Returns relevant chunks for all knowledge base topics (policies, guides, procedures)
- [ ] Handles empty results gracefully (returns empty array, not error)
- [ ] `top_k` parameter works (default 3, configurable 1-10)
- [ ] `/health` returns 200, `/ready` checks AI Search reachability
- [ ] Dockerfile builds and runs (see Containerization & Deployment → Step A below)
- [ ] Helm chart validates (see Containerization & Deployment → Step B below)
- [ ] Unit tests pass
- [ ] Integration tests pass
- [ ] Added to solution file under `MCP Servers` folder

### Containerization & Deployment

**Step A: Dockerfile**
- [ ] Create `src/knowledge-mcp/Dockerfile` using `infra/templates/Dockerfile.template` as base
- [ ] Customize build args for `src/knowledge-mcp/knowledge-mcp.csproj` project path
- [ ] Expose port 8080
- [ ] Verify: `docker build -t knowledge-mcp:dev src/knowledge-mcp/`
- [ ] Verify: `docker run --rm -p 8080:8080 knowledge-mcp:dev` → `curl http://localhost:8080/health` returns 200

**Step B: Helm Chart**
- [ ] Create `src/knowledge-mcp/chart/` using `infra/templates/helm-base/` as base
- [ ] Customize `Chart.yaml` (name: `knowledge-mcp`, description: "Knowledge Base MCP Server — semantic search over policies and guides")
- [ ] Customize `values.yaml`:
  - Image repository/tag for knowledge-mcp
  - Environment variables: `Search:Endpoint`, `Search:IndexName`, `AzureAd:TenantId`
  - Resource limits: 128Mi memory request / 256Mi limit, 50m CPU request / 200m limit (single-tool server — lightweight profile)
  - Service account name matching Terraform-provisioned SA for AI Search access
  - Liveness probe: `/health`, Readiness probe: `/ready`
- [ ] Add AI Search config to `templates/configmap.yaml`
- [ ] Verify: `helm lint src/knowledge-mcp/chart/`
- [ ] Verify: `helm template knowledge-mcp src/knowledge-mcp/chart/` renders valid YAML

---

## Component 4: CRM Agent

> **The CRM specialist.** Handles customer inquiries about orders, returns, billing, and support tickets. Uses CRM MCP tools for data and Knowledge MCP for policy/procedure lookups.

### What It Does
An AI agent built with `Microsoft.Agents.AI` and `Azure.AI.OpenAI`. Receives a customer question plus conversation history, uses MCP tools to retrieve relevant data (orders, tickets, policies), and produces a helpful response. Handles Scenarios 1, 2, 4, 6, 7, 8.

### Key Files/Folders
```
src/crm-agent/
├── Program.cs                    # Minimal API host, agent construction, /api/v1/chat endpoint
├── crm-agent.csproj
├── Prompts/
│   └── system-prompt.md          # CRM agent system prompt (embedded resource)
├── Models/
│   ├── ChatRequest.cs            # { customerId, message, conversationHistory[] }
│   └── ChatResponse.cs           # { message, toolsUsed[] }
├── AgentFactory.cs               # Constructs the agent with MCP tool connections
├── Dockerfile
└── chart/
    ├── Chart.yaml
    ├── values.yaml
    └── templates/
src/crm-agent.tests/
├── crm-agent.tests.csproj
├── AgentBehavior/                # Scenario-based tests with mocked tools
└── Integration/                  # End-to-end with real MCP servers
```

### Endpoints
| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/v1/chat` | Send a message to the CRM agent |
| GET | `/health` | Liveness probe |
| GET | `/ready` | Readiness probe (MCP server + Azure OpenAI connectivity) |

### External Dependencies
| Dependency | NuGet Package | Version |
|------------|---------------|---------|
| Agent Framework | `Microsoft.Agents.AI` | 1.0.0 |
| Agent + OpenAI | `Microsoft.Agents.AI.OpenAI` | 1.0.0 |
| Azure OpenAI | `Azure.AI.OpenAI` | 2.1.0 |
| Extensions.AI | `Microsoft.Extensions.AI` | 10.4.1 |
| Extensions.AI.Abstractions | `Microsoft.Extensions.AI.Abstractions` | 10.4.1 |
| Extensions.AI.OpenAI | `Microsoft.Extensions.AI.OpenAI` | 10.4.1 |
| MCP SDK | `ModelContextProtocol` | latest stable |
| Azure Identity | `Azure.Identity` | 1.19.0 |

> **Note:** `Microsoft.Extensions.AI` and `Microsoft.Extensions.AI.Abstractions` are pinned to 10.4.1. This was required with 1.0.0-rc2 to avoid a transitive `TypeLoadException`. With 1.0.0 GA these pins may no longer be needed — verify and remove if dependency resolution is clean.

### Azure Services
- **Azure OpenAI** — gpt-4.1 deployment for chat completions
- **CRM MCP Server** (HTTP, ClusterIP)
- **Knowledge MCP Server** (HTTP, ClusterIP)

### Dependencies
- **Depends on:** CRM MCP (Component 2), Knowledge MCP (Component 3)
- **Unblocks:** Orchestrator Agent (Component 6)

### Estimated Complexity: **Complex**
Agent construction with `Microsoft.Agents.AI` (RC2 — potential API instability), MCP tool connection, prompt engineering, multi-tool orchestration (agent may call 3+ tools per request), conversation history management, image reference pattern.

### Configuration Keys
```
AzureOpenAi:Endpoint            → Azure OpenAI endpoint
AzureOpenAi:DeploymentName      → Chat model deployment (gpt-4.1)
CrmMcp:BaseUrl                  → CRM MCP server URL
KnowledgeMcp:BaseUrl            → Knowledge MCP server URL
AzureAd:TenantId                → Tenant for DefaultAzureCredential
```

### Testing Strategy
- [ ] **Unit tests (mocked tools):** Construct agent with mock MCP tool responses → verify it calls the right tools for each scenario type and produces appropriate responses
- [ ] **Scenario tests:** Feed each CRM scenario (1, 2, 4, 6, 7, 8) through the agent with mocked tool responses → verify tool call sequence and response quality
- [ ] **Integration tests:** Full stack (CRM Agent → CRM MCP → CRM API → Cosmos DB) → send scenario prompts → verify correct data is retrieved
- [ ] **Edge cases:** Unknown customer ID, empty order history, knowledge search returns nothing

### "Done" Checklist
- [ ] POST /api/v1/chat accepts a message and returns an agent response
- [ ] Agent correctly calls CRM MCP tools for data retrieval
- [ ] Agent correctly calls Knowledge MCP for policy/procedure lookups
- [ ] Agent references product images as `![Name](filename.png)` in markdown
- [ ] Handles Scenarios 1, 2, 4, 6, 7, 8 with correct tool usage
- [ ] Stateless — conversation history comes from request body, not stored locally
- [ ] `/health` returns 200, `/ready` checks MCP servers + Azure OpenAI
- [ ] Dockerfile builds and runs (see Containerization & Deployment → Step A below)
- [ ] Helm chart validates (see Containerization & Deployment → Step B below)
- [ ] Tests pass
- [ ] Added to solution file under `Agents` folder

### Containerization & Deployment

**Step A: Dockerfile**
- [ ] Create `src/crm-agent/Dockerfile` using `infra/templates/Dockerfile.template` as base
- [ ] Customize build args for `src/crm-agent/crm-agent.csproj` project path
- [ ] Ensure `Prompts/system-prompt.md` is included as an embedded resource in the build output
- [ ] Expose port 8080
- [ ] Verify: `docker build -t crm-agent:dev src/crm-agent/`
- [ ] Verify: `docker run --rm -p 8080:8080 crm-agent:dev` → `curl http://localhost:8080/health` returns 200

**Step B: Helm Chart**
- [ ] Create `src/crm-agent/chart/` using `infra/templates/helm-base/` as base
- [ ] Customize `Chart.yaml` (name: `crm-agent`, description: "CRM Specialist Agent — orders, returns, billing, and support")
- [ ] Customize `values.yaml`:
  - Image repository/tag for crm-agent
  - Environment variables: `AzureOpenAi:Endpoint`, `AzureOpenAi:DeploymentName`, `CrmMcp:BaseUrl`, `KnowledgeMcp:BaseUrl`, `AzureAd:TenantId`
  - Resource limits: 256Mi memory request / 512Mi limit, 200m CPU request / 500m limit (agent — higher profile for LLM orchestration overhead)
  - Service account name matching Terraform-provisioned SA for Azure OpenAI access
  - Liveness probe: `/health`, Readiness probe: `/ready`
- [ ] Add MCP server URLs and Azure OpenAI config to `templates/configmap.yaml`
- [ ] Verify: `helm lint src/crm-agent/chart/`
- [ ] Verify: `helm template crm-agent src/crm-agent/chart/` renders valid YAML

---

## Component 5: Product Agent

> **The product specialist.** Handles catalog browsing, product recommendations, promotion eligibility, and gear advice. Uses CRM MCP for catalog/promo data and Knowledge MCP for fitting guides.

### What It Does
An AI agent focused on product-related queries: searching the catalog, checking promotions against loyalty tier, recommending gear based on use case, and referencing fitting/sizing guides. Handles Scenarios 3 and 5.

### Key Files/Folders
```
src/product-agent/
├── Program.cs                    # Minimal API host, agent construction, /api/v1/chat endpoint
├── product-agent.csproj
├── Prompts/
│   └── system-prompt.md          # Product agent system prompt
├── Models/
│   ├── ChatRequest.cs
│   └── ChatResponse.cs
├── AgentFactory.cs
├── Dockerfile
└── chart/
    ├── Chart.yaml
    ├── values.yaml
    └── templates/
src/product-agent.tests/
├── product-agent.tests.csproj
├── AgentBehavior/
└── Integration/
```

### Endpoints
| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/v1/chat` | Send a message to the Product agent |
| GET | `/health` | Liveness probe |
| GET | `/ready` | Readiness probe |

### External Dependencies
Same NuGet packages as CRM Agent (Microsoft.Agents.AI 1.0.0, Azure.AI.OpenAI 2.1.0, MCP SDK, etc.) with the same version pinning requirements.

### Azure Services
- **Azure OpenAI** — gpt-4.1 deployment
- **CRM MCP Server** (HTTP) — product catalog, promotions, customer loyalty tier
- **Knowledge MCP Server** (HTTP) — fitting guides, care guides

### Dependencies
- **Depends on:** CRM MCP (Component 2), Knowledge MCP (Component 3)
- **Unblocks:** Orchestrator Agent (Component 6)

### Estimated Complexity: **Complex**
Same agent framework complexity as CRM Agent plus product recommendation logic in the prompt. The prompt must know how to cross-reference loyalty tier with promotion eligibility and present products with images.

### Configuration Keys
Same as CRM Agent:
```
AzureOpenAi:Endpoint, AzureOpenAi:DeploymentName
CrmMcp:BaseUrl, KnowledgeMcp:BaseUrl
AzureAd:TenantId
```

### Testing Strategy
- [ ] **Scenario tests:** Scenario 3 (tent deals + Gold loyalty) and Scenario 5 (backpack recommendation for 5-day trip) with mocked tools
- [ ] **Promotion eligibility:** Verify agent checks customer tier against promotion `min_loyalty_tier`
- [ ] **Image references:** Verify agent includes `imageFilename` in markdown for product responses
- [ ] **Integration tests:** Full stack through CRM MCP

### "Done" Checklist
- [ ] POST /api/v1/chat returns product-focused agent responses
- [ ] Agent searches products by category, recommends based on query
- [ ] Agent checks promotion eligibility using customer loyalty tier
- [ ] Agent references fitting/care guides from Knowledge MCP
- [ ] Agent includes product images in markdown responses
- [ ] Handles Scenarios 3 and 5 correctly
- [ ] Stateless, health checks, tests
- [ ] Added to solution file under `Agents` folder

### Containerization & Deployment

**Step A: Dockerfile**
- [ ] Create `src/product-agent/Dockerfile` using `infra/templates/Dockerfile.template` as base
- [ ] Customize build args for `src/product-agent/product-agent.csproj` project path
- [ ] Ensure `Prompts/system-prompt.md` is included as an embedded resource in the build output
- [ ] Expose port 8080
- [ ] Verify: `docker build -t product-agent:dev src/product-agent/`
- [ ] Verify: `docker run --rm -p 8080:8080 product-agent:dev` → `curl http://localhost:8080/health` returns 200

**Step B: Helm Chart**
- [ ] Create `src/product-agent/chart/` using `infra/templates/helm-base/` as base
- [ ] Customize `Chart.yaml` (name: `product-agent`, description: "Product Specialist Agent — catalog, recommendations, and promotions")
- [ ] Customize `values.yaml`:
  - Image repository/tag for product-agent
  - Environment variables: `AzureOpenAi:Endpoint`, `AzureOpenAi:DeploymentName`, `CrmMcp:BaseUrl`, `KnowledgeMcp:BaseUrl`, `AzureAd:TenantId`
  - Resource limits: 256Mi memory request / 512Mi limit, 200m CPU request / 500m limit (agent — higher profile for LLM orchestration overhead)
  - Service account name matching Terraform-provisioned SA for Azure OpenAI access
  - Liveness probe: `/health`, Readiness probe: `/ready`
- [ ] Add MCP server URLs and Azure OpenAI config to `templates/configmap.yaml`
- [ ] Verify: `helm lint src/product-agent/chart/`
- [ ] Verify: `helm template product-agent src/product-agent/chart/` renders valid YAML

---

## Component 6: Orchestrator Agent

> **The traffic cop.** Classifies user intent and routes to the CRM Agent or Product Agent. Does not call MCP tools directly — it delegates to specialist agents.

### What It Does
Receives a user message + conversation history, uses LLM-based intent classification to determine whether the request is CRM-related (orders, returns, tickets, account) or product-related (catalog, recommendations, promotions, guides), then forwards to the appropriate specialist agent via HTTP. Returns the specialist's response to the BFF.

### Key Files/Folders
```
src/orchestrator-agent/
├── Program.cs                    # Minimal API host, /api/v1/chat endpoint
├── orchestrator-agent.csproj
├── Prompts/
│   └── system-prompt.md          # Orchestrator system prompt (routing rules)
├── Models/
│   ├── ChatRequest.cs
│   └── ChatResponse.cs
├── Services/
│   ├── IntentClassifier.cs       # LLM-based intent classification
│   └── AgentRouter.cs            # Routes to CRM or Product agent via HTTP
├── Dockerfile
└── chart/
    ├── Chart.yaml
    ├── values.yaml
    └── templates/
src/orchestrator-agent.tests/
├── orchestrator-agent.tests.csproj
├── Routing/                      # Intent classification accuracy tests
└── Integration/
```

### Endpoints
| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/v1/chat` | Send a message to the Orchestrator |
| GET | `/health` | Liveness probe |
| GET | `/ready` | Readiness probe (checks CRM + Product agent health) |

### External Dependencies
| Dependency | NuGet Package | Version |
|------------|---------------|---------|
| Agent Framework | `Microsoft.Agents.AI` | 1.0.0 |
| Azure OpenAI | `Azure.AI.OpenAI` | 2.1.0 |
| HTTP Resilience | `Microsoft.Extensions.Http.Resilience` | latest |
| Azure Identity | `Azure.Identity` | 1.19.0 |
| (same Extensions.AI pinning) | | 10.4.1 |

### Azure Services
- **Azure OpenAI** — gpt-4.1 for intent classification
- **CRM Agent** (HTTP, ClusterIP)
- **Product Agent** (HTTP, ClusterIP)

### Dependencies
- **Depends on:** CRM Agent (Component 4), Product Agent (Component 5)
- **Unblocks:** BFF API (Component 7)

### Estimated Complexity: **Medium**
Simpler than specialist agents — no MCP tool calls, just intent classification and HTTP routing. The complexity is in the prompt engineering for accurate classification and handling edge cases (ambiguous intents, multi-domain questions).

### Configuration Keys
```
AzureOpenAi:Endpoint, AzureOpenAi:DeploymentName
CrmAgent:BaseUrl                 → CRM Agent URL
ProductAgent:BaseUrl             → Product Agent URL
AzureAd:TenantId
```

### Testing Strategy
- [ ] **Intent classification tests:** 20+ sample messages → verify correct routing (CRM vs Product)
- [ ] **Edge cases:** Ambiguous queries ("I want to return this and buy a new one"), mixed-domain questions
- [ ] **Scenario tests:** All 8 scenarios → verify each routes to the correct specialist
- [ ] **Integration tests:** Full pipeline (Orchestrator → Specialist → MCP → CRM API)
- [ ] **Failure handling:** What happens when a specialist agent is down? (circuit breaker, graceful error)

### "Done" Checklist
- [ ] POST /api/v1/chat correctly routes to CRM or Product agent
- [ ] Intent classification is accurate for all 8 business scenarios
- [ ] Handles ambiguous intents (asks clarifying question or picks most likely)
- [ ] Returns specialist agent response as-is (preserves markdown, images)
- [ ] Polly resilience on outbound HTTP to specialist agents
- [ ] Health checks, tests
- [ ] Added to solution file under `Agents` folder

### Containerization & Deployment

**Step A: Dockerfile**
- [ ] Create `src/orchestrator-agent/Dockerfile` using `infra/templates/Dockerfile.template` as base
- [ ] Customize build args for `src/orchestrator-agent/orchestrator-agent.csproj` project path
- [ ] Ensure `Prompts/system-prompt.md` is included as an embedded resource in the build output
- [ ] Expose port 8080
- [ ] Verify: `docker build -t orchestrator-agent:dev src/orchestrator-agent/`
- [ ] Verify: `docker run --rm -p 8080:8080 orchestrator-agent:dev` → `curl http://localhost:8080/health` returns 200

**Step B: Helm Chart**
- [ ] Create `src/orchestrator-agent/chart/` using `infra/templates/helm-base/` as base
- [ ] Customize `Chart.yaml` (name: `orchestrator-agent`, description: "Orchestrator Agent — intent classification and agent routing")
- [ ] Customize `values.yaml`:
  - Image repository/tag for orchestrator-agent
  - Environment variables: `AzureOpenAi:Endpoint`, `AzureOpenAi:DeploymentName`, `CrmAgent:BaseUrl`, `ProductAgent:BaseUrl`, `AzureAd:TenantId`
  - Resource limits: 128Mi memory request / 256Mi limit, 100m CPU request / 250m limit (routing agent — lighter than specialist agents, no MCP tool calls)
  - Service account name matching Terraform-provisioned SA for Azure OpenAI access
  - Polly resilience config for outbound HTTP to specialist agents
  - Liveness probe: `/health`, Readiness probe: `/ready`
- [ ] Add specialist agent URLs and Azure OpenAI config to `templates/configmap.yaml`
- [ ] Verify: `helm lint src/orchestrator-agent/chart/`
- [ ] Verify: `helm template orchestrator-agent src/orchestrator-agent/chart/` renders valid YAML

---

## Component 7: BFF API

> **The gateway between the browser and everything else.** Handles authentication, conversation persistence, CRM data proxy, image proxy, and the chat pipeline.

### What It Does
A .NET 9 Minimal API that: (1) validates JWT tokens from the Blazor UI (Microsoft.Identity.Web), (2) proxies CRM API endpoints for the UI's data views, (3) proxies product images from Blob Storage, (4) manages chat conversations in Cosmos DB (agent state account), and (5) orchestrates the chat flow (save user message → call Orchestrator → save assistant response → return to UI).

### Key Files/Folders
```
src/bff-api/
├── Program.cs                    # Host, DI, auth, CORS, middleware, endpoints
├── bff-api.csproj
├── Auth/
│   └── JwtConfiguration.cs       # Microsoft.Identity.Web setup
├── Endpoints/
│   ├── CrmProxyEndpoints.cs      # Proxy CRM API endpoints for UI
│   ├── ChatEndpoints.cs          # POST /api/v1/chat, GET /api/v1/conversations
│   ├── ImageEndpoints.cs         # GET /api/v1/images/{filename}
│   └── ConversationEndpoints.cs  # GET /api/v1/conversations/{id}/messages
├── Services/
│   ├── ConversationService.cs    # Cosmos DB (agents) — conversation CRUD
│   ├── ImageProxyService.cs      # Blob Storage — validate filename, stream bytes
│   └── OrchestratorClient.cs     # HTTP client to Orchestrator Agent
├── Middleware/
│   ├── CorrelationIdMiddleware.cs
│   ├── RateLimitingConfig.cs     # Rate limiting on /api/v1/chat
│   └── ExceptionHandler.cs
├── Hubs/
│   └── ChatHub.cs                # SignalR hub for streaming responses
├── Dockerfile
└── chart/
    ├── Chart.yaml
    ├── values.yaml
    └── templates/
src/bff-api.tests/
├── bff-api.tests.csproj
├── Auth/                         # JWT validation tests
├── Endpoints/                    # Integration tests
├── Services/                     # Unit tests
└── Middleware/                    # Rate limiting, CORS tests
```

### Endpoints
| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/v1/customers/{id}` | Proxy → CRM API (authenticated) |
| GET | `/api/v1/customers/{id}/orders` | Proxy → CRM API |
| POST | `/api/v1/chat` | Chat endpoint (rate limited): save msg → Orchestrator → save response |
| GET | `/api/v1/conversations` | List user's conversations |
| GET | `/api/v1/conversations/{id}/messages` | Get messages in a conversation |
| GET | `/api/v1/images/{filename}` | Proxy product image from Blob Storage |
| GET | `/health` | Liveness probe |
| GET | `/ready` | Readiness (Cosmos + CRM API + Orchestrator) |

### External Dependencies
| Dependency | NuGet Package | Version |
|------------|---------------|---------|
| JWT/Auth | `Microsoft.Identity.Web` | latest |
| Cosmos DB | `Microsoft.Azure.Cosmos` | 3.46.1 |
| Blob Storage | `Azure.Storage.Blobs` | latest |
| SignalR | `Microsoft.AspNetCore.SignalR` | (built-in) |
| Rate Limiting | `System.Threading.RateLimiting` | (built-in .NET 9) |
| HTTP Resilience | `Microsoft.Extensions.Http.Resilience` | latest |
| Azure Identity | `Azure.Identity` | 1.19.0 |

### Azure Services
- **Cosmos DB (Agents account)** — conversations + messages containers
- **Blob Storage** — product-images container
- **CRM API** (HTTP)
- **Orchestrator Agent** (HTTP)

### Dependencies
- **Depends on:** CRM API (Component 1), Orchestrator Agent (Component 6)
- **Unblocks:** Blazor UI (Component 8)

### Estimated Complexity: **Complex**
Most responsibilities of any single service: auth, proxy, chat orchestration, conversation persistence, image proxy, rate limiting, CORS, SignalR. Security boundary between public internet and internal services.

### Configuration Keys
```
CosmosDb:AgentsEndpoint, CosmosDb:AgentsDatabase
CosmosDb:CrmEndpoint (for readiness check)
Storage:ImagesEndpoint, Storage:ImagesContainer
CrmApi:BaseUrl
Orchestrator:BaseUrl
AzureAd:ClientId, AzureAd:TenantId
Bff:Hostname
```

### Testing Strategy
- [ ] **Auth tests:** Valid JWT → 200, expired/missing JWT → 401, wrong audience → 403
- [ ] **Chat flow tests:** Mock Orchestrator → verify message saved to Cosmos → response returned
- [ ] **Image proxy tests:** Valid filename → blob bytes streamed, path traversal attempt (`../../etc/passwd`) → 400
- [ ] **Rate limiting tests:** Exceed rate limit on /chat → 429
- [ ] **CORS tests:** Allowed origin → headers present, disallowed origin → blocked
- [ ] **Integration tests:** Full pipeline with real Cosmos + mocked Orchestrator

### "Done" Checklist
- [ ] JWT validation works with Microsoft.Identity.Web
- [ ] CRM proxy endpoints return data for authenticated users
- [ ] Chat endpoint: save → orchestrate → save → return cycle works
- [ ] Image proxy validates filenames and streams blob bytes
- [ ] Rate limiting on /api/v1/chat (e.g., 10 requests/minute per user)
- [ ] CORS configured for Blazor UI origin
- [ ] Conversation persistence in Cosmos DB (agents account)
- [ ] Correlation ID middleware (X-Correlation-ID header)
- [ ] Health checks, tests
- [ ] Added to solution file under `Frontend` folder

### Containerization & Deployment

**Step A: Dockerfile**
- [ ] Create `src/bff-api/Dockerfile` using `infra/templates/Dockerfile.template` as base
- [ ] Customize build args for `src/bff-api/bff-api.csproj` project path
- [ ] Expose port 8080
- [ ] Verify: `docker build -t bff-api:dev src/bff-api/`
- [ ] Verify: `docker run --rm -p 8080:8080 bff-api:dev` → `curl http://localhost:8080/health` returns 200

**Step B: Helm Chart**
- [ ] Create `src/bff-api/chart/` using `infra/templates/helm-base/` as base
- [ ] Customize `Chart.yaml` (name: `bff-api`, description: "Backend-for-Frontend API — auth gateway, chat orchestration, and data proxy")
- [ ] Customize `values.yaml`:
  - Image repository/tag for bff-api
  - Environment variables: `CosmosDb:AgentsEndpoint`, `CosmosDb:AgentsDatabase`, `Storage:ImagesEndpoint`, `Storage:ImagesContainer`, `CrmApi:BaseUrl`, `Orchestrator:BaseUrl`, `AzureAd:ClientId`, `AzureAd:TenantId`, `Bff:Hostname`
  - Resource limits: 256Mi memory request / 512Mi limit, 200m CPU request / 500m limit (gateway — handles auth, proxying, and SignalR connections)
  - Service account name matching Terraform-provisioned SA for Cosmos DB + Blob Storage access
  - Liveness probe: `/health`, Readiness probe: `/ready`
  - **Ingress configuration**: Enable Application Gateway for Containers (AGC) ingress with TLS termination, public hostname, and path-based routing
  - CORS origin allowlist for Blazor UI domain
- [ ] Add all service URLs and auth config to `templates/configmap.yaml`
- [ ] Add ingress resource to `templates/ingress.yaml` with AGC annotations
- [ ] Verify: `helm lint src/bff-api/chart/`
- [ ] Verify: `helm template bff-api src/bff-api/chart/` renders valid YAML

---

## Component 8: Blazor UI

> **The user's window into the system.** A Blazor WebAssembly SPA with MudBlazor components, MSAL authentication, and a chat panel that renders agent markdown responses with product images.

### What It Does
A single-page application where customers log in (MSAL/Entra ID), view their orders/profile, and chat with AI agents. The chat panel renders markdown responses (via Markdig), rewrites image URLs to BFF proxy paths, and supports streaming via SignalR.

### Key Files/Folders
```
src/blazor-ui/
├── blazor-ui.csproj
├── Program.cs                    # WebAssembly host, MSAL, HttpClient, services
├── wwwroot/
│   ├── index.html
│   └── css/
├── Layout/
│   ├── MainLayout.razor          # MudBlazor layout (NavMenu, AppBar)
│   └── NavMenu.razor
├── Pages/
│   ├── Home.razor                # Landing / dashboard
│   ├── Chat.razor                # Chat panel (main feature)
│   ├── Orders.razor              # Customer's order history
│   └── Profile.razor             # Customer profile view
├── Components/
│   ├── ChatMessage.razor         # Renders single message (markdown → HTML, image rewrite)
│   ├── ChatInput.razor           # Message input box
│   └── OrderCard.razor           # Order summary card
├── Services/
│   ├── ChatService.cs            # HTTP calls to BFF /api/v1/chat
│   ├── CrmService.cs             # HTTP calls to BFF CRM proxy endpoints
│   └── ConversationState.cs      # Scoped state management
├── Auth/
│   └── MsalConfig.cs             # MSAL configuration (PKCE, scopes)
├── Dockerfile                    # ⚠️ Special: nginx or dotnet static files
└── chart/
    ├── Chart.yaml
    ├── values.yaml
    └── templates/
```

### External Dependencies
| Dependency | NuGet Package | Version |
|------------|---------------|---------|
| Blazor WASM | `Microsoft.AspNetCore.Components.WebAssembly` | (built-in) |
| MudBlazor | `MudBlazor` | latest |
| MSAL | `Microsoft.Authentication.WebAssembly.Msal` | latest |
| Markdown | `Markdig` | latest |
| SignalR Client | `Microsoft.AspNetCore.SignalR.Client` | latest |

### Azure Services
- **BFF API** (HTTP — all backend access goes through BFF)

### Dependencies
- **Depends on:** BFF API (Component 7)
- **Unblocks:** Nothing — this is the top of the dependency chain

### Estimated Complexity: **Medium-High**
Blazor WASM with MSAL auth, MudBlazor theming, markdown rendering with custom image URL rewriting, state management, and SignalR streaming. Different Dockerfile pattern (nginx or static file server).

### Configuration
```
BFF API base URL → configured in Program.cs HttpClient
MSAL Client ID, Authority → wwwroot/appsettings.json
```

### Testing Strategy
- [ ] **bUnit tests:** ChatMessage renders markdown correctly, image URLs are rewritten to `/api/v1/images/{filename}`
- [ ] **Component tests:** ChatInput sends messages, Chat.razor displays conversation history
- [ ] **Auth tests:** Unauthenticated user → redirect to login, authenticated → sees chat panel
- [ ] **Manual E2E:** Log in → send each of the 8 scenario messages → verify responses render correctly with images

### "Done" Checklist
- [ ] MSAL authentication works (login, token acquisition, logout)
- [ ] Chat panel sends messages to BFF and renders responses
- [ ] Markdown rendering with image URL rewriting (`filename.png` → `/api/v1/images/filename.png`)
- [ ] Order history page shows customer's orders
- [ ] MudBlazor themed (responsive, accessible)
- [ ] State management via scoped services (ConversationState)
- [ ] bUnit tests pass
- [ ] Added to solution file under `Frontend` folder

### Containerization & Deployment

**Step A: Dockerfile**
- [ ] Create `src/blazor-ui/Dockerfile` — ⚠️ **Special: multi-stage build with nginx**
  - Stage 1: .NET SDK to publish Blazor WASM (`dotnet publish -c Release`)
  - Stage 2: `nginx:alpine` to serve the static `wwwroot/_framework/` output
- [ ] Create `src/blazor-ui/nginx.conf` with:
  - SPA fallback routing (`try_files $uri $uri/ /index.html`)
  - Gzip compression for `.wasm`, `.dll`, `.js`, `.css` assets
  - Cache-Control headers for framework files (immutable, long-lived)
  - Security headers (X-Content-Type-Options, X-Frame-Options, CSP)
- [ ] Expose port 80 (nginx default)
- [ ] Verify: `docker build -t blazor-ui:dev src/blazor-ui/`
- [ ] Verify: `docker run --rm -p 8080:80 blazor-ui:dev` → browser at `http://localhost:8080` loads the Blazor app shell

**Step B: Helm Chart**
- [ ] Create `src/blazor-ui/chart/` using `infra/templates/helm-base/` as base
- [ ] Customize `Chart.yaml` (name: `blazor-ui`, description: "Contoso Outdoors Blazor WebAssembly UI")
- [ ] Customize `values.yaml`:
  - Image repository/tag for blazor-ui
  - Container port: 80 (nginx), not 8080
  - Resource limits: 64Mi memory request / 128Mi limit, 25m CPU request / 100m limit (static file server — minimal profile)
  - **No service account needed** (UI has no Azure SDK calls — all backend access goes through BFF)
  - Liveness probe: HTTP GET `/` on port 80
  - Readiness probe: HTTP GET `/` on port 80
  - **Ingress configuration**: Enable AGC ingress with TLS, public hostname, and path-based routing (serve UI at `/`, route `/api/` to BFF)
- [ ] Add nginx config as a ConfigMap in `templates/configmap.yaml`
- [ ] Add ingress resource to `templates/ingress.yaml` with AGC annotations
- [ ] Verify: `helm lint src/blazor-ui/chart/`
- [ ] Verify: `helm template blazor-ui src/blazor-ui/chart/` renders valid YAML

---

## Cross-Cutting Concerns

These are addressed alongside component implementation, not as separate phases.

### Correlation IDs / Distributed Tracing
- **When:** Implement with BFF API (Component 7), propagate backward
- **How:** BFF generates `X-Correlation-ID` if not present, passes it to Orchestrator → Agents → MCP servers → CRM API as an HTTP header. All services include it in structured log output.
- **Pattern:** Custom middleware that reads/generates the header and stores it in `IHttpContextAccessor` or `Activity.Current`
- **Future:** OpenTelemetry SDK integration when the team is ready (not blocking for MVP)

### CORS Policy
- **When:** Implement with BFF API (Component 7)
- **How:** `builder.Services.AddCors()` with Blazor UI origin allowed. In dev: `localhost:5001` (UI) → `localhost:5000` (BFF). In AKS: both behind the same AGC ingress (CORS may not be needed if same-origin).
- **Pattern:** Named CORS policy, applied via `app.UseCors("BlazorUI")`

### Rate Limiting
- **When:** Implement with BFF API (Component 7)
- **How:** .NET 9 built-in `System.Threading.RateLimiting` on the `/api/v1/chat` endpoint. Fixed window: 10 requests/minute per authenticated user.
- **Purpose:** Prevent Azure OpenAI cost spikes from aggressive callers

### Resilience (Polly)
- **When:** Implement with CRM MCP (first service making outbound HTTP calls), carry forward to all
- **How:** `Microsoft.Extensions.Http.Resilience` with `AddStandardResilienceHandler()` on all `HttpClient` registrations
- **Policy:** Retry (3 attempts, exponential backoff), circuit breaker (5 failures → 30s break), timeout (30s per request)

---

## Implementation Summary Table

| # | Component | Owner | Depends On | Unblocks | Complexity | Est. Files |
|---|-----------|-------|-----------|----------|------------|-----------|
| 0 | Pre-implementation tasks | Team | — | Everything | Medium | 5-10 |
| 1 | CRM API | Brian | Nothing | 2, 7 | Medium-High | ~20 |
| 2 | CRM MCP | Brian | 1 | 4, 5 | Simple-Medium | ~12 |
| 3 | Knowledge MCP | Brian | — (AI Search) | 4, 5 | Simple | ~8 |
| 4 | CRM Agent | Lois | 2, 3 | 6 | Complex | ~12 |
| 5 | Product Agent | Lois | 2, 3 | 6 | Complex | ~12 |
| 6 | Orchestrator Agent | Lois | 4, 5 | 7 | Medium | ~12 |
| 7 | BFF API | Peter | 1, 6 | 8 | Complex | ~25 |
| 8 | Blazor UI | Peter | 7 | — | Medium-High | ~20 |

### Parallelization Opportunities
- **Components 2 and 3** can be built in parallel (CRM MCP + Knowledge MCP have no mutual dependency)
- **Components 4 and 5** can be built in parallel once 2 and 3 are done
- **Component 3** can actually start immediately in parallel with Component 1 (it only depends on AI Search, not CRM API)

### Optimal Build Order With Parallelization
```
Week 1:  [1: CRM API] + [3: Knowledge MCP]          ← parallel
Week 2:  [2: CRM MCP]                                ← needs CRM API
Week 3:  [4: CRM Agent] + [5: Product Agent]         ← parallel, need 2+3
Week 4:  [6: Orchestrator Agent]                      ← needs 4+5
Week 5:  [7: BFF API]                                 ← needs 1+6
Week 6:  [8: Blazor UI]                               ← needs 7
```

---

## Per-Component Universal Checklist

Every component must satisfy ALL of these before moving on:

```markdown
### Component: {name}
- [ ] Code compiles with zero warnings (TreatWarningsAsErrors=true)
- [ ] All endpoints/tools work as specified
- [ ] /health endpoint returns 200
- [ ] /ready endpoint checks all dependencies
- [ ] ProblemDetails on all error paths
- [ ] Structured JSON logging configured
- [ ] Dockerfile builds successfully from repo root
- [ ] Helm chart passes `helm lint` and `helm template`
- [ ] Security context: runAsNonRoot, readOnlyRootFilesystem, drop ALL caps
- [ ] Unit tests exist and pass
- [ ] Integration tests exist and pass
- [ ] Project added to .sln under correct solution folder
- [ ] appsettings.json config keys documented
- [ ] README.md in the component folder (what it does, how to run, endpoints)
```

---

## Verification Milestones

After each tier, run the corresponding business scenarios to verify the whole stack works:

| Milestone | Components Ready | What You Can Verify |
|-----------|-----------------|---------------------|
| **M1: Data Layer** | CRM API | All 11 endpoints return correct data matching CSVs |
| **M2: Tool Layer** | + CRM MCP, Knowledge MCP | MCP tools return data; knowledge search finds documents |
| **M3: Agent Layer** | + CRM Agent, Product Agent | Agents answer scenario questions correctly with tool usage |
| **M4: Routing** | + Orchestrator | All 8 scenarios route to correct specialist and return correct answers |
| **M5: Gateway** | + BFF API | Full chat flow with auth, persistence, image proxy |
| **M6: Complete** | + Blazor UI | End-to-end: login → chat → see responses with images |

---

That's the complete plan. Brian builds the data and protocol layers (Components 1-3), Lois builds the agents (4-6), and Peter builds the gateway and UI (7-8). Each component has a clear "done" definition, testing strategy, and dependency chain. Start with the Phase 0 pre-implementation tasks, then CRM API — nothing else can move without it.