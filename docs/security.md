# Security Architecture

This document describes the authentication, authorization, identity, and network security model for the Contoso Outdoors agent framework.

## Overview

```
User (browser)
  │ MSAL.js PKCE (Entra ID)
  ▼
React UI (static) ─── Bearer token ───► BFF API (.NET)
  │
  ├── X-User-Id, X-User-Roles headers ──► CRM API (internal)
  ├── HTTP ──► Orchestrator Agent (internal)
  │              ├── X-User-Id, X-User-Roles ──► CRM Agent (internal)
  │              └── X-User-Id, X-User-Roles ──► Product Agent (internal)
  │
  └── All internal services authenticate to Azure using Workload Identity
      (managed identity per service, federated via AKS OIDC)
```

## User Authentication (Entra ID)

### How users sign in

1. User opens the React UI → MSAL.js checks for existing session
2. If not signed in, MSAL.js redirects to Entra ID login page (PKCE flow)
3. User authenticates at `login.microsoftonline.com`
4. Entra ID issues an access token containing user identity + role claims
5. MSAL.js stores the token in browser memory (not localStorage for security)
6. React sends `Authorization: Bearer <token>` on every request to the BFF
7. BFF validates the JWT (signature, issuer, audience, expiry, roles)
8. BFF extracts claims and proxies requests to internal services with `X-User-*` headers

### Entra App Registration

Terraform creates a **SPA (public client)** app registration:

| Property | Value |
|---|---|
| Display name | `app-{base_name}-bff-{environment}` |
| Type | Single-page application (public client) |
| Sign-in audience | AzureADMyOrg (single tenant) |
| Redirect URIs | `http://localhost:3000` (dev), `https://{agc-frontend-fqdn}` (AKS) |
| Client secret | **None** (SPA uses PKCE, not secrets) |
| Token version | v2 |

### App Roles

Two roles are defined on the BFF app registration:

| Role | Claim Value | Who gets it | What it allows |
|---|---|---|---|
| Agent User | `Agent.User` | All CS reps | View customers, orders, products, promotions, tickets. Chat with agents. |
| Data Writer | `Data.Writer` | Senior CS reps | Create and update support tickets. |

Roles appear in the JWT `roles` claim. The React UI checks roles to conditionally render UI elements (e.g., "Create Ticket" button). The BFF checks roles server-side before proxying write requests.

### Test Users

Terraform creates 5 test users in Entra ID with random passwords stored in Key Vault:

| User | UPN | Roles | Purpose |
|---|---|---|---|
| Emma CS Rep | `emma.csrep@{domain}` | `Agent.User` | Basic rep — view only |
| Bob Senior Rep | `bob.senior@{domain}` | `Agent.User`, `Data.Writer` | Senior rep — can create tickets |
| Sarah Manager | `sarah.manager@{domain}` | `Agent.User`, `Data.Writer` | Manager — full access |
| Dave Readonly | `dave.readonly@{domain}` | *(none)* | Security test — authenticated but unauthorized (403) |
| Admin Contoso | `admin.contoso@{domain}` | `Agent.User`, `Data.Writer` | Admin account |

Passwords follow the pattern `Contoso-<Animal>-<4digits>!#` and are stored in Key Vault as `TEST-USER-{NAME}-PASSWORD`.

## User Authorization

### BFF Layer

The BFF validates the Bearer token (JWT) on every request and enforces role-based access:

- **`[Authorize]`** on all API endpoints → missing/invalid token returns 401
- **Role checks** → BFF reads `roles` claim from the validated JWT
- **Proxy headers** → on every outbound request to CRM API, BFF injects:
  - `X-User-Id` (from `preferred_username` claim)
  - `X-User-Roles` (comma-separated, from `roles` claim)
  - `X-User-Email` (from `email` or `preferred_username` claim)
- **CORS** → BFF allows requests from React UI origin (localhost:3000 for dev, AKS hostname for prod)

### CRM API Layer

The CRM API is internal (ClusterIP) and trusts the BFF. It reads `X-User-Roles` from the request header:

- **GET endpoints** → require `Agent.User` in `X-User-Roles`
- **POST `/support-tickets`** → require `Data.Writer` in `X-User-Roles`, returns 403 if missing

### Agent Layer

Agents receive user context from the Orchestrator via `X-User-Id` and `X-User-Roles` headers:

- **CRM Agent** → before calling `create_support_ticket` MCP tool, checks `X-User-Roles` contains `Data.Writer`
- **Product Agent** → read-only tools, no role gating needed
- **Orchestrator** → pass-through, propagates headers to specialist agents

## Service Authentication (Workload Identity)

### How services authenticate to Azure

Each service runs in AKS with its own **user-assigned managed identity** and **Kubernetes service account**. Azure AD issues tokens to pods via the **workload identity federation** protocol:

```
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
|---|---|---|
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
|---|---|---|---|---|---|---|---|
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
|---|---|---|---|
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
|---|---|---|
| React UI | **Public** | AKS Gateway API (AGC HTTPRoute, path: `/`) — static file serving |
| BFF API | **Public** | AKS Gateway API (AGC HTTPRoute, path: `/api/*`, `/hubs/*`, `/auth/*`) |
| CRM API | **Internal only** | ClusterIP service |
| CRM MCP | **Internal only** | ClusterIP |
| Knowledge MCP | **Internal only** | ClusterIP |
| CRM Agent | **Internal only** | ClusterIP |
| Product Agent | **Internal only** | ClusterIP |
| Orchestrator Agent | **Internal only** | ClusterIP |

Only the React UI and BFF API are publicly accessible (via path-based Ingress routing on the same hostname). All other services are internal to the AKS cluster. The BFF acts as the security perimeter — it validates every user request before forwarding to internal services. The React UI serves static files only (no server-side logic, no secrets).

### TLS

A self-signed TLS certificate is stored in Key Vault and referenced by the Gateway API TLS configuration. The certificate's CN and SAN match the App Gateway for Containers frontend FQDN.

## Image Security

Product images are stored in a **private** Blob Storage container. The browser never gets a direct storage URL:

1. React renders `<img src="/api/images/{filename}">`
2. BFF validates filename (`^[a-zA-Z0-9_-]+\.png$` — rejects path traversal)
3. BFF checks user is authenticated
4. BFF fetches blob bytes from Blob Storage using managed identity
5. BFF streams bytes to browser with `Content-Type: image/png`

This pattern is extensible to private per-customer images (e.g., damage claim photos) in the future.

## Key Vault

All secrets, credentials, and identity client IDs are stored in Azure Key Vault:

| Category | Secrets |
|---|---|
| Azure OpenAI | Endpoint, API key, deployment names |
| Azure SQL | Server FQDN, database name, admin login, admin password |
| Cosmos DB | Endpoint, key, database name |
| Blob Storage | Endpoint, account name, container, key |
| AI Search | Endpoint, admin key, index name |
| Entra ID | BFF client ID, tenant ID, hostname (no client secret — SPA uses PKCE) |
| Workload Identities | Client IDs for all 7 service identities |
| Test Users | Passwords for 5 test users |

Key Vault RBAC:
- **Secrets Officer** → Terraform deployer (writes secrets during `terraform apply`)
- **Secrets User** → all 7 workload identities + deployer (reads secrets at startup)
- **Certificate User** → Reserved for AGC TLS integration (reads TLS cert for ingress)

## Terraform Resources

All security resources are created by Terraform during `terraform apply`:

| Resource | Module |
|---|---|
| Managed identities (8) | `modules/identity/v1` |
| RBAC role assignments | `modules/rbac/*/v1` (foundry, cosmosdb, storage, acr, aks, keyvault, search) |
| Workload identity federation (7) | `modules/workload-identity/v1` |
| Entra app registration + roles | `modules/entra/v1` |
| Test users + passwords | `modules/entra/v1` (users.tf) |
| TLS certificate | `modules/tls-cert/v1` |
| K8s namespace + service accounts | `kubectl_manifest` resources (manifests/) |
| Key Vault + secrets | `modules/keyvault/v1`, `modules/keyvault-secrets/v1` |
