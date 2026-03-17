# dotnet-agent-framework

.NET Agent Framework for building agentic AI systems with Contoso Outdoors, inspired by [Microsoft Agentic AI Workshop](https://github.com/microsoft/OpenAIWorkshop).

## Architecture

![Architecture Diagram](docs/architecture.png)

*Edit the source: [docs/architecture.drawio](docs/architecture.drawio) — open in [draw.io](https://app.diagrams.net)*

### Components — 8 containers, each independently deployable

| Component | Type | What it does | Calls | Identity |
| --- | --- | --- | --- | --- |
| **Blazor WASM UI** | SPA (.NET, MudBlazor) | User interface, MSAL auth, chat panel, SignalR streaming | BFF API (HTTP + SignalR) | *(none)* |
| **BFF API** | .NET Minimal API | JWT validation, CRM API proxy, image proxy (blob bytes), chat, conversation persistence | CRM API, Orchestrator, Blob Storage, Cosmos DB | `id-bff` |
| **CRM API** | Domain API | All SQL data: customers, orders, products, promotions, support tickets (11 endpoints) | Azure SQL | `id-crm-api` |
| **CRM MCP** | MCP Server | 10 tools wrapping all CRM API endpoints | CRM API (HTTP) | `id-crm-mcp` |
| **Knowledge MCP** | MCP Server | 1 tool: `search_knowledge_base` | AI Search (SDK direct) | `id-know-mcp` |
| **CRM Agent** | Agent | CRM specialist: customers, orders, billing, tickets, policies | CRM MCP + Knowledge MCP, Azure OpenAI | `id-crm-agt` |
| **Product Agent** | Agent | Product specialist: catalog, promotions, recommendations, guides | CRM MCP + Knowledge MCP, Azure OpenAI | `id-prod-agt` |
| **Orchestrator Agent** | Agent | Intent classification, routes to CRM or Product agent | CRM/Product Agent (HTTP), Azure OpenAI | `id-orch` |

Each component is fully independent — own models, own Dockerfile, own Helm chart, own test project. No shared project references. Communication between services is HTTP/JSON only.

### Key architectural decisions

#### 1. Each agent is its own container with its own identity

Each agent runs in its own container with its own managed identity and least-privilege RBAC. This provides blast radius isolation, independent scaling, independent deployment, and clear auditability in Azure activity logs.

#### 2. One SQL database → one CRM Domain API

All six SQL tables (Customers, Orders, OrderItems, Products, Promotions, SupportTickets) are served by a single CRM API. Both the BFF (via HTTP proxy) and agents (via CRM MCP tools) consume the same endpoints. The CRM API earns its existence — 11 endpoints with JOINs, filtering, write operations, and identity-scoped authorization.

#### 3. MCP Servers are thin protocol adapters

Each [MCP Server](https://modelcontextprotocol.io/docs/concepts/tools) translates between the MCP protocol and its backend. The CRM MCP wraps CRM API endpoints as tools. The Knowledge MCP calls Azure AI Search directly — no wrapper API needed for a single SDK call. Agents discover tools dynamically at runtime via the [ModelContextProtocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk).

| MCP Server | Tools | Backend |
| --- | --- | --- |
| **CRM MCP** | `get_all_customers`, `get_customer_detail`, `get_customer_orders`, `get_order_detail`, `get_products`, `get_product_detail`, `get_promotions`, `get_eligible_promotions`, `get_support_tickets`, `create_support_ticket` | CRM API (HTTP) |
| **Knowledge MCP** | `search_knowledge_base` | Azure AI Search (SDK direct) |

#### 4. BFF owns conversation persistence; agents are stateless

The BFF is the sole writer/reader for conversation history in Cosmos DB. The Orchestrator and specialist agents are stateless — they receive conversation history in the request body from the BFF. This keeps agents simple, scalable, and independently replaceable.

#### 5. Blazor WASM UI + separate BFF API

The UI is a Blazor WebAssembly SPA with MudBlazor components, deployed as static files in its own container. The BFF is a .NET Minimal API in a separate container. Blazor WASM authenticates via Microsoft.Authentication.Msal (PKCE) and sends Bearer tokens to the BFF. Both UI and backend use C# — the entire stack is .NET.

#### 6. BFF proxies product images (most secure)

The browser never gets a direct Blob Storage URL. Image requests go through the BFF, which validates the filename (prevents path traversal), checks authentication, fetches blob bytes, and streams them to the browser. This is extensible to private per-customer images (e.g., damage claim photos) in the future.

#### 7. Agents render images via markdown

Agents get `imageFilename` from the `get_product_detail` tool. They include it as markdown: `![TrailBlazer](trailblazer-hiking-boots.png)`. The Blazor `ChatMessage` component renders markdown (via Markdig) and rewrites image src to `/api/images/{filename}`, which the BFF proxies from Blob Storage.

### Traffic paths

```text
Path 1 — Direct data (no agent):
  Browser → Blazor WASM UI → BFF API → CRM API → Azure SQL

Path 2 — Agent chat:
  Browser → Blazor WASM UI → BFF API → save user msg to Cosmos
    → Orchestrator Agent (with history) → classify intent
      → CRM Agent → CRM MCP tools → CRM API → SQL
                   → Knowledge MCP tools → AI Search
      → response back to Orchestrator → BFF
    → save assistant msg to Cosmos → render in ChatPanel

Path 3 — Product image:
  Browser → BFF /api/images/{filename} → validate → Blob Storage → stream bytes
```

### AKS deployment summary

| Container | Service Type | Identity | Key Connections |
| --- | --- | --- | --- |
| blazor-ui | Ingress (public, path: /) | *(none)* | BFF API (HTTP + SignalR) |
| bff-api | Ingress (path: /api/*, /hubs/*) | `id-bff` | CRM API, Orchestrator, Blob Storage, Cosmos DB |
| crm-api | ClusterIP | `id-crm-api` | Azure SQL |
| crm-mcp | ClusterIP | `id-crm-mcp` | CRM API |
| knowledge-mcp | ClusterIP | `id-know-mcp` | AI Search |
| crm-agent | ClusterIP | `id-crm-agt` | CRM MCP, Knowledge MCP, Azure OpenAI |
| product-agent | ClusterIP | `id-prod-agt` | CRM MCP, Knowledge MCP, Azure OpenAI |
| orchestrator-agent | ClusterIP | `id-orch` | CRM Agent, Product Agent, Azure OpenAI |

### Data flow

#### Structured data (CRM → Azure SQL Database)

CSV files in `data/contoso-crm/` are parsed by the seed tool and upserted into **Azure SQL Database** tables with proper relational modeling (joins, foreign keys). Agents query this data via MCP tools → CRM API → SQL queries. The Blazor WASM UI queries the same data via BFF API → CRM API.

#### Unstructured data (SharePoint → Azure AI Search)

PDF documents in `data/contoso-sharepoint/` are uploaded to Azure Blob Storage by Terraform. The AI Search indexer processes them via integrated vectorization: text extraction → chunking → embedding via `text-embedding-ada-002` → indexed for semantic search. Event Grid triggers the indexer on new blob uploads via a Logic App intermediary (Event Grid can't send auth headers to the Search API directly). Agents search via the Knowledge MCP Server which calls the Azure AI Search SDK directly.

#### Product images (Azure Blob Storage)

Product images in `data/contoso-images/` are uploaded to a private `product-images` blob container during Terraform apply. The BFF proxies image bytes to the browser — no direct storage URL is exposed. Agents reference product images by including the `imageFilename` from product data in their markdown responses.

### Azure infrastructure

| Resource | Purpose |
| ---------- | --------- |
| **Azure AI Foundry** | AI Services account with chat model (gpt-4.1) and embedding model (text-embedding-ada-002) |
| **Azure SQL Database** | Operational CRM data (Serverless tier) — 6 tables |
| **Cosmos DB** | Conversation history + agent session state (Eventual consistency) |
| **Azure AI Search** | Knowledge base — indexes PDFs via integrated vectorization (Basic tier) |
| **Event Grid** | Triggers AI Search indexer on new PDF blob uploads (via Logic App intermediary) |
| **Storage Account** | Product images + SharePoint documents blob storage |
| **AKS** | Hosts all 8 containers |
| **ACR** | Container image registry |
| **Key Vault** | Secrets management (endpoints, keys, deployment names) |
| **Managed Identities** | 8 identities with least-privilege RBAC per component |

See [infra/README.md](infra/README.md) for Terraform module structure, setup instructions, and CI/CD configuration.

See [docs/security.md](docs/security.md) for the full security architecture: authentication, authorization, managed identities, workload identity federation, and network security.

### Technology stack

| Component | Technology |
| --- | --- |
| Domain API | ASP.NET Core Minimal API, Microsoft.Data.SqlClient |
| MCP Servers | [ModelContextProtocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) (Streamable HTTP) |
| Agents | [Microsoft.Agents.AI](https://learn.microsoft.com/en-us/agent-framework/agents/), Azure.AI.OpenAI |
| BFF + UI | Blazor WebAssembly ([MudBlazor](https://mudblazor.com/), Microsoft.Authentication.Msal, SignalR.Client) + .NET Minimal API (separate containers) |
| Markdown | [Markdig](https://github.com/xoofx/markdig) (renders agent markdown responses with image rewriting) |
| Auth | Microsoft.Authentication.Msal in Blazor WASM, JwtBearer validation in BFF |
| Chat persistence | Microsoft.Azure.Cosmos |
| Image proxy | Azure.Storage.Blobs |
| Testing | xUnit, FluentAssertions, NSubstitute, WebApplicationFactory, bUnit (Blazor) |
| Infrastructure | Terraform (AzureRM + AzAPI providers) |
| Deployment | Docker, Helm, AKS with Workload Identity |

### Technology references

| Topic | Link |
| ------- | ------ |
| Microsoft Agent Framework | [Overview](https://learn.microsoft.com/en-us/agent-framework/overview/?pivots=programming-language-csharp) |
| Agent Framework — Agents | [Agent types](https://learn.microsoft.com/en-us/agent-framework/agents/) |
| Agent Framework — MCP integration | [Using MCP tools with agents](https://learn.microsoft.com/en-us/agent-framework/agents/tools/local-mcp-tools) |
| Model Context Protocol | [Architecture overview](https://modelcontextprotocol.io/docs/concepts/architecture) |
| MCP C# SDK | [GitHub](https://github.com/modelcontextprotocol/csharp-sdk) |
| Backend for Frontend (BFF) | [BFF pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/backends-for-frontends) |
| Azure AI Search | [Overview](https://learn.microsoft.com/en-us/azure/search/search-what-is-azure-search) |
| Terraform AzureRM provider | [Registry](https://registry.terraform.io/providers/hashicorp/azurerm/latest) |

## Repository structure

```text
data/
  contoso-crm/                    → Simulated CRM data export (6 CSV files)
  contoso-sharepoint/             → Simulated SharePoint docs (TXT + PDF)
  contoso-images/                 → Product images (15 PNGs, uploaded to Blob Storage)

docs/
  architecture.drawio             → Editable architecture diagram (draw.io)
  lab-0.md                        → Lab 0: Bootstrap (Terraform backend, Entra, CI/CD)
  lab-1.md                        → Lab 1: Infrastructure, Validation & Data Seeding

infra/
  init.ps1 / init.sh              → One-time bootstrap scripts
  deploy.ps1 / deploy.sh          → Deployment scripts
  terraform/                      → Terraform IaC (12 modules, versioned)

src/
  appsettings.json                → Shared config (gitignored, populated by config-sync)
  config-sync/                    → Tool: Key Vault → appsettings.json
  seed-data/                      → Tool: CSV → Azure SQL (runs via terraform apply)
  simple-agent/                   → Lab 1 validation (Azure OpenAI connectivity)

  crm-api/                        → Domain API: all SQL data (11 endpoints)
  crm-api.tests/

  crm-mcp/                        → MCP Server: 10 CRM tools → CRM API
  crm-mcp.tests/

  knowledge-mcp/                  → MCP Server: knowledge search → AI Search SDK
  knowledge-mcp.tests/

  crm-agent/                      → CRM specialist agent (customers, orders, tickets)
  crm-agent.tests/

  product-agent/                  → Product specialist agent (catalog, promotions)
  product-agent.tests/

  orchestrator-agent/             → Intent classifier + specialist routing
  orchestrator-agent.tests/

  blazor-ui/                       → Blazor WebAssembly SPA (MudBlazor + MSAL + SignalR)

  bff-api/                         → BFF API (.NET): JWT validation, proxy, chat, image proxy
  bff-api.tests/

Each service contains its own helm/ folder with Chart.yaml, values.yaml, and templates/.
```

## Prerequisites

### Accounts

- An **Azure subscription** with Owner or Contributor permissions
- A **GitHub account** with a repository (GitHub Actions path)

### Tools

- [.NET SDK 9.0+](https://dotnet.microsoft.com/download)
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli)
- [GitHub CLI](https://cli.github.com/) (GitHub Actions path)
- [Terraform >= 1.14.6](https://developer.hashicorp.com/terraform/install)

## Getting started

See the lab guides in [`docs/`](docs/):

| # | Lab | Description |
| --- | ----- | ------------- |
| 0 | [Lab 0 — Bootstrap](docs/lab-0.md) | One-time setup: Terraform config files, remote state backend, CI/CD configuration |
| 1 | [Lab 1 — Infrastructure, Validation & Data Seeding](docs/lab-1.md) | Deploy Azure infrastructure, validate with simple-agent, seed Azure SQL with CRM data |

## Notes

- Provider versions are pinned in `infra/terraform/providers.tf`.
- `terraform.tfvars`, `backend.hcl`, and `appsettings.json` are gitignored.
