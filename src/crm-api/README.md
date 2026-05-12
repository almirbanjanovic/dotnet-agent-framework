# CRM API
> REST API backed by Cosmos DB serving customer, order, product, promotion, and support-ticket data (11 endpoints).

Fully implemented — ASP.NET Core Minimal API with 6 Cosmos DB containers.

## Configuration

Required config keys (populated by config-sync from Key Vault):

| Key | Description |
|-----|-------------|
| `CosmosDb:Endpoint` | Cosmos DB CRM account endpoint |
| `CosmosDb:DatabaseName` | Database name (default: contoso-crm) |
| `AzureAd:TenantId` | Entra tenant ID for DefaultAzureCredential |

Run config-sync to populate: `cd src/config-sync && dotnet run -- <key-vault-uri> [environment]`

## How to run locally

```bash
cd src/crm-api
dotnet run
```

The API starts on `http://localhost:5001` by default (set by Aspire AppHost in local dev). Health endpoints: `/health` (liveness), `/ready` (Cosmos DB connectivity).

## Endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/v1/customers/` | List all customers |
| GET | `/api/v1/customers/{id}` | Get customer by ID |
| GET | `/api/v1/orders/{id}` | Get order by ID |
| GET | `/api/v1/customers/{id}/orders` | List orders for a customer |
| GET | `/api/v1/orders/{id}/items` | List line items for an order |
| GET | `/api/v1/products/` | Search/list products (query, category, in_stock_only) |
| GET | `/api/v1/products/{id}` | Get product by ID |
| GET | `/api/v1/promotions/` | List active promotions |
| GET | `/api/v1/promotions/eligible/{customerId}` | Get promotions eligible for a customer's loyalty tier |
| GET | `/api/v1/customers/{id}/tickets` | List support tickets for a customer (open_only filter) |
| POST | `/api/v1/tickets` | Create a support ticket. When `category="return"` and a valid `order_id` is supplied, a background task additionally fires-and-forgets a refund-risk alert to the `fraud-workflow` service (`POST /api/v1/refunds`) including the new ticket's id. If `fraud-workflow` responds with `status="below_threshold"` (amount under `Refund:Threshold`, default $200), CRM API immediately resolves the ticket with an `auto/below_threshold` audit comment so the customer sees the outcome. Above-threshold alerts run the workflow asynchronously and call back to `/internal/tickets/{id}/refund-decision` when a terminal decision is reached. Failures of the outbound alert are logged and never surfaced to the caller. |
| PATCH | `/api/v1/tickets/{id}` | Update a support ticket's status. Customer-settable values are `cancelled` and `resolved`. Only tickets currently in status `open` may transition; anything else returns `409 Conflict`. The customer **must** be identified by the `X-Customer-Entra-Id` header — there is no body fallback (a body-only `customer_id` is ignored and the request is rejected with `401 Unauthorized`). Cross-customer reads return `404 Not Found` (not `403`) so an attacker cannot probe ticket ids. On success, `closed_at` is set to today's UTC date. |
| POST | `/api/v1/internal/tickets/{id}/refund-decision` | **Service-to-service callback** — not exposed by the BFF. Called by `fraud-workflow` when a terminal refund decision is reached (`approve`, `reject`, `below_threshold`, `timeout`). Approve/below_threshold transition the ticket to `resolved`; reject/timeout leave it `open`. In all cases an audit line is appended to the ticket's `comments` field (sanitized to single-line, capped to 500 chars). Idempotent: a late callback against an already-resolved ticket appends the comment but does not re-mutate the status. The endpoint relies on cluster-network isolation for trust and does not require any header. |

## Architecture role

CRM API is the single data-access layer for all CRM data in the system. MCP servers (crm-mcp) call these endpoints to expose CRM operations as agent tools, and the BFF API proxies a subset for direct UI access. No other component talks to the CRM Cosmos DB directly.
