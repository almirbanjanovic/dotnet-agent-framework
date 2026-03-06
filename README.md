# dotnet-agent-framework

.NET Agent Framework tutorials for building agentic AI systems, based on [Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/overview/?pivots=programming-language-csharp).

## Architecture

![Architecture Diagram](docs/architecture.png)

*Edit the source: [docs/architecture.drawio](docs/architecture.drawio) — open in [draw.io](https://app.diagrams.net)*

### Overview

This system is a .NET rewrite of the original Python prototype (`python-old/`). It implements a Contoso Telecom customer service platform where AI agents handle customer inquiries using structured CRM data and unstructured knowledge documents (via RAG). The architecture follows a **hybrid data access pattern** — shared REST APIs where multiple consumers exist, direct database access where only one consumer exists.

```text
                     ┌──────────────────────────────────────────────────────────────────────┐
                     │  AKS Cluster                                                        │
                     │                                                                      │
 User ──► Blazor UI ─┤── (chat) ──────► Orchestration Pods ◄── Agent Definitions (lib)      │
              │       │                   │             │                                    │
              │       │                   │             ▼                                    │
              │       │                   │        Azure OpenAI (chat completions)           │
              │       │                   ▼                                                  │
              │       │             MCP Servers                                              │
              │       │        ┌──────┬──────┬──────┬──────┐                                │
              │       │        │      │      │      │      │                                │
              │       │        ▼      ▼      ▼      ▼      ▼                                │
              │       │      CRM  Billing Product Security Knowledge                       │
              │       │      MCP   MCP    MCP    MCP    (RAG) MCP                           │
              │       │        │      │      │      │      │                                │
              │       │        ▼      ▼      ▼      ▼      └─► Cosmos DB Knowledge (direct) │
              │       │      CRM   Billing Product Security    + Embedding Model            │
              ▼       │      API    API    API    API                                       │
             BFF ─────┤──►    │      │      │      │                                        │
              │       │       └──────┴──────┴──────┘                                        │
              │       │                   │                                                  │
              │       └───────────────────┼──────────────────────────────────────────────────┘
              │                           │
              │                           ▼
              │                     Cosmos DB Operational (CRM data)
              │
              │          Cosmos DB Agents (state — written directly by orchestrator)
              │
              └── (direct data: tables, dashboards) ──► BFF ──► Domain APIs
```

### Architectural decisions

#### 1. Hybrid data access — REST APIs where shared, direct where exclusive

The system uses a **hybrid pattern** rather than forcing all traffic through REST APIs or allowing everything to access databases directly. The deciding principle: **use shared REST APIs when multiple consumers exist; go direct when only one consumer exists.**

| Data Store | UI needs it? | Agents need it? | Access pattern | Why |
|------------|:---:|:---:|---|---|
| **Operational** (CRM) | ✅ | ✅ | Shared domain APIs | Both UI (via BFF) and agents (via MCP → domain APIs) need the same data. Shared APIs prevent duplicating data access logic, validation, and business rules. |
| **Knowledge** (RAG vectors) | ❌ | ✅ | MCP → Cosmos DB direct | Only agents perform vector similarity search. This is a compound AI operation (embed query → `VectorDistance` search → return chunks) that no other consumer needs. Routing it through a REST API adds a pointless hop. |
| **Knowledge** (doc metadata) | Maybe | ❌ | Domain API endpoint | If an admin UI needs to browse/manage documents (titles, categories), that's simple CRUD — add a domain API endpoint. But vector search remains agent-only. |
| **Agents** (state) | ❌ | ✅ | Orchestrator → Cosmos DB direct | Internal conversation history and agent memory. No other consumer needs this. |

**Why not "everything through REST APIs"?** The Blazor UI needs direct data access for tables, dashboards, and admin views — not everything flows through an agent. If MCP Servers also access Cosmos DB directly for CRM data, we'd duplicate repositories, validation, and business rules in two places. But forcing the Knowledge MCP Server through a REST API for an operation only agents perform is over-engineering — adding a network hop and an extra service layer with zero consumers besides the MCP Server itself.

**The MCP specification is agnostic on this.** MCP tools can "[query databases, call APIs, or perform computations](https://modelcontextprotocol.io/specification/2025-03-26)" — the spec doesn't prescribe one pattern over the other. The official [MCP reference servers](https://github.com/modelcontextprotocol/servers) include both: `server-sqlite` (direct DB) and `server-github` (calls REST API).

#### 2. Domain-specific APIs with a BFF layer

The data layer is split into **four domain-specific APIs**, each owning its Cosmos DB containers and business logic. A **[Backend for Frontend (BFF)](https://learn.microsoft.com/en-us/azure/architecture/patterns/backends-for-frontends)** sits between the Blazor UI and the domain APIs, aggregating cross-domain calls into UI-optimized responses.

| Domain API | Cosmos Containers | Key Operations |
|---|---|---|
| **CRM API** | Customers, Subscriptions, DataUsage, SupportTickets, ServiceIncidents | Get/update customers, subscriptions, data usage, support tickets |
| **Billing API** | Invoices, Payments | Get invoices, payments, billing summary, pay invoice |
| **Product API** | Products, Promotions, Orders | Get products, promotions, eligibility, orders |
| **Security API** | SecurityLogs | Get security logs, unlock account |

All four share the **Operational** Cosmos DB account but own separate containers. Each API has its own clean architecture (Services → Repositories → Models).

**Why domain APIs instead of one monolith?** Each domain has distinct scaling, deployment, and security requirements. The Security API needs stricter [RBAC](https://learn.microsoft.com/en-us/azure/role-based-access-control/overview) than the Product API. Domain APIs can be deployed independently — a billing fix doesn't require redeploying the CRM service.

**Why a BFF?** The Blazor UI needs cross-domain views: a customer detail page shows customer info (CRM) + subscriptions (CRM) + outstanding balance (Billing) + recent orders (Product) on one screen. The BFF aggregates these into UI-optimized endpoints. Domain APIs know about business rules. The BFF knows about UI views. Neither duplicates the other's concerns.

**MCP servers don't use the BFF.** Each MCP server calls its corresponding domain API directly — it's already scoped to a single domain. The BFF exists solely because the UI needs cross-domain aggregation.

**Cross-domain MCP operations:** Some tools need data from multiple domains (e.g., `get_eligible_promotions` needs the customer's loyalty tier from CRM). The domain API handles this via API-to-API calls internally — the promotion eligibility logic belongs in the Product API, not in the MCP adapter.

#### 3. MCP Servers are thin protocol adapters

Each MCP Server exposes [tools](https://modelcontextprotocol.io/docs/concepts/tools) with names, descriptions, and schemas that the LLM uses for function calling. The CRM/Billing/Product/Security MCP Servers translate between MCP protocol and HTTP calls to their corresponding domain APIs. The Knowledge MCP Server translates between MCP protocol and Cosmos DB + Embedding Model calls. None of them contain business logic — that lives in the domain APIs (for shared data) or in the MCP tool handler itself (for the RAG compound operation).

#### 4. Five domain-specific MCP Servers

Tools are grouped by domain boundary, with each MCP server calling exactly one domain API:

| MCP Server | Tools | Calls |
|---|---|---|
| **CRM** | `get_all_customers`, `get_customer_detail`, `get_subscription_detail`, `get_data_usage`, `update_subscription`, `get_support_tickets`, `create_support_ticket` | CRM API |
| **Billing** | `get_billing_summary`, `get_invoice_payments`, `pay_invoice` | Billing API |
| **Product** | `get_products`, `get_product_detail`, `get_promotions`, `get_eligible_promotions`, `get_customer_orders` | Product API |
| **Security** | `get_security_logs`, `unlock_account` | Security API |
| **Knowledge (RAG)** | `search_knowledge_base` | Knowledge Cosmos DB direct + Embedding Model |

This 1:1 alignment between MCP servers and domain APIs keeps each adapter simple and independently deployable.

#### 5. Agent definitions are a shared library, not separate deployments

In [Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/agents/), an agent is an **in-process object** — a configuration of (LLM client + system prompt + tools). Creating an agent is a few lines of code:

```csharp
AIAgent agent = new AzureOpenAIClient(...)
    .GetChatClient("gpt-4.1")
    .AsAIAgent(
        instructions: "You are a CRM specialist...",
        tools: [.. crmTools]);
```

Agents are not deployed as separate pods. They are instantiated inside whichever orchestration needs them. A `CrmAgent` factory method returns the same agent configuration whether it's used in a single-agent scenario, a handoff pattern, or a magentic group.

```text
src/
  agents/                          ← SHARED CLASS LIBRARY (not a pod)
    AgentDefinitions.cs
    CrmAgent                          → system prompt + CRM MCP tools
    BillingAgent                      → system prompt + Billing MCP tools
    ProductAgent                      → system prompt + Product MCP tools
    SecurityAgent                     → system prompt + Security MCP tools
    KnowledgeAgent                    → system prompt + Knowledge MCP tools
    ReviewerAgent                     → system prompt + quality review prompt
    ManagerAgent                      → system prompt + no tools (orchestrates only)

  orchestrations/                  ← DEPLOYABLE PODS (each references shared library)
    single-agent/                  → Creates 1 agent with ALL tools, exposes HTTP endpoint
    reflection/                    → Creates Primary + Reviewer in-process, exposes endpoint
    handoff/                       → Creates intent classifier + specialists, exposes endpoint
    magentic/                      → Creates Manager + specialists, exposes endpoint
```

This means:

- Agent configurations are **defined once, reused across patterns** — no duplication.
- Each orchestration pod references the shared library, instantiates the agents it needs, and composes them using the appropriate workflow pattern.
- Adding a new pattern means adding a new orchestration pod, not redefining agents.

#### 6. Agent orchestrator owns agent state

Each orchestration pod writes directly to the **Agents** Cosmos DB account for conversation history and agent memory. This state is owned by the orchestration layer — it doesn't belong in the shared REST API surface because no other consumer needs it.

#### 7. APIM as an optional external gateway

For external access (partner integrations, multi-tenant scenarios), [Azure API Management](https://learn.microsoft.com/en-us/azure/api-management/api-management-key-concepts) sits in front of the MCP Servers — handling JWT validation, tenant routing, and rate limiting. Internal traffic within AKS bypasses APIM. See `python-old/mcp/apim_inbound_policy.xml` for the reference policy.

### Workflow orchestration patterns

The same agent definitions can be composed into different orchestration patterns. [Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/overview/?pivots=programming-language-csharp) supports both autonomous [agents](https://learn.microsoft.com/en-us/agent-framework/agents/) (LLM-driven steps) and explicit [workflows](https://learn.microsoft.com/en-us/agent-framework/workflows/) (developer-defined execution paths). Each pattern reuses the same MCP tools and domain APIs — the difference is how agents are composed and coordinated.

Each orchestration pattern is deployed as a separate pod in AKS with its own HTTP endpoint. The Blazor UI routes to the appropriate endpoint based on user selection.

#### Single agent

One agent with access to all MCP tools handles the entire conversation.

```text
User ↔ Agent (all tools) ↔ LLM
```

- **Agents used:** 1 (all tools via all MCP servers)
- **When to use:** Simple Q&A, single-domain tasks, prototyping
- **LLM calls per turn:** 1
- **Docs:** [Agent Framework — Agents](https://learn.microsoft.com/en-us/agent-framework/agents/)

#### Sequential

Agents process tasks one after another. The output of one agent becomes the input of the next.

```text
User → Agent A → Agent B → Agent C → Result
```

- **Agents used:** N (each with domain-specific tools)
- **When to use:** Multi-step processing, data enrichment, review chains
- **LLM calls per turn:** N (one per agent)
- **Docs:** [Agent Framework — Workflows](https://learn.microsoft.com/en-us/agent-framework/workflows/)

#### Reflection

A primary agent generates a response, then a reviewer agent evaluates quality. If the reviewer rejects, the primary refines. Loops up to a configurable maximum.

```text
User → Primary Agent → Reviewer → [APPROVE or refine] → Response
```

- **Agents used:** Primary (domain tools) + Reviewer (no tools, quality prompt)
- **When to use:** Quality assurance, compliance checking, high-stakes responses
- **LLM calls per turn:** 2–4 (depends on refinement rounds)
- **Docs:** [Agent Framework — Workflows](https://learn.microsoft.com/en-us/agent-framework/workflows/)

#### Concurrent (fan-out / fan-in)

Multiple agents process the same input in parallel. Results are aggregated.

```text
User → [Agent A, Agent B, Agent C] → Aggregator → Result
```

- **Agents used:** N specialist agents + 1 aggregator
- **When to use:** Multi-perspective analysis, parallel research, consensus
- **LLM calls per turn:** N (parallel) + 1 (aggregator)
- **Docs:** [Agent Framework — Workflows](https://learn.microsoft.com/en-us/agent-framework/workflows/)

#### Handoff

An intent classifier routes the conversation to a domain specialist. Specialists communicate directly with the user. When a specialist detects an out-of-domain request, it triggers a handoff to another specialist. Classification is lazy — runs only on first message or handoff detection.

```text
User ↔ Intent Classifier → Specialist A ↔ User
                          → Specialist B ↔ User (on handoff)
```

- **Agents used:** CrmBillingAgent, ProductPromotionsAgent, SecurityAgent (from shared library)
- **When to use:** Multi-domain customer service, clear domain boundaries
- **LLM calls per turn:** 1–2 (specialist + optional reclassification)
- **Docs:** [.NET AI — Agents (Handoff)](https://learn.microsoft.com/en-us/dotnet/ai/conceptual/agents)

#### Magentic (group chat with orchestrator)

A manager agent coordinates specialist agents. Specialists only communicate with the manager — never directly with the user. The manager plans, delegates to specialists, synthesizes responses, and delivers the final answer.

```text
User ↔ Manager → Specialist A
               → Specialist B → Manager → Response
               → Specialist C
```

- **Agents used:** ManagerAgent + CrmBillingAgent, ProductPromotionsAgent, SecurityAgent (from shared library)
- **When to use:** Complex multi-domain queries requiring synthesis and planning
- **LLM calls per turn:** 3–10+ (manager + specialists + replanning)
- **Docs:** [.NET AI — Agents (Magentic)](https://learn.microsoft.com/en-us/dotnet/ai/conceptual/agents)

#### Choosing a pattern

| Consideration | Single | Sequential | Reflection | Concurrent | Handoff | Magentic |
|:---|:---:|:---:|:---:|:---:|:---:|:---:|
| Complexity | Low | Medium | Medium | Medium | Medium | High |
| Latency | Low | Medium | Medium | Low | Low | High |
| LLM cost per turn | Low | Medium | Medium | Medium | Low | High |
| Quality control | — | — | ✅ | — | — | ✅ |
| Multi-domain | — | — | — | — | ✅ | ✅ |
| Parallelism | — | — | — | ✅ | — | — |

### AKS pod summary

| Pod | Type | Connects to |
|---|---|---|
| **Blazor UI** | Frontend | BFF (data views), Orchestration pods (chat) |
| **BFF** | Aggregation | CRM API, Billing API, Product API, Security API |
| **CRM API** | Domain API | Operational Cosmos DB (Customers, Subscriptions, etc.) |
| **Billing API** | Domain API | Operational Cosmos DB (Invoices, Payments) |
| **Product API** | Domain API | Operational Cosmos DB (Products, Promotions, Orders) |
| **Security API** | Domain API | Operational Cosmos DB (SecurityLogs) |
| **MCP: CRM** | Tool server | CRM API |
| **MCP: Billing** | Tool server | Billing API |
| **MCP: Product** | Tool server | Product API |
| **MCP: Security** | Tool server | Security API |
| **MCP: Knowledge (RAG)** | Tool server | Knowledge Cosmos DB + Embedding Model (direct) |
| **Orch: Single Agent** | Agent pattern | All MCP servers, Azure OpenAI, Agents Cosmos DB |
| **Orch: Reflection** | Agent pattern | All MCP servers, Azure OpenAI, Agents Cosmos DB |
| **Orch: Handoff** | Agent pattern | All MCP servers, Azure OpenAI, Agents Cosmos DB |
| **Orch: Magentic** | Agent pattern | All MCP servers, Azure OpenAI, Agents Cosmos DB |

### Data flow

#### Structured data (CRM → Operational Cosmos DB)

CSV files in `data/contoso-crm/` are parsed by the seed tool and upserted into the **Operational** Cosmos DB account. No vectorization. Agents query this data via MCP tools → domain APIs → Cosmos DB SQL queries. The Blazor UI queries the same data via BFF → domain APIs for tables and dashboards.

#### Unstructured data (SharePoint → Knowledge Cosmos DB)

PDF documents in `data/contoso-sharepoint/` are processed by the seed tool: text extraction → chunking → embedding via `text-embedding-ada-002` → upserted into the **Knowledge** Cosmos DB account with vector indexing (diskANN, cosine distance, 1536 dimensions). Agents search this via the Knowledge MCP Server which performs the compound operation: embed user query → `VectorDistance` search → return relevant chunks for the LLM to ground its response (RAG pattern).

See [data/README.md](data/README.md) for the complete data architecture, seeding process, and Cosmos DB container mapping.

### Azure infrastructure

All infrastructure is defined as Terraform IaC in `infra/terraform/`, deployed via GitHub Actions or locally.

| Resource | Purpose |
|----------|---------|
| **Azure AI Foundry** | Hosts AI Services account with chat model (gpt-4.1) and embedding model (text-embedding-ada-002) |
| **Cosmos DB** (×3 accounts) | Operational (Session consistency, CRM data), Knowledge (Eventual + vector search, RAG), Agents (Eventual, agent state) |
| **AKS** | Hosts all application pods — UI, BFF, domain APIs, MCP servers, orchestrations |
| **ACR** | Container image registry |
| **Key Vault** | Secrets management (endpoints, keys, deployment names) |
| **Managed Identities** | RBAC for backend and kubelet workloads |

See [infra/README.md](infra/README.md) for setup instructions, Terraform module structure, and CI/CD configuration.

### Technology references

| Topic | Link |
|-------|------|
| Microsoft Agent Framework | [Overview](https://learn.microsoft.com/en-us/agent-framework/overview/?pivots=programming-language-csharp) |
| Agent Framework — Agents | [Agent types](https://learn.microsoft.com/en-us/agent-framework/agents/) |
| Agent Framework — Tools | [Tools overview](https://learn.microsoft.com/en-us/agent-framework/agents/tools/) |
| Agent Framework — Function tools | [Function tools](https://learn.microsoft.com/en-us/agent-framework/agents/tools/function-tools) |
| Agent Framework — MCP integration | [Using MCP tools with agents](https://learn.microsoft.com/en-us/agent-framework/agents/tools/local-mcp-tools) |
| Agent Framework — Workflows | [Workflows overview](https://learn.microsoft.com/en-us/agent-framework/workflows/) |
| .NET AI concepts — Agents | [What are agents?](https://learn.microsoft.com/en-us/dotnet/ai/conceptual/agents) |
| Model Context Protocol — Architecture | [Architecture overview](https://modelcontextprotocol.io/docs/concepts/architecture) |
| MCP specification | [2025-03-26](https://modelcontextprotocol.io/specification/2025-03-26) |
| MCP — Tools | [Server tools](https://modelcontextprotocol.io/specification/2025-06-18/server/tools) |
| MCP C# SDK | [GitHub](https://github.com/modelcontextprotocol/csharp-sdk) |
| Backend for Frontend (BFF) | [BFF pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/backends-for-frontends) |
| .NET clean architecture | [Common web app architectures](https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures#clean-architecture) |
| Azure Cosmos DB vector search | [Vector search overview](https://learn.microsoft.com/en-us/azure/cosmos-db/nosql/vector-search) |
| Azure API Management | [Overview](https://learn.microsoft.com/en-us/azure/api-management/api-management-key-concepts) |
| Terraform AzureRM provider | [Registry](https://registry.terraform.io/providers/hashicorp/azurerm/latest) |

## Repository structure

```
.github/workflows/                → CI/CD (plan, apply, backend bootstrap)

data/
  README.md                       → Data architecture and seeding guide
  contoso-crm/                    → Simulated CRM export (CSV)
  contoso-sharepoint/             → Simulated SharePoint docs (TXT + PDF)

docs/
  architecture.drawio             → Editable architecture diagram (draw.io)
  architecture.png                → Rendered architecture diagram

infra/
  README.md                       → Infrastructure setup guide
  init-backend.ps1                → Bootstrap Terraform backend (PowerShell)
  init-backend.sh                 → Bootstrap Terraform backend (Bash)
  terraform/                      → Terraform IaC (modular, versioned)

src/
  README.md                       → Lab setup and run guide
  appsettings.json                → Shared app settings (gitignored, populated by config-sync)
  config-sync/                    → Tool: pulls Key Vault secrets into appsettings.json
  simple-agent/                   → Lab 1: validate infrastructure setup
  seed-data/                      → Lab 2: seed Cosmos DB with CRM + vectorized docs (RAG)
  agents/                         → Shared agent definitions library
  apis/
    crm-api/                      → CRM domain API
    billing-api/                  → Billing domain API
    product-api/                  → Product domain API
    security-api/                 → Security domain API
    bff/                          → Backend for Frontend (UI aggregation)
    shared/                       → Shared models and interfaces
  mcp-servers/
    mcp-crm/                      → MCP → CRM API
    mcp-billing/                  → MCP → Billing API
    mcp-product/                  → MCP → Product API
    mcp-security/                 → MCP → Security API
    mcp-knowledge/                → MCP → Cosmos DB Knowledge + Embedding (direct)
  orchestrations/
    single-agent/                 → Single agent orchestration
    reflection/                   → Reflection orchestration
    handoff/                      → Handoff orchestration
    magentic/                     → Magentic orchestration
  blazor-ui/                      → Blazor front end
```

## Prerequisites

- [.NET SDK 9.0+](https://dotnet.microsoft.com/download)
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) (`az login` completed)
- [Terraform >= 1.14.6](https://developer.hashicorp.com/terraform/install)

## Getting started

### 1. Deploy infrastructure

Infrastructure must be provisioned before running any labs. Follow the full setup instructions in **[infra/README.md](infra/README.md)** — this covers backend bootstrap, Terraform configuration, and deployment.

### 2. Configure agentic app settings

After infrastructure is deployed, sync secrets from Key Vault to your local config. Follow the instructions in **[src/README.md](src/README.md)**.

## Notes

- Provider versions are pinned in `infra/terraform/providers.tf`.
- `terraform.tfvars`, `backend.hcl`, and `appsettings.json` are gitignored.
- This project is a .NET rewrite of the Python prototype in `python-old/`. The original code serves as a reference for agent patterns, MCP tool definitions, and data architecture but is not used at runtime.
