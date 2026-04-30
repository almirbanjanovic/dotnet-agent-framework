# CRM MCP Server
> MCP Server exposing 11 tools that wrap CRM API endpoints for agent consumption.

ASP.NET Core MCP Server (Streamable HTTP transport) implemented with the [ModelContextProtocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk). Each tool calls the CRM API over HTTP using the pod's workload identity.

## Configuration

Required config keys (populated by config-sync from Key Vault):

| Key | Description |
|-----|-------------|
| `CrmApi:BaseUrl` | CRM API base URL |
| `AzureAd:TenantId` | Entra tenant ID for DefaultAzureCredential |

Run config-sync to populate: `cd src/config-sync && dotnet run -- <key-vault-uri> [environment]`

## How to run locally

```bash
cd src/crm-mcp
dotnet run
```

The MCP server listens on the URL bound by `ASPNETCORE_URLS` (Aspire AppHost binds 5002 in local dev).

## Tools

| Tool | Backing endpoint |
|------|------------------|
| `get_all_customers` | `GET /api/v1/customers/` |
| `get_customer_detail` | `GET /api/v1/customers/{id}` |
| `get_customer_orders` | `GET /api/v1/customers/{id}/orders` |
| `get_order_detail` | `GET /api/v1/orders/{id}` |
| `get_order_items` | `GET /api/v1/orders/{id}/items` |
| `get_products` | `GET /api/v1/products/` |
| `get_product_detail` | `GET /api/v1/products/{id}` |
| `get_promotions` | `GET /api/v1/promotions/` |
| `get_eligible_promotions` | `GET /api/v1/promotions/eligible/{customerId}` |
| `get_support_tickets` | `GET /api/v1/customers/{id}/tickets` |
| `create_support_ticket` | `POST /api/v1/tickets` |

## Architecture role

CRM MCP Server is a thin protocol adapter that translates MCP tool calls into CRM API HTTP requests. Agents (crm-agent, product-agent) connect to this server to access customer, order, product, promotion, and ticket data without knowing the REST API contract directly.

