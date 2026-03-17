# Security Architecture

This document describes the authentication, authorization, identity, and network security model for the Contoso Outdoors agent framework.

## Overview

The security model has **two independent authentication flows** that solve different problems:

| | User Authentication (Entra ID) | Service Authentication (Workload Identity) |
| --- | --- | --- |
| **Question answered** | "Which *human* is making this request?" | "Which *service* is calling this Azure resource?" |
| **Who authenticates** | Customer (Emma, James, Sarah...) | Kubernetes pod (crm-api, bff, crm-agent...) |
| **Authenticates to** | BFF API (validates JWT) | Azure resources (SQL, Cosmos, Blob, Search, OpenAI) |
| **Token issuer** | Entra ID (`login.microsoftonline.com`) | AKS OIDC issuer → Azure AD |
| **Token type** | JWT access token (Bearer) | Managed identity token (via DefaultAzureCredential) |
| **Purpose** | Data scoping — "show me only *my* orders" | Resource access — "let this pod connect to Azure SQL" |
| **Without it** | Anyone could see all customer data | Services would need hardcoded passwords/API keys |

Both flows are required. They operate on **orthogonal axes** — user auth controls *whose data* is returned, while workload identity controls *which Azure resources* a service can reach. Removing either one breaks the system.

### How the two flows work together

A single customer request touches both flows:

```text
① Emma signs in via MSAL (Entra ID PKCE) → browser gets JWT with oid claim
② Blazor sends "GET /api/orders" with Authorization: Bearer <JWT>
③ BFF validates JWT → extracts oid → passes X-Customer-Entra-Id to CRM API
④ CRM API pod uses Workload Identity (id-crm-api) to get an Azure AD token
⑤ CRM API connects to Azure SQL using the managed identity token (no password)
⑥ CRM API queries: SELECT * FROM Orders WHERE entra_id = '<emma-oid>'
⑦ Only Emma's orders are returned — scoped by user auth, accessed by workload identity
```

Step ③ uses **user authentication** (Entra ID) — it determines *whose* data to query.
Steps ④–⑤ use **service authentication** (Workload Identity) — they determine *how* the CRM API
connects to Azure SQL without any stored credentials.

```text
User auth (Entra ID)        → controls WHAT data (row-level: entra_id filter)
Service auth (Workload ID)  → controls HOW to access Azure (identity-based, no secrets)
```

## User Authentication (Entra ID)

### How users sign in

1. User opens the Blazor WASM UI → MSAL checks for existing session
2. If not signed in, MSAL redirects to Entra ID login page (PKCE flow)
3. User authenticates at `login.microsoftonline.com`
4. Entra ID issues an access token containing user identity + role claims
5. MSAL stores the token in browser memory (not localStorage for security)
6. Blazor sends `Authorization: Bearer <token>` on every request to the BFF
7. BFF validates the JWT (signature, issuer, audience, expiry, roles)
8. BFF extracts claims and proxies requests to internal services with `X-User-*` headers

### Entra App Registration

Terraform creates a **SPA (public client)** app registration:

| Property | Value |
| --- | --- |
| Display name | `app-{base_name}-bff-{environment}` |
| Type | Single-page application (public client) |
| Sign-in audience | AzureADMyOrg (single tenant) |
| Redirect URIs | `https://localhost:5002/authentication/login-callback` (dev), `https://{agc-frontend-fqdn}/authentication/login-callback` (AKS) |
| Client secret | **None** (SPA uses PKCE, not secrets) |
| Token version | v2 |

### App Roles

Two roles are defined on the app registration:

| Role | Claim Value | Who gets it | What it allows |
| --- | --- | --- | --- |
| Customer | `Customer` | All registered customers | View own data, chat with agents, create support tickets for own orders |

Customers can only see their own data. Authorization is **identity-based** (entra_id → customer_id), not role-based. The `Customer` role gates access to the application itself.

### Test Users

Terraform creates 5 customer test users in Entra ID, matching the pre-seeded customers in Azure SQL:

| User | UPN | Customer ID | Loyalty | Scenario |
| --- | --- | --- | --- | --- |
| Emma Wilson | `emma.wilson@{domain}` | 101 | Silver | 1 — Order tracking |
| James Chen | `james.chen@{domain}` | 102 | Bronze | 2 — Return/sizing |
| Sarah Miller | `sarah.miller@{domain}` | 103 | Gold | 3 — Promotions |
| David Park | `david.park@{domain}` | 104 | Silver | 4 — Damaged item |
| Lisa Torres | `lisa.torres@{domain}` | 105 | Bronze | 5 — Product recommendation |

Passwords follow the pattern `Contoso-<Animal>-<4digits>!#` and are stored in Key Vault as `CUSTOMER-{NAME}-PASSWORD`. Each user's Entra object ID is linked to their customer record in the SQL Customers table (via the `entra_id` column) during deployment.

## User Authorization

### BFF Layer

The BFF validates the Bearer token (JWT) on every request and enforces identity-based access:

- **`[Authorize]`** on all API endpoints → missing/invalid token returns 401
- **Customer identity** → BFF extracts `oid` claim from JWT, passes as `X-Customer-Entra-Id` header to internal services
- **All data queries are scoped to the authenticated customer** — no customer can see another's data
- **CORS** → BFF allows requests from Blazor WASM UI origin (localhost:5002 for dev, AGC hostname for prod)

### CRM API Layer

The CRM API is internal (ClusterIP) and trusts the BFF. It reads `X-Customer-Entra-Id` from the request header:

- **All queries filtered by `entra_id`** → customers can only see their own data
- **New customer auto-provisioning** → if no matching `entra_id` exists, creates a new customer record from JWT claims
- **Support ticket creation** → any authenticated customer can create tickets for their own orders

### Agent Layer

Agents receive customer context from the Orchestrator via `X-Customer-Entra-Id` header:

- **All MCP tool calls are scoped to the authenticated customer's entra_id**
- **Agents don't need to "look up" the customer** — the identity is known from the token
- **Orchestrator** → propagates `X-Customer-Entra-Id` to specialist agents

## Service Authentication (Workload Identity)

### How services authenticate to Azure

Each service runs in AKS with its own **user-assigned managed identity** and **Kubernetes service account**. Azure AD issues tokens to pods via the **workload identity federation** protocol:

```text
Terraform creates:
  ① User-assigned managed identity (Azure AD)
  ② RBAC role assignment (identity → Azure resource)
  ③ Federated identity credential (trust rule: AKS OIDC + K8s service account → identity)
  ④ K8s service account in AKS namespace (annotated with identity client ID)

At runtime:
  ⑤ AKS webhook injects a K8s token + env vars into the pod
  ⑥ Pod calls DefaultAzureCredential() → reads K8s token
  ⑦ Azure AD validates: token from trusted AKS cluster? correct service account?
  ⑧ Azure AD issues an Azure access token for the managed identity
  ⑨ Pod uses the token to call Azure SQL, Blob Storage, Cosmos DB, etc.
```

No secrets, no passwords, no API keys in environment variables. Authentication is purely identity-based.

### Managed Identities

| Identity | Service | Purpose |
| --- | --- | --- |
| `id-bff` | BFF API | Read Blob Storage (image proxy), read/write Cosmos DB (conversations), read Key Vault |
| `id-crm-api` | CRM API | Access Azure SQL, read Key Vault |
| `id-crm-mcp` | CRM MCP Server | Read Key Vault |
| `id-know-mcp` | Knowledge MCP Server | Read AI Search index, read Key Vault |
| `id-crm-agt` | CRM Agent | Call Azure OpenAI, read Key Vault |
| `id-prod-agt` | Product Agent | Call Azure OpenAI, read Key Vault |
| `id-orch` | Orchestrator Agent | Call Azure OpenAI, read Key Vault |
| `id-kubelet` | AKS kubelet | Pull images from ACR |

### RBAC Matrix

| Identity | Key Vault Secrets User | SQL Access | OpenAI User | Cosmos DB Data Contributor | Search Index Reader | Blob Data Reader | ACR Pull |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `id-bff` | ✓ | | | ✓ | | ✓ | |
| `id-crm-api` | ✓ | ✓ | | | | | |
| `id-crm-mcp` | ✓ | | | | | | |
| `id-know-mcp` | ✓ | | | | ✓ | | |
| `id-crm-agt` | ✓ | | ✓ | | | | |
| `id-prod-agt` | ✓ | | ✓ | | | | |
| `id-orch` | ✓ | | ✓ | | | | |
| `id-kubelet` | | | | | | | ✓ |

Each identity has **only** the permissions it needs. No identity has blanket access.

### Workload Identity Federation

Each identity has a **federated credential** that binds it to a specific AKS service account:

| Identity | K8s Service Account | Namespace | Federation Subject |
| --- | --- | --- | --- |
| `id-bff` | `sa-bff` | `contoso` | `system:serviceaccount:contoso:sa-bff` |
| `id-crm-api` | `sa-crm-api` | `contoso` | `system:serviceaccount:contoso:sa-crm-api` |
| `id-crm-mcp` | `sa-crm-mcp` | `contoso` | `system:serviceaccount:contoso:sa-crm-mcp` |
| `id-know-mcp` | `sa-know-mcp` | `contoso` | `system:serviceaccount:contoso:sa-know-mcp` |
| `id-crm-agt` | `sa-crm-agent` | `contoso` | `system:serviceaccount:contoso:sa-crm-agent` |
| `id-prod-agt` | `sa-prod-agent` | `contoso` | `system:serviceaccount:contoso:sa-prod-agent` |
| `id-orch` | `sa-orch-agent` | `contoso` | `system:serviceaccount:contoso:sa-orch-agent` |

The federation is a **three-way lock**:

1. Token must come from **this AKS cluster** (OIDC issuer)
2. Token must be for **this service account in this namespace** (subject)
3. Only then is access granted for **this managed identity** (parent)

A pod using `sa-bff` cannot assume `id-crm-api`. A pod in a different namespace cannot assume any of these identities.

### Kubernetes Service Accounts

Terraform creates service accounts in the `contoso` namespace, annotated for workload identity:

```yaml
apiVersion: v1
kind: ServiceAccount
metadata:
  name: sa-crm-api
  namespace: contoso
  labels:
    azure.workload.identity/use: "true"
  annotations:
    azure.workload.identity/client-id: <client-id-of-id-crm-api>
```

Manifests are in `infra/terraform/manifests/` and applied via `kubectl_manifest` resources.

## Network Security

### Public vs Internal

| Service | Exposure | How |
| --- | --- | --- |
| Blazor WASM UI | **Public** | AKS Gateway API (AGC HTTPRoute, path: `/`) — static file serving |
| BFF API | **Public** | AKS Gateway API (AGC HTTPRoute, path: `/api/*`, `/hubs/*`, `/auth/*`) |
| CRM API | **Internal only** | ClusterIP service |
| CRM MCP | **Internal only** | ClusterIP |
| Knowledge MCP | **Internal only** | ClusterIP |
| CRM Agent | **Internal only** | ClusterIP |
| Product Agent | **Internal only** | ClusterIP |
| Orchestrator Agent | **Internal only** | ClusterIP |

Only the Blazor WASM UI and BFF API are publicly accessible (via path-based AGC routing on the same hostname). All other services are internal to the AKS cluster. The BFF acts as the security perimeter — it validates every user request before forwarding to internal services. The Blazor WASM UI serves static files only (no server-side logic, no secrets).

### TLS

A self-signed TLS certificate is stored in Key Vault and referenced by the Gateway API TLS configuration. The certificate's CN and SAN match the App Gateway for Containers frontend FQDN.

## Image Security

Product images are stored in a **private** Blob Storage container. The browser never gets a direct storage URL:

1. Blazor WASM renders `<img src="/api/images/{filename}">`
2. BFF validates filename (`^[a-zA-Z0-9_-]+\.png$` — rejects path traversal)
3. BFF checks user is authenticated
4. BFF fetches blob bytes from Blob Storage using managed identity
5. BFF streams bytes to browser with `Content-Type: image/png`

This pattern is extensible to private per-customer images (e.g., damage claim photos) in the future.

## Key Vault

All secrets, credentials, and identity client IDs are stored in Azure Key Vault:

| Category | Secrets |
| --- | --- |
| Azure OpenAI | Endpoint, API key, deployment names |
| Azure SQL | Server FQDN, database name, admin login, admin password |
| Cosmos DB | Endpoint, key, database name |
| Blob Storage | Endpoint, account name, container, key |
| AI Search | Endpoint, admin key, index name |
| Entra ID | BFF client ID, tenant ID, hostname (no client secret — SPA uses PKCE) |
| Workload Identities | Client IDs for all 7 service identities |
| Customers | Passwords + Entra object IDs for 5 test customers |

Key Vault RBAC:

- **Secrets Officer** → Terraform deployer (writes secrets during `terraform apply`)
- **Secrets User** → all 7 workload identities + deployer (reads secrets at startup)
- **Certificate User** → Reserved for AGC TLS integration (reads TLS cert for ingress)

## Terraform Resources

All security resources are created by Terraform during `terraform apply`:

| Resource | Module |
| --- | --- |
| Managed identities (8) | `modules/identity/v1` |
| RBAC role assignments | `modules/rbac/*/v1` (foundry, cosmosdb, storage, acr, aks, keyvault, search) |
| Workload identity federation (7) | `modules/workload-identity/v1` |
| Entra app registration + roles | `modules/entra/v1` |
| Test users + passwords | `modules/entra/v1` (users.tf) |
| TLS certificate | `modules/tls-cert/v1` |
| K8s namespace + service accounts | `kubectl_manifest` resources (manifests/) |
| Key Vault + secrets | `modules/keyvault/v1`, `modules/keyvault-secrets/v1` |
