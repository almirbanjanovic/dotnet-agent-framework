# Source

This folder contains all .NET projects for the Contoso Outdoors agent framework. Each project is fully independent — own models, own Dockerfile, own Helm chart, own test project. No shared project references.

> **Prerequisite:** Infrastructure must be deployed before running any project. See [Lab 0](../docs/lab-0.md) and [Lab 1](../docs/lab-1.md).

## Configure app settings

After infrastructure is deployed, run the **config-sync** tool to pull secrets from Key Vault into `src/appsettings.json`. See [Lab 1 Step 2](../docs/lab-1.md#step-2--configure-app-settings) for full instructions.

> **In AKS:** Apps read the same keys from environment variables injected by Helm, so no Key Vault dependency at runtime.

The `appsettings.json` is shared across all projects — each references it via a relative path from `src/`.

## Projects

### Domain API (.NET Minimal API)

| Project | Description | Backing Service |
| --- | --- | --- |
| `crm-api/` | All CRM data: customers, orders, products, promotions, support tickets (11 endpoints). ASP.NET Core Minimal API. | Azure SQL Database (6 tables) |

### MCP Servers

| Project | Description | Backend |
| --- | --- | --- |
| `crm-mcp/` | 10 tools wrapping CRM API endpoints. Thin pass-through. | CRM API (HTTP) |
| `knowledge-mcp/` | 1 tool: `search_knowledge_base`. Calls AI Search SDK directly. | Azure AI Search |

### Agents

| Project | Description | Connects to |
| --- | --- | --- |
| `crm-agent/` | CRM specialist: customers, orders, billing, tickets, policy questions | CRM MCP + Knowledge MCP, Azure OpenAI |
| `product-agent/` | Product specialist: catalog, promotions, recommendations, sizing guides | CRM MCP + Knowledge MCP, Azure OpenAI |
| `orchestrator-agent/` | Intent classification → routes to CRM or Product agent. Stateless. | CRM Agent, Product Agent (HTTP), Azure OpenAI |

### BFF + UI

| Project | Description | Connects to |
| --- | --- | --- |
| `blazor-ui/` | Blazor WebAssembly SPA (MudBlazor + MSAL + SignalR). Static file container. | BFF API (HTTP + SignalR) |
| `bff-api/` | BFF API (.NET Minimal API). JWT auth, CRM API proxy, image proxy, chat, conversation history. | CRM API, Orchestrator, Blob Storage, Cosmos DB |

### Dev Tools (not deployed)

| Project | Description |
| --- | --- |
| `config-sync/` | Pulls Key Vault secrets into `appsettings.json` |
| `seed-data/` | Seeds Azure SQL from CSV files (runs via `terraform apply`) |
| `simple-agent/` | Validates Azure OpenAI connectivity (Lab 1) |

## Labs

| # | Lab | Description |
| --- | --- | --- |
| 0 | [Lab 0 — Bootstrap](../docs/lab-0.md) | One-time setup: Terraform config files, remote state backend, CI/CD |
| 1 | [Lab 1 — Infrastructure, Validation & Data Seeding](../docs/lab-1.md) | Deploy Azure infrastructure, validate with simple-agent, seed Azure SQL |

## Running a project

From the project directory (e.g., `src/crm-api/`):

```bash
dotnet restore
dotnet run
```

## Running tests

```bash
# All tests
dotnet test dotnet-agent-framework.sln

# Single project
dotnet test src/crm-api.tests/

# With coverage
dotnet test --collect:"XPlat Code Coverage"
```
