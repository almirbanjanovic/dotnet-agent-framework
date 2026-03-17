# Security Architecture

This document describes how the Contoso Outdoors agent framework handles authentication ("who are you?"), authorization ("what can you do?"), and secure communication between services.

## Key Terms

These terms appear throughout this document:

| Term | What it means |
| --- | --- |
| **Entra ID** | Microsoft's identity service (formerly Azure Active Directory). It manages user accounts, passwords, and login flows. Think of it as the bouncer who checks IDs at the door. |
| **MSAL** | Microsoft Authentication Library. A JavaScript library that runs inside the Blazor app in the user's browser. It handles the login redirect, token storage, and attaching tokens to API requests — so application code doesn't have to. |
| **JWT** | JSON Web Token. A digitally signed data packet that Entra ID issues after login. It contains the user's identity (name, email, unique ID) and what they're allowed to do (roles). The BFF checks this signature on every request to confirm the token hasn't been tampered with. |
| **Bearer token** | The way a JWT is sent to an API. The browser includes `Authorization: Bearer <token>` in the HTTP header of every request. "Bearer" means "whoever bears (carries) this token is authenticated." |
| **PKCE** | Proof Key for Code Exchange (pronounced "pixy"). A security mechanism for browser-based apps that can't keep secrets (the entire Blazor WASM app runs in the user's browser — anyone can view its code). PKCE adds a one-time random challenge during login so that even if someone intercepts the login redirect URL, they can't use it to steal the token. |
| **SPA** | Single-Page Application. The Blazor WASM UI is a SPA — it's downloaded to the browser as static files and runs entirely client-side. It has no server-side secrets, no backend session, and no way to hide an API key. This is why PKCE is required. |
| **Managed identity** | An Azure-managed identity for a service (not a human). Instead of storing a database password in an environment variable, the service gets an identity that Azure trusts. The service calls `DefaultAzureCredential()` and Azure issues a short-lived token automatically. No passwords to rotate, no secrets to leak. |
| **Workload Identity** | The AKS-specific mechanism that connects a Kubernetes pod to an Azure managed identity. It uses the cluster's OIDC issuer (a certificate-based trust) to prove "this pod in this namespace is authorized to use this identity." |
| **RBAC** | Role-Based Access Control. Azure's permission system. Each managed identity is granted specific roles (e.g., "Key Vault Secrets User", "Cosmos DB Data Owner") scoped to specific resources. |
| **OIDC** | OpenID Connect. A standard identity protocol built on top of OAuth 2.0. Used here in two places: (1) Entra ID uses OIDC for user login, and (2) AKS uses OIDC to federate pod identities with Azure AD. |
| **CORS** | Cross-Origin Resource Sharing. A browser security feature that prevents JavaScript on one website from calling APIs on a different website. The BFF explicitly allows requests from the Blazor WASM UI's origin. |

## Overview

The security model has **two independent authentication flows** that solve different problems:

| | User Authentication (Entra ID) | Service Authentication (Workload Identity) |
| --- | --- | --- |
| **Question answered** | "Which *human* is making this request?" | "Which *service* is calling this Azure resource?" |
| **Who authenticates** | Customer (Emma, James, Sarah...) | Kubernetes pod (crm-api, bff, crm-agent...) |
| **Authenticates to** | BFF API (validates JWT) | Azure resources (SQL, Cosmos, Blob, Search, OpenAI) |
| **Token issuer** | Entra ID (`login.microsoftonline.com`) | AKS OIDC issuer → Azure AD |
| **Token type** | JWT access token (sent as Bearer header) | Managed identity token (obtained via `DefaultAzureCredential()`) |
| **Purpose** | Data scoping — "show me only *my* orders" | Resource access — "let this pod connect to Azure SQL" |
| **Without it** | Anyone could see all customer data | Services would need hardcoded passwords/API keys |

Both flows are required. They operate on **orthogonal axes** — user auth controls *whose data* is returned, while workload identity controls *which Azure resources* a service can reach. Removing either one breaks the system.

### How the two flows work together

A single customer request touches both flows:

```text
① Emma signs in via MSAL (the login library in her browser). MSAL redirects her
  to Entra ID's login page, she enters her password, and Entra ID returns a JWT
  (a signed token containing her identity, like a digital ID card).
② Blazor sends "GET /api/orders" with her JWT in the Authorization header
③ BFF validates the JWT signature → extracts her unique ID (oid claim) →
  passes it as X-Customer-Entra-Id header to the CRM API
④ CRM API pod uses Workload Identity (id-crm-api) to get an Azure AD token
⑤ CRM API connects to Azure SQL using that managed identity token (no password needed)
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

When a customer opens the app, the following happens:

1. **Browser loads the Blazor WASM app** → MSAL (the login library bundled in the app) checks if there's already a valid token in browser memory from a previous login
2. **If not signed in**, MSAL redirects the browser to Entra ID's login page at `login.microsoftonline.com`. This redirect uses the PKCE flow, which adds a one-time cryptographic challenge to prevent token theft (important because the app runs entirely in the browser and can't keep secrets)
3. **User enters their credentials** at the Entra ID login page (this page is hosted by Microsoft, not by our app)
4. **Entra ID issues a JWT** (a signed token) containing the user's unique object ID (`oid`), display name, email, and role (`Customer`)
5. **MSAL receives the token** and stores it in browser memory (not in localStorage or cookies, which could be stolen by malicious scripts)
6. **On every API request**, Blazor automatically attaches the JWT as `Authorization: Bearer <token>` in the HTTP header
7. **BFF receives the request** and validates the JWT: checks the cryptographic signature (is it genuinely from Entra ID?), the issuer, the audience (is it meant for this app?), the expiry time, and the role claims
8. **BFF extracts the user's identity** from the validated token and passes it to internal services via an `X-Customer-Entra-Id` header

### Entra App Registration

Terraform creates an **app registration** in Entra ID. This tells Entra ID: "there is an application called Contoso Outdoors BFF, and here's how users are allowed to sign into it."

Because the Blazor WASM app is a SPA (it runs entirely in the browser), it's registered as a **public client** — meaning it doesn't have a client secret (there's nowhere safe to store one in browser code). Instead, it uses PKCE for secure login.

| Property | Value |
| --- | --- |
| Display name | `app-{base_name}-bff-{environment}` |
| Type | Single-page application (public client) |
| Sign-in audience | AzureADMyOrg (single tenant) |
| Redirect URIs | `https://localhost:5002/authentication/login-callback` (dev), `https://{agc-frontend-fqdn}/authentication/login-callback` (AKS) |
| Client secret | **None** — browser apps can't keep secrets, so PKCE is used instead |
| Token version | v2 |

### App Roles

The app registration defines one role that controls who can access the application:

| Role | Claim Value | Who gets it | What it allows |
| --- | --- | --- | --- |
| Customer | `Customer` | All registered customers | View own data, chat with agents, create support tickets for own orders |

The `Customer` role gates access to the application itself (if you don't have this role, you can't use the app at all). However, within the app, data access is controlled by **identity** — the system uses each customer's unique Entra ID (`entra_id` column in the Customers table) to filter data, so Emma can only see Emma's orders regardless of her role.

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

Once a user is authenticated ("we know who you are"), authorization determines what they can see and do.

### BFF Layer

The BFF (Backend for Frontend) is the only publicly accessible API. It validates the JWT on every request:

- **`[Authorize]` attribute** on all API endpoints → any request without a valid token gets a `401 Unauthorized` response
- **Customer identity** → BFF reads the `oid` (object ID) claim from the JWT — this is the user's unique Entra ID — and passes it as `X-Customer-Entra-Id` header to internal services
- **All data queries are scoped to the authenticated customer** — no customer can see another's data
- **CORS** → the BFF only accepts requests from the Blazor WASM UI's origin (this prevents other websites from calling the API using a logged-in user's browser)

### CRM API Layer

The CRM API is internal (only reachable from inside the Kubernetes cluster, not from the internet). It trusts that the BFF has already validated the user's token. It reads the `X-Customer-Entra-Id` header to know which customer is making the request:

- **All SQL queries include** `WHERE entra_id = '<value from header>'` → customers can only see their own data
- **New customer auto-provisioning** → if a user signs in but no matching `entra_id` exists in the Customers table, the CRM API creates a new customer record automatically
- **Support ticket creation** → any authenticated customer can create support tickets, but only for their own orders

### Agent Layer

Agents receive customer context from the Orchestrator via `X-Customer-Entra-Id` header:

- **All MCP tool calls are scoped to the authenticated customer's entra_id**
- **Agents don't need to "look up" the customer** — the identity is known from the token
- **Orchestrator** → propagates `X-Customer-Entra-Id` to specialist agents

## Service Authentication (Workload Identity)

This section covers how backend services (running as containers in Kubernetes) authenticate to Azure resources like SQL Database, Cosmos DB, and Blob Storage — without any hardcoded passwords.

### The problem Workload Identity solves

The CRM API needs to connect to Azure SQL Database. Traditionally, you'd put a database password in an environment variable or config file. This is risky — passwords can leak through logs, crash dumps, or source control. Workload Identity eliminates passwords entirely: each service gets an Azure-managed identity, and Azure handles the token exchange automatically.

### How services authenticate to Azure

Each service runs in AKS (Azure Kubernetes Service) with its own **managed identity** (an Azure-managed identity that represents the service, not a human). Here's how it works:

```text
What Terraform sets up (one-time, during deployment):
  ① Creates a managed identity in Azure AD (e.g., "id-crm-api")
  ② Grants that identity specific permissions (e.g., "can access Azure SQL")
  ③ Creates a trust rule: "if a token comes from THIS AKS cluster for
     THIS Kubernetes service account, it can use THIS identity"
  ④ Creates a Kubernetes service account in the cluster, labeled with
     the identity's client ID

What happens at runtime (every time a pod starts):
  ⑤ AKS automatically injects a short-lived token into the pod
  ⑥ The pod's code calls DefaultAzureCredential() — a single line
     of code that reads the injected token
  ⑦ Azure AD verifies: is this token from a trusted cluster? Is it
     for the correct service account in the correct namespace?
  ⑧ Azure AD issues an access token for the managed identity
  ⑨ The pod uses that token to call Azure SQL, Blob Storage, etc.
     (no password, no API key — just identity-based access)
```

The developer experience is simple: call `DefaultAzureCredential()` and it works. All the complexity is handled by the infrastructure.

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

Each identity is granted **only** the permissions it needs (least privilege). This table shows which identity can access which resource:

| Identity | Key Vault Secrets User | SQL Access | OpenAI User | Cosmos DB Data Owner | Search Index Reader | Blob Data Reader | ACR Pull |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `id-bff` | ✓ | | | ✓ | | ✓ | |
| `id-crm-api` | ✓ | ✓ | | | | | |
| `id-crm-mcp` | ✓ | | | | | | |
| `id-know-mcp` | ✓ | | | | ✓ | | |
| `id-crm-agt` | ✓ | | ✓ | | | | |
| `id-prod-agt` | ✓ | | ✓ | | | | |
| `id-orch` | ✓ | | ✓ | | | | |
| `id-kubelet` | | | | | | | ✓ |

Each identity has **only** the permissions it needs. For example, the CRM API can access SQL and Key Vault but cannot call Azure OpenAI — only the agent identities can do that. If one service is compromised, the blast radius is limited to its specific permissions.

### Workload Identity Federation

Each managed identity has a **federated credential** — a trust rule that says "only a specific Kubernetes service account in a specific namespace on a specific AKS cluster can use this identity." This prevents one service from impersonating another:

| Identity | K8s Service Account | Namespace | Federation Subject |
| --- | --- | --- | --- |
| `id-bff` | `sa-bff` | `contoso` | `system:serviceaccount:contoso:sa-bff` |
| `id-crm-api` | `sa-crm-api` | `contoso` | `system:serviceaccount:contoso:sa-crm-api` |
| `id-crm-mcp` | `sa-crm-mcp` | `contoso` | `system:serviceaccount:contoso:sa-crm-mcp` |
| `id-know-mcp` | `sa-know-mcp` | `contoso` | `system:serviceaccount:contoso:sa-know-mcp` |
| `id-crm-agt` | `sa-crm-agent` | `contoso` | `system:serviceaccount:contoso:sa-crm-agent` |
| `id-prod-agt` | `sa-prod-agent` | `contoso` | `system:serviceaccount:contoso:sa-prod-agent` |
| `id-orch` | `sa-orch-agent` | `contoso` | `system:serviceaccount:contoso:sa-orch-agent` |

The federation is a **three-way lock** — all three conditions must be true:

1. The token must come from **this specific AKS cluster** (verified by the cluster's OIDC issuer URL)
2. The token must be for **this specific service account in this specific namespace** (e.g., `sa-bff` in `contoso`)
3. Only then does Azure AD issue a token for **this specific managed identity** (e.g., `id-bff`)

This means: a pod running as `sa-bff` cannot use `id-crm-api`'s identity. A pod in a different namespace cannot use any of these identities. Even if someone deploys a rogue pod in the same cluster, it can't access Azure resources unless it matches an exact service account + namespace + identity combination.

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

Only two services are reachable from the internet. Everything else is internal.

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

Only the Blazor WASM UI (static files — HTML, CSS, JS, no secrets) and BFF API are publicly accessible via path-based routing on BFF's hostname. All other services are internal to the AKS cluster — they have `ClusterIP` service types, meaning Kubernetes only assigns them an internal IP address that is unreachable from outside the cluster. The BFF acts as the security perimeter: every user request passes through it, gets its JWT validated, and only then is forwarded to internal services.

### TLS

A self-signed TLS certificate is stored in Key Vault and referenced by the Gateway API TLS configuration. The certificate's CN and SAN match the App Gateway for Containers frontend FQDN.

## Image Security

Product images are stored in a **private** Blob Storage container. The browser never gets a direct URL to Azure Storage (which would bypass authentication):

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
| Entra ID | BFF client ID, tenant ID, hostname (no client secret — the Blazor app is a browser-based SPA) |
| Workload Identities | Client IDs for all 7 service identities |
| Customers | Passwords + Entra object IDs for 5 test customers |

Key Vault uses Azure RBAC to control who can read and write secrets:

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
