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
| **Managed identity** | An Azure-managed identity for a non-agent service (not a human). Instead of storing a database password in an environment variable, the service gets an identity that Azure trusts. The service calls `DefaultAzureCredential()` and Azure issues a short-lived token automatically. No passwords to rotate, no secrets to leak. Used by BFF, CRM API, MCP servers, and kubelet. |
| **Agent identity** | An Entra ID service principal with "agent" subtype, created from an Agent Identity Blueprint. Represents an AI agent in the directory — visible in the Entra Admin Center under Agent ID. Supports the same federated identity credentials as managed identities (including AKS workload identity), and can be assigned Azure RBAC roles. Used by CRM Agent, Product Agent, and Orchestrator Agent. See [Agent Identity Platform](#agent-identity-platform-entra-agent-id). |
| **Agent Identity Blueprint** | A reusable template (Entra application registration) that defines a "kind" of agent. E.g., "Contoso CRM Agent" is a blueprint; the actual agent instance running in dev is created from it. Blueprints enable Conditional Access policies across all instances of an agent type, centralized permission management, and governance at scale. |
| **Workload Identity** | The AKS-specific mechanism that connects a Kubernetes pod to an Azure identity (managed identity or agent identity). It uses the cluster's OIDC issuer (a certificate-based trust) to prove "this pod in this namespace is authorized to use this identity." |
| **RBAC** | Role-Based Access Control. Azure's permission system. Each identity (managed or agent) is granted specific roles (e.g., "Key Vault Secrets User", "Cosmos DB Data Contributor") scoped to specific resources. |
| **Human-in-the-loop** | A pattern where an AI agent pauses before executing a sensitive operation (e.g., canceling an order) and requests explicit approval from the user through the chat UI. See [Human-in-the-Loop](#human-in-the-loop-consent). |
| **OIDC** | OpenID Connect. A standard identity protocol built on top of OAuth 2.0. Used here in two places: (1) Entra ID uses OIDC for user login, and (2) AKS uses OIDC to federate pod identities with Azure AD. |
| **CORS** | Cross-Origin Resource Sharing. A browser security feature that prevents JavaScript on one website from calling APIs on a different website. The BFF explicitly allows requests from the Blazor WASM UI's origin. |

## Overview

The security model has **three authentication flows** that solve different problems:

| | User Authentication | Non-Agent Service Authentication | Agent Authentication |
| --- | --- | --- | --- |
| **Question answered** | "Which *human* is making this request?" | "Which *service* is calling this Azure resource?" | "Which *agent* is acting, and for which user?" |
| **Who authenticates** | Customer (Emma, James, Sarah...) | Kubernetes pod (crm-api, bff, crm-mcp...) | Kubernetes pod (crm-agent, prod-agent, orch-agent) |
| **Identity type** | Entra ID user account | Managed identity | Agent identity (Entra Agent ID) |
| **Authenticates to** | BFF API (validates JWT) | Azure resources (Cosmos DB, Blob, Search) | Azure resources (OpenAI, Key Vault) |
| **Token issuer** | Entra ID (`login.microsoftonline.com`) | AKS OIDC issuer → Azure AD | AKS OIDC issuer → Azure AD |
| **Token type** | JWT access token (Bearer header) | Managed identity token (`DefaultAzureCredential()`) | Agent identity token (`DefaultAzureCredential()`) |
| **Purpose** | Data scoping — "show me only *my* orders" | Resource access — "let this pod connect to Cosmos DB" | Agent resource access + identity for consent and audit |
| **Without it** | Anyone could see all customer data | Services would need hardcoded passwords | Agents would be invisible in Entra — no governance, no consent tracking |

All three flows are required. User auth controls *whose data* is returned. Service auth and agent auth control *which Azure resources* a pod can reach — the difference is that agent identities are first-class objects in Entra's Agent ID platform, enabling governance, Conditional Access, and human-in-the-loop consent that managed identities can't provide.

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
⑤ CRM API connects to Cosmos DB using that managed identity token (no password needed)
⑥ CRM API queries: SELECT * FROM c WHERE c.entra_id = '<emma-oid>' (in Customers container)
⑦ Only Emma's orders are returned — scoped by user auth, accessed by workload identity
```

Step ③ uses **user authentication** (Entra ID) — it determines *whose* data to query.
Steps ④–⑤ use **service authentication** (Workload Identity) — they determine *how* the CRM API
connects to Cosmos DB without any stored credentials.

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

The `Customer` role gates access to the application itself (if you don't have this role, you can't use the app at all). However, within the app, data access is controlled by **identity** — the system uses each customer's unique Entra ID (`entra_id` field in the Customers container) to filter data, so Emma can only see Emma's orders regardless of her role.

### Test Users

Terraform creates 5 customer test users in Entra ID, matching the pre-seeded customers in Cosmos DB:

| User | UPN | Customer ID | Loyalty | Scenario |
| --- | --- | --- | --- | --- |
| Emma Wilson | `emma.wilson@{domain}` | 101 | Silver | 1 — Order tracking |
| James Chen | `james.chen@{domain}` | 102 | Bronze | 2 — Return/sizing |
| Sarah Miller | `sarah.miller@{domain}` | 103 | Gold | 3 — Promotions |
| David Park | `david.park@{domain}` | 104 | Silver | 4 — Damaged item |
| Lisa Torres | `lisa.torres@{domain}` | 105 | Bronze | 5 — Product recommendation |

Passwords follow the pattern `Contoso-<Animal>-<4digits>!#` and are stored in Key Vault as `Customer--{Name}Password` (PascalCase--Hierarchy convention, e.g., `Customer--EmmaPassword`, `Customer--JamesPassword`, `Customer--SarahPassword`). Each user's Entra object ID is linked to their customer record in the Cosmos DB Customers container (via the `entra_id` field) during deployment.

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

- **All queries are scoped to the authenticated customer's entra_id** → customers can only see their own data
- **New customer auto-provisioning** → if a user signs in but no matching `entra_id` exists in the Customers container, the CRM API creates a new customer record automatically
- **Support ticket creation** → any authenticated customer can create support tickets, but only for their own orders

### Agent Layer

Agents receive customer context from the Orchestrator via `X-Customer-Entra-Id` header:

- **All MCP tool calls are scoped to the authenticated customer's entra_id**
- **Agents don't need to "look up" the customer** — the identity is known from the token
- **Orchestrator** → propagates `X-Customer-Entra-Id` to specialist agents

### How Entra Users Link to Cosmos DB Customers

Customers exist in two places: Entra ID (for login) and Cosmos DB (for data). The `entra_id` field in the Customers container bridges them.

**During deployment (Lab 1, Phase 7):**

```text
Terraform creates 5 Entra ID test users     →  Entra object IDs stored in Key Vault
Terraform creates 6 Cosmos DB containers     →  Customers container seeded from CSV
Deploy script reads OIDs from Key Vault      →  Updates entra_id field on each Customer

  Entra ID                         Cosmos DB (Customers container)
  ┌───────────────────────┐        ┌─────────────────────────────────┐
  │ Emma Wilson           │        │ { "id": "101",                  │
  │ OID: abc-123-...      │ -----> │   "name": "Emma Wilson",        │
  │ UPN: emma.wilson@...  │        │   "entra_id": "abc-123-...",    │
  └───────────────────────┘        │   "loyalty_tier": "Silver" }    │
                                   └─────────────────────────────────┘
```

**At runtime (every request):**

```text
① Emma logs in → JWT contains oid claim = "abc-123-..."
② BFF extracts oid → passes as X-Customer-Entra-Id header
③ CRM API queries: SELECT * FROM c WHERE c.entra_id = "abc-123-..."
④ Only Emma's data is returned — orders, tickets, promotions
```

Without the `entra_id` link, the system would know *someone* logged in but couldn't determine *whose* data to show.

## Identity Flows — End-to-End Scenarios

This section shows how all three identity types (user, managed, agent) work together during real user interactions.

### Scenario 1: "Show me my orders" (Read — No Agent Involved)

```text
┌─────────┐     ┌─────────┐     ┌─────────┐     ┌───────────┐
│  Emma's │     │         │     │         │     │           │
│ Browser │---->│   BFF   │---->│ CRM API │---->│ Cosmos DB │
│ (Blazor)│     │         │     │         │     │   (CRM)   │
└─────────┘     └─────────┘     └─────────┘     └───────────┘

Identity flow:
  Emma (Entra user)  --JWT-->  BFF validates JWT, extracts oid
                               BFF passes X-Customer-Entra-Id header
  id-crm-api (managed identity)  --token-->  Cosmos DB
                               CRM API filters: WHERE c.entra_id = '<oid>'

Identities used:
  ✓ User identity   — Emma's JWT (determines WHOSE data)
  ✓ Managed identity — id-crm-api (determines HOW to access Cosmos DB)
  ✗ Agent identity   — not involved (no AI interaction)
```

### Scenario 2: "What's the status of my order?" (Agent Chat — Read Only)

```text
┌─────────┐    ┌─────┐    ┌──────┐    ┌─────────┐    ┌─────────┐    ┌──────────┐
│  Emma's │    │     │    │ Orch │    │   CRM   │    │   CRM   │    │          │
│ Browser │--->│ BFF │--->│Agent │--->│  Agent  │--->│   MCP   │--->│Cosmos DB │
│ (chat)  │    │     │    │      │    │         │    │         │    │  (CRM)   │
└─────────┘    └─────┘    └──────┘    └─────────┘    └─────────┘    └──────────┘

Identity flow:
  Emma (Entra user)      --JWT-->  BFF validates, extracts oid
  Orch Agent (agent ID)  --token-->  Azure OpenAI (intent classification)
  CRM Agent (agent ID)   --token-->  Azure OpenAI (tool selection)
  id-crm-mcp (managed)   --token-->  Cosmos DB (execute get_orders tool)

Identities used:
  ✓ User identity    — Emma's JWT (determines WHOSE orders to retrieve)
  ✓ Managed identity — id-crm-mcp (CRM MCP connects to Cosmos DB)
  ✓ Agent identity   — Orchestrator + CRM Agent (call Azure OpenAI for reasoning)

Why agent identity here?
  The Orchestrator and CRM Agent call Azure OpenAI to understand intent and
  select MCP tools. These calls appear in Entra sign-in logs as "Contoso
  Orchestrator Agent" and "Contoso CRM Agent" — not as anonymous managed
  identities. This enables audit trails showing which agent made which
  AI inference call.
```

### Scenario 3: "Cancel my order #1023" (Agent Chat — Write with Consent)

```text
┌─────────┐    ┌─────┐    ┌──────┐    ┌─────────┐    ┌──────────────┐    ┌──────────┐
│  Emma's │    │     │    │ Orch │    │   CRM   │    │  CRM MCP     │    │          │
│ Browser │--->│ BFF │--->│Agent │--->│  Agent  │--->│ cancel_order │--->│Cosmos DB │
│ (chat)  │    │     │    │      │    │         │    │  (HIGH sens) │    │  (CRM)   │
└─────────┘    └─────┘    └──────┘    └─────────┘    └──────────────┘    └──────────┘
                  │                        │
                  │<── consent_required ───┘
                  │    agentName: "Contoso CRM Agent"
                  │    agentObjectId: <entra-oid>
                  │
                  v
            ┌─────────────────────────────────────────────┐
            │  🛡️  Contoso CRM Agent wants to:            │
            │  Cancel order #1023 (Alpine Explorer Tent)  │
            │                                             │
            │  [Approve Once]  [Approve Session]  [Deny]  │
            └─────────────────────────────────────────────┘

Identity flow:
  Emma (Entra user)        --JWT-->  BFF validates, extracts oid
  Orch Agent (agent ID)    --token-->  Azure OpenAI
  CRM Agent (agent ID)     --token-->  Azure OpenAI -> decides cancel_order
  CRM Agent                --returns consent_required to BFF
  BFF                      --checks consent records in Cosmos DB (Agents account)
  id-crm-mcp (managed)     --token-->  Cosmos DB (execute cancel_order after approval)

Why agent identity is critical here:
  The consent dialog shows "Contoso CRM Agent wants to..." — this name
  comes from the agent identity in Entra, not a hardcoded string. If
  an admin disables the CRM Agent blueprint in Entra, the consent
  records become invalid and the agent can no longer perform write ops.

  This is governance: the agent's ability to act is controlled by its
  Entra identity status, not just by code deployment.
```

### Scenario 4: "What hiking boots do you recommend?" (Knowledge Search)

```text
┌─────────┐    ┌─────┐    ┌──────┐    ┌─────────┐    ┌──────────┐    ┌───────────┐
│  Emma's │    │     │    │ Orch │    │ Product │    │Knowledge │    │ AI Search │
│ Browser │--->│ BFF │--->│Agent │--->│  Agent  │--->│   MCP    │--->│  (index)  │
│ (chat)  │    │     │    │      │    │         │    │          │    │           │
└─────────┘    └─────┘    └──────┘    └─────────┘    └──────────┘    └───────────┘

Identity flow:
  Emma (Entra user)         --JWT-->  BFF validates
  Orch Agent (agent ID)     --token-->  Azure OpenAI (routes to Product Agent)
  Product Agent (agent ID)  --token-->  Azure OpenAI (generates search query)
  id-know-mcp (managed)     --token-->  AI Search (searches knowledge base)

Identities used:
  ✓ User identity    — Emma's JWT (user context, though no data scoping here)
  ✓ Managed identity — id-know-mcp (Knowledge MCP reads AI Search via RBAC)
  ✓ Agent identity   — Orchestrator + Product Agent (AI reasoning calls)

Note: Knowledge search is NOT customer-scoped — product info, sizing guides,
and policies are the same for all customers. The user identity still flows
through for audit purposes but doesn't filter results.
```

### Identity Type Summary

| Identity Type | Created By | Where It Lives | When It's Used | Governance |
| --- | --- | --- | --- | --- |
| **User (Entra ID)** | Terraform (`entra/v1`) | Entra ID directory | Every HTTP request from browser | Password policies, MFA, Conditional Access |
| **Managed Identity** | Terraform (`identity/v1`) | Azure resource | Pod → Azure resource calls (Cosmos DB, Storage, Search) | Azure RBAC only |
| **Agent Identity** | Terraform (`agent-identity/v1`) | Entra Agent ID platform | Pod → Azure OpenAI calls, consent tracking | RBAC + Agent ID portal + Conditional Access + audit logs + M365 Copilot discovery |

### Microsoft 365 Copilot Integration

Agent identities created via Entra Agent ID are discoverable in the **Microsoft 365 Copilot** ecosystem. When an organization deploys agents using Agent Identity Blueprints:

- **IT admins** see all agents in **Entra Admin Center → Agent ID** — grouped by blueprint, with sign-in activity, owners, and status
- **Microsoft 365 Copilot** can discover agents registered via blueprints, enabling them to appear in Copilot's agent catalog (when published)
- **Conditional Access policies** can target agent blueprints — e.g., "require all CRM agents to only authenticate from trusted networks" or "disable all agents of this type during an incident"
- **Entra sign-in logs** show agent activity alongside human user activity, with the blueprint relationship visible for each sign-in event

This is why agents use Agent Identity Blueprints instead of plain managed identities — managed identities are invisible in the Agent ID portal and can't participate in the M365 Copilot agent ecosystem.

## Service Authentication (Workload Identity)

This section covers how backend services (running as containers in Kubernetes) authenticate to Azure resources like Cosmos DB, Blob Storage, and AI Search — without any hardcoded passwords.

### The problem Workload Identity solves

The CRM API needs to connect to Cosmos DB. Traditionally, you'd put a database password in an environment variable or config file. This is risky — passwords can leak through logs, crash dumps, or source control. Workload Identity eliminates passwords entirely: each service gets an Azure identity (managed identity for non-agent services, agent identity for agents), and Azure handles the token exchange automatically.

### How services authenticate to Azure

Each service runs in AKS (Azure Kubernetes Service) with its own identity. Non-agent services use **managed identities**; agents use **agent identities** from the [Entra Agent ID platform](#agent-identity-platform-entra-agent-id). Both use the same workload identity federation mechanism — the only difference is the identity type. Here's how it works:

```text
What Terraform sets up (one-time, during deployment):
  ① Creates an identity:
     - Non-agent services: managed identity (e.g., "id-crm-api")
     - Agents: agent identity blueprint + instance (e.g., "Contoso CRM Agent")
  ② Grants that identity specific permissions (e.g., "can access Cosmos DB")
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
  ⑧ Azure AD issues an access token for the identity
  ⑨ The pod uses that token to call Cosmos DB, Azure OpenAI, etc.
     (no password, no API key — just identity-based access)
```

The developer experience is simple: call `DefaultAzureCredential()` and it works — whether the backing identity is a managed identity or an agent identity. All the complexity is handled by the infrastructure.

### Managed Identities (Non-Agent Services)

Non-agent services use standard Azure managed identities:

| Identity | Service | Purpose |
| --- | --- | --- |
| `id-bff` | BFF API | Access Cosmos DB (CRM + conversations), read Blob Storage (image proxy), read Key Vault |
| `id-crm-api` | CRM API | Access Cosmos DB (CRM), read Key Vault |
| `id-crm-mcp` | CRM MCP Server | Access Cosmos DB (CRM), read Key Vault |
| `id-know-mcp` | Knowledge MCP Server | Read AI Search index, read Key Vault |
| `id-kubelet` | AKS kubelet | Pull images from ACR |

### Agent Identities (Entra Agent ID)

Agents use [Entra Agent ID](https://learn.microsoft.com/en-us/entra/agent-id/identity-platform/agent-identities) identities — service principals with an "agent" subtype, created from Agent Identity Blueprints. They appear as first-class agent objects in the Entra Admin Center and support the same AKS federated identity credentials as managed identities.

| Agent Identity | Blueprint | Service | Purpose |
| --- | --- | --- | --- |
| Contoso CRM Agent (dev) | Contoso CRM Agent | CRM Agent | Call Azure OpenAI, read Key Vault |
| Contoso Product Agent (dev) | Contoso Product Agent | Product Agent | Call Azure OpenAI, read Key Vault |
| Contoso Orchestrator Agent (dev) | Contoso Orchestrator Agent | Orchestrator Agent | Call Azure OpenAI, read Key Vault |

Why agent identities instead of managed identities for agents?

- **Discoverability** — Entra Admin Center → Agent ID shows all agents in the tenant
- **Governance** — Assign owners, sponsors, and managers to each agent
- **Conditional Access** — Apply CA policies to all instances of a blueprint (e.g., disable all CRM agents)
- **Audit** — Sign-in logs show the agent identity with its blueprint relationship
- **Human-in-the-loop** — Agent identity enables consent tracking and approval flows (see [Human-in-the-Loop](#human-in-the-loop-consent))

### RBAC Matrix

Each identity is granted **only** the permissions it needs (least privilege). This table shows which identity can access which resource:

| Identity | Type | Key Vault Secrets User | Cosmos DB CRM Data Contributor | OpenAI User | Cosmos DB Agents Data Contributor | Search Index Reader | Blob Data Reader | ACR Pull |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `id-bff` | Managed | ✓ | ✓ | | ✓ | | ✓ | |
| `id-crm-api` | Managed | ✓ | ✓ | | | | | |
| `id-crm-mcp` | Managed | ✓ | ✓ | | | | | |
| `id-know-mcp` | Managed | ✓ | | | | ✓ | | |
| Contoso CRM Agent | Agent | ✓ | | ✓ | ✓ | | | |
| Contoso Product Agent | Agent | ✓ | | ✓ | ✓ | | | |
| Contoso Orchestrator Agent | Agent | ✓ | | ✓ | ✓ | | | |
| `id-kubelet` | Managed | | | | | | | ✓ |

Each identity has **only** the permissions it needs. For example, the CRM API can access Cosmos DB (CRM) and Key Vault but cannot call Azure OpenAI — only the agent identities can do that. If one service is compromised, the blast radius is limited to its specific permissions. Agent identities receive the same RBAC roles as the managed identities they replaced — the difference is in how they're represented in Entra (as agent objects, not generic managed identities).

### Workload Identity Federation

Each identity (managed or agent) has a **federated credential** — a trust rule that says "only a specific Kubernetes service account in a specific namespace on a specific AKS cluster can use this identity." This prevents one service from impersonating another.

**Non-agent services** (managed identity → AKS service account):

| Identity | K8s Service Account | Namespace | Federation Subject |
| --- | --- | --- | --- |
| `id-bff` | `sa-bff` | `contoso` | `system:serviceaccount:contoso:sa-bff` |
| `id-crm-api` | `sa-crm-api` | `contoso` | `system:serviceaccount:contoso:sa-crm-api` |
| `id-crm-mcp` | `sa-crm-mcp` | `contoso` | `system:serviceaccount:contoso:sa-crm-mcp` |
| `id-know-mcp` | `sa-know-mcp` | `contoso` | `system:serviceaccount:contoso:sa-know-mcp` |

**Agent services** (agent identity → AKS service account):

| Agent Identity | K8s Service Account | Namespace | Federation Subject |
| --- | --- | --- | --- |
| Contoso CRM Agent | `sa-crm-agent` | `contoso` | `system:serviceaccount:contoso:sa-crm-agent` |
| Contoso Product Agent | `sa-prod-agent` | `contoso` | `system:serviceaccount:contoso:sa-prod-agent` |
| Contoso Orchestrator Agent | `sa-orch-agent` | `contoso` | `system:serviceaccount:contoso:sa-orch-agent` |

Agent identity FIC uses the same issuer/subject/audience pattern as managed identity FIC. The only difference is the parent resource: managed identity FIC is on `azurerm_user_assigned_identity`, while agent identity FIC is on `azuread_application` (the blueprint).

The federation is a **three-way lock** — all three conditions must be true:

1. The token must come from **this specific AKS cluster** (verified by the cluster's OIDC issuer URL)
2. The token must be for **this specific service account in this specific namespace** (e.g., `sa-bff` in `contoso`)
3. Only then does Azure AD issue a token for **this specific identity** (e.g., `id-bff` for non-agent services, or `Contoso CRM Agent` for agents)

This means: a pod running as `sa-bff` cannot use `id-crm-api`'s identity. A pod in a different namespace cannot use any of these identities. Even if someone deploys a rogue pod in the same cluster, it can't access Azure resources unless it matches an exact service account + namespace + identity combination. This applies equally to managed identities and agent identities.

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

Manifests are in `infra/k8s/manifests/` and applied via `kubectl_manifest` resources. For agents, the `client-id` annotation references the agent identity's application (client) ID instead of a managed identity's client ID — but `DefaultAzureCredential()` works identically in both cases.

## Agent Identity Platform (Entra Agent ID)

The Contoso Outdoors framework uses [Microsoft Entra Agent ID](https://learn.microsoft.com/en-us/entra/agent-id/identity-platform/what-is-agent-id) to give AI agents first-class identities in the directory. This is a deliberate architectural choice — agents are not generic services, and their identities should reflect that.

> **Infrastructure: Entra Agent ID Platform**
>
> Agent identities are provisioned using the Microsoft Graph beta API via the `microsoft/msgraph` Terraform provider. During infrastructure deployment:
> - **Agent Identity Blueprints** and **Blueprint Principals** are created via Terraform (`modules/agent-identity/v1`)
> - **Agent Identity instances** are created at runtime by the blueprint service (per [Microsoft documentation](https://learn.microsoft.com/en-us/entra/agent-id/identity-platform/what-is-agent-id-platform))
>
> **Prerequisites:**
> - Microsoft 365 Copilot license (required for Entra Agent ID)
> - Frontier program enrollment with Microsoft
>
> For more details, see [Entra Agent ID Platform documentation](https://learn.microsoft.com/en-us/entra/agent-id/identity-platform/what-is-agent-id-platform).

### Blueprint → Agent Identity hierarchy

```text
Agent Identity Blueprint (template — defines a "kind" of agent)
  "Contoso CRM Agent"
    └── Agent Identity (instance — the actual runtime identity)
          "Contoso CRM Agent (dev)"
            ├── Federated Identity Credential → AKS service account sa-crm-agent
            ├── Azure RBAC: Key Vault Secrets User, Cognitive Services OpenAI User
            └── Appears in: Entra sign-in logs, Agent ID portal, Conditional Access
```

Three blueprints exist, one per agent type:

| Blueprint | Agent Identity Instance | Why separate |
| --- | --- | --- |
| Contoso CRM Agent | Contoso CRM Agent (dev) | Different MCP tools, different consent scopes |
| Contoso Product Agent | Contoso Product Agent (dev) | Different MCP tools, different consent scopes |
| Contoso Orchestrator Agent | Contoso Orchestrator Agent (dev) | Routes to specialists, no direct tool access |

### What agent identities enable

- **Discoverability** — Entra Admin Center → Agent ID → All agent identities shows all agents in the tenant, grouped by blueprint
- **Conditional Access** — Apply a CA policy to the "Contoso CRM Agent" blueprint → affects all CRM agent instances across all environments
- **Governance** — Each agent can have an owner (technical admin), sponsor (business accountable), and manager
- **Audit** — Entra sign-in logs show the agent identity as the client with the blueprint relationship, not a generic managed identity
- **Human-in-the-loop** — Agent identity provides the "who" in consent tracking: "*Contoso CRM Agent* wants to cancel your order. Allow?"

### How it works at runtime

```text
① AKS injects a short-lived token into the agent pod
② Pod calls DefaultAzureCredential() — same code as managed identity
③ Azure AD verifies: token is from trusted AKS cluster + correct service account
④ Azure AD issues an access token for the agent identity
⑤ Pod uses that token to call Azure OpenAI, Key Vault, etc.
```

The developer experience is identical to managed identities — `DefaultAzureCredential()` works transparently. The difference is entirely in how the identity appears in Entra: as a purpose-built agent object, not a generic managed identity.

## Human-in-the-Loop Consent

The framework supports a **human-in-the-loop** pattern where agents pause before executing sensitive operations and request explicit user approval.

### When consent is required

MCP tools are classified by sensitivity:

| Sensitivity | Consent | Examples |
| --- | --- | --- |
| **High** | Always required | `cancel_order`, `update_customer` |
| **Medium** | User-configurable | `create_support_ticket`, `apply_promotion` |
| **Low** | Never required | All read operations (`get_orders`, `get_products`, etc.) |

### How the consent flow works

```text
① User: "Cancel my order #1023"
② Orchestrator Agent classifies intent → routes to CRM Agent
③ CRM Agent decides to call cancel_order tool → checks sensitivity → HIGH
④ CRM Agent returns a structured consent_required response:
   {
     "type": "consent_required",
     "agentName": "Contoso CRM Agent",
     "scope": "Orders.Cancel",
     "toolName": "cancel_order",
     "description": "Cancel order #1023 (Alpine Explorer Tent, $349.99)"
   }
⑤ BFF intercepts → checks Cosmos DB for existing consent
   - If pre-approved (user chose "always allow"): auto-approve, execute tool
   - If no consent: emit ConsentRequested event via SignalR
⑥ Blazor UI shows inline consent card in chat:
   ┌───────────────────────────────────────────────┐
   │  🛡️  Contoso CRM Agent wants to:              │
   │  Cancel order #1023 (Alpine Explorer Tent)    │
   │                                               │
   │  [Approve Once]  [Approve Session]  [Deny]    │
   └───────────────────────────────────────────────┘
⑦ User clicks Approve → BFF records consent in Cosmos DB → replays tool call
⑧ CRM Agent executes cancel_order → returns result → chat resumes
```

### Consent storage

Consent records are stored in the Cosmos DB agents account (`consent-records` container, partition key: `/userId`):

| Field | Description |
| --- | --- |
| `userId` | Human user's Entra object ID (partition key) |
| `agentObjectId` | Agent identity's object ID (from Entra Agent ID) |
| `agentName` | Human-readable agent name (e.g., "Contoso CRM Agent") |
| `scope` | Consent category (e.g., `Orders.Cancel`) |
| `toolName` | Specific MCP tool (e.g., `cancel_order`) |
| `status` | `pending`, `approved`, `denied` |
| `granularity` | `once` (this action only), `session` (this conversation), `always` (permanent) |

### User consent settings

Users can pre-configure their consent preferences in a settings page:

- "Always allow CRM Agent to create support tickets" → `granularity = always` for `Tickets.Create`
- "Always ask before canceling orders" → no pre-approval for `Orders.Cancel`
- Users can revoke pre-approvals at any time

### Why this ties to agent identity

The consent record references the agent's Entra object ID — not a generic managed identity principal. This means:

- Consent is tied to a specific, named agent in the directory
- If an agent identity is disabled in Entra, its consent records become invalid
- Audit trails show exactly which named agent was approved to perform which action

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

### Azure Resource Firewalls

Every data-plane Azure resource uses a **default deny** firewall with two controlled access paths:

1. **Private endpoints** — AKS pods reach all resources through private endpoints on the `snet-private-endpoints` subnet (same VNet). Private DNS zones resolve service FQDNs to private IPs so traffic never leaves the VNet.
2. **Azure services bypass** — Azure-to-Azure platform traffic (e.g., AI Search indexer reading Blob Storage) is explicitly allowed through each resource's firewall.

| Resource | Firewall Default | Azure Services Bypass | Private Endpoint | Deployer IP Exception |
| --- | --- | --- | --- | --- |
| **Storage Account** | Deny | `bypass = ["AzureServices"]` | `pe-st-*` (blob) | ✓ |
| **Key Vault** | Deny (when IPs configured) | `bypass = "AzureServices"` | `pe-kv-*` (vault) | ✓ |
| **ACR** | Allow (public) | `network_rule_bypass_option = "AzureServices"` | `pe-acr-*` (registry) | — |
| **Cosmos DB** (CRM) | Allow (public, IP-filtered) | `0.0.0.0` in `ip_range_filter` | `pe-cosmos-crm-*` (Sql) | ✓ |
| **Cosmos DB** (Agents) | Allow (public, IP-filtered) | `0.0.0.0` in `ip_range_filter` | `pe-cosmos-*` (Sql) | ✓ |
| **Azure OpenAI** | Deny | Trusted services (implicit via managed identity + RBAC) | `pe-aif-*` (account) | ✓ |
| **AI Search** | IP-restricted | No bypass parameter available | `pe-srch-*` (searchService) | ✓ |

The **deployer IP exception** is the public IP of the machine running `terraform apply`. It is added automatically during deployment and allows Terraform to reach service endpoints for provisioning. These rules can be removed post-deployment when all runtime access flows through private endpoints.

### Kubernetes Network Policies

In addition to Azure resource firewalls, the cluster enforces **pod-level network segmentation** using Kubernetes NetworkPolicy resources (Azure Network Policy provider). All policy manifests live in `infra/k8s/manifests/network-policies/`.

**Default-deny posture:** `default-deny.yaml` blocks all ingress and egress traffic namespace-wide (pod selector `{}`, policy types Ingress + Egress). Every pod must have an explicit allow rule or it cannot send or receive any traffic.

**Per-service allow rules** (one YAML per service):

| Service | Ingress From | Egress To |
| --- | --- | --- |
| `blazor-ui` | `azure-alb-system` namespace → port 8080 | `kube-dns` (port 53) only — serves static files |
| `bff-api` | `azure-alb-system` namespace → port 8080 | `kube-dns`, `orchestrator-agent:8080`, `crm-api:8080`, private endpoints (`10.0.3.0/24:443`) |
| `orchestrator-agent` | `bff-api` → port 8080 | `kube-dns`, `crm-agent:8080`, `product-agent:8080`, private endpoints (`10.0.3.0/24:443`) |
| `crm-agent` | `orchestrator-agent` → port 8080 | `kube-dns`, `crm-mcp:8080`, `knowledge-mcp:8080`, private endpoints (`10.0.3.0/24:443`) |
| `product-agent` | `orchestrator-agent` → port 8080 | `kube-dns`, `crm-mcp:8080`, `knowledge-mcp:8080`, private endpoints (`10.0.3.0/24:443`) |
| `crm-mcp` | `crm-agent`, `product-agent` → port 8080 | `kube-dns`, `crm-api:8080`, private endpoints (`10.0.3.0/24:443`) |
| `knowledge-mcp` | `crm-agent`, `product-agent` → port 8080 | `kube-dns`, private endpoints (`10.0.3.0/24:443`) |
| `crm-api` | `crm-mcp`, `bff-api` → port 8080 | `kube-dns`, private endpoints (`10.0.3.0/24:443`) |

Traffic to Azure resources (Cosmos DB, Key Vault, OpenAI, AI Search) flows through private endpoints on the `10.0.3.0/24` subnet over port 443. DNS resolution is allowed via `kube-system/kube-dns` on port 53 (TCP/UDP). See `infra/k8s/manifests/network-policies/README.md` for full details.

## Image Security

Product images are stored in a **private** Blob Storage container. The browser never gets a direct URL to Azure Storage (which would bypass authentication):

1. Blazor WASM renders `<img src="/api/images/{filename}">`
2. BFF validates filename (`^[a-zA-Z0-9_-]+\.png$` — rejects path traversal)
3. BFF checks user is authenticated
4. BFF fetches blob bytes from Blob Storage using managed identity
5. BFF streams bytes to browser with `Content-Type: image/png`

This pattern is extensible to private per-customer images (e.g., damage claim photos) in the future.

## Container Hardening

All services use the Helm base chart (`infra/templates/helm-base/`) which enforces a locked-down container security posture by default. Individual service charts inherit these settings.

### Pod Security Context

| Setting | Value | Purpose |
| --- | --- | --- |
| `runAsNonRoot` | `true` | Prevents container from running as root |
| `runAsUser` | `1654` | `app` user from the `aspnet:9.0` base image |
| `runAsGroup` | `1654` | Same non-root group |
| `fsGroup` | `1654` | File system group for volume mounts |
| `seccompProfile.type` | `RuntimeDefault` | Restricts system calls to the container runtime's default allowlist |

### Container Security Context

| Setting | Value | Purpose |
| --- | --- | --- |
| `allowPrivilegeEscalation` | `false` | Blocks `setuid`/`setgid` privilege escalation |
| `readOnlyRootFilesystem` | `true` | Filesystem is immutable at runtime |
| `capabilities.drop` | `[ALL]` | Drops all Linux capabilities |

The only writable path is `/tmp` (an `emptyDir` volume, capped at 64Mi), used for ASP.NET temporary files. All services listen on non-privileged port **8080** (`.NET 9` default for non-root containers).

## CRM API Error Handling

The CRM API uses a `GlobalExceptionHandler` middleware (`src/crm-api/Middleware/GlobalExceptionHandler.cs`) to ensure no internal details leak to callers in production.

### Standardized error responses

All errors return [RFC 9457 ProblemDetails](https://www.rfc-editor.org/rfc/rfc9457) with consistent structure:

| Field | Value |
| --- | --- |
| `type` | `https://httpstatuses.io/{statusCode}` |
| `title` | Exception-specific (e.g., "Bad Request", "Not Found") |
| `status` | HTTP status code |
| `detail` | Environment-aware (see below) |
| `instance` | `httpContext.Request.Path` |
| `extensions.traceId` | `Activity.Current?.Id` (W3C distributed trace ID), fallback `httpContext.TraceIdentifier` |

### Environment-aware detail suppression

- **Development:** `detail` contains the full `exception.Message` — useful for debugging
- **Production (all other environments):** `detail` is the generic string `"An unexpected error occurred."` — prevents leaking stack traces, connection strings, or internal paths

### Exception → status code mapping

| Exception | Status Code | Title |
| --- | --- | --- |
| `ArgumentException` | 400 | Bad Request |
| `KeyNotFoundException` | 404 | Not Found |
| `CosmosException` (404) | 404 | Not Found |
| `CosmosException` (429) | 429 | Too Many Requests |
| `CosmosException` (503) | 503 | Service Unavailable |
| `OperationCanceledException` | 499 | Client Closed Request |
| All others | 500 | Internal Server Error |

The trace ID correlation (`Activity.Current?.Id`) enables linking error responses back to distributed traces across services.

## Key Vault

All secrets, credentials, and identity client IDs are stored in Azure Key Vault:

| Category | Secrets |
| --- | --- |
| Azure OpenAI | Endpoint, deployment names |
| Cosmos DB (CRM) | Endpoint, database name |
| Cosmos DB (Agents) | Endpoint, database name |
| Blob Storage | Endpoint, account name, container |
| AI Search | Endpoint, index name |
| Entra ID | BFF client ID, tenant ID, hostname (no client secret — the Blazor app is a browser-based SPA) |
| Workload Identities | Client IDs for 4 non-agent managed identities |
| Agent Identities | Client IDs, object IDs, and blueprint IDs for 3 agent identities |
| Customers | Passwords + Entra object IDs for 5 test customers |

Key Vault uses Azure RBAC to control who can read and write secrets:

- **Secrets Officer** → Terraform deployer (writes secrets during `terraform apply`)
- **Secrets User** → all 7 workload identities + deployer (reads secrets at startup)
- **Certificate User** → Reserved for AGC TLS integration (reads TLS cert for ingress)

## Configuration Security Pipeline

The `config-sync` tool (`src/config-sync/Program.cs`) bridges Key Vault secrets into per-component configuration files without exposing secrets in source control.

### How it works

1. Authenticates to Key Vault using `DefaultAzureCredential` (interactive `az login` locally, managed identity on AKS)
2. Reads only the secrets each component needs (defined in a per-component manifest)
3. Writes `appsettings.{Environment}.json` files into each component's `src/{component}/` directory
4. All `appsettings.*.json` files are gitignored — secrets never enter source control

### Least-privilege configuration

Each component's manifest maps specific Key Vault secrets to local config keys. A component only receives the secrets it needs:

| Component | Secrets Received | Example Mapping |
| --- | --- | --- |
| `crm-api` | 3 | `CosmosDb--CrmEndpoint` → `CosmosDb:Endpoint` |
| `crm-mcp` | 2 | `CrmApi--BaseUrl` → `CrmApi:BaseUrl` |
| `knowledge-mcp` | 6 | `Search--Endpoint` → `Search:Endpoint` |
| `crm-agent` | 4 | `AzureOpenAi--Endpoint` → `AzureOpenAi:Endpoint` |
| `product-agent` | 5 | `AzureOpenAi--DeploymentName` → `AzureOpenAi:DeploymentName` |
| `orchestrator-agent` | 5 | `CrmAgent--BaseUrl` → `CrmAgent:BaseUrl` |
| `bff-api` | 9 | `AzureAd--BffClientId` → `AzureAd:BffClientId` |
| `blazor-ui` | 3 | `Bff--BaseUrl` → `Bff:BaseUrl` |

### Key Vault naming convention

Secrets use PascalCase--Hierarchy naming: `{Category}--{Name}` (e.g., `CosmosDb--CrmEndpoint`, `AzureOpenAi--DeploymentName`, `Customer--EmmaPassword`). The `--` separator maps to .NET's `:` configuration hierarchy when consumed by components.

## Helm Secret Injection

In production (AKS), secrets are injected as environment variables through Helm — the `appsettings.{Environment}.json` files generated by `config-sync` are not needed.

### Injection chain

```text
Key Vault → External Secrets Operator / config-sync → Kubernetes Secret → Pod environment variable
```

### How it works

Each service's Helm `values.yaml` defines a `secretRefs` section that maps Kubernetes secret keys to environment variable names. For example, from `src/crm-api/chart/values.yaml`:

```yaml
secretRefs:
  - name: keyvault-secrets
    keys:
      - key: CosmosDb--CrmEndpoint
        envVar: CosmosDb__Endpoint
      - key: AzureAd--TenantId
        envVar: AzureAd__TenantId
```

The Helm base chart (`infra/templates/helm-base/`) templates these `secretRefs` into `envFrom` or individual `env` entries in the Deployment spec. The double-underscore (`__`) in environment variable names is .NET's convention for representing the `:` hierarchy separator (e.g., `CosmosDb__Endpoint` maps to `CosmosDb:Endpoint` in configuration).

The Kubernetes secrets referenced by `secretRefs` are not created by the Helm chart — they must already exist in the namespace, synced from Key Vault via the External Secrets Operator or CSI driver.

## Terraform Resources

All security resources are created by Terraform during `terraform apply`:

| Resource | Module |
| --- | --- |
| Managed identities (5 — non-agent services) | `modules/identity/v1` |
| Agent identities (3 — blueprints + instances + FIC) | `modules/agent-identity/v1` |
| RBAC role assignments | `modules/rbac/*/v1` (foundry, cosmosdb, storage, acr, aks, keyvault, search) |
| Workload identity federation (4 — non-agent services) | `modules/workload-identity/v1` |
| Entra app registration + roles | `modules/entra/v1` |
| Test users + passwords | `modules/entra/v1` (users.tf) |
| TLS certificate | `modules/tls-cert/v1` |
| K8s namespace + service accounts | `kubectl_manifest` resources (manifests/) |
| Key Vault + secrets | `modules/keyvault/v1`, `modules/keyvault-secrets/v1` |

## Known Gaps

### CI/CD Cannot Provision Agent Identity Blueprints

**What:** The `agent-identity` Terraform module uses the `msgraph` provider to create Entra Agent Identity Blueprints (application registrations with agent subtype), service principals, and federated identity credentials. This provider requires a client ID + client secret for authentication.

**Why it matters for CI/CD:** The GitHub Actions workflows authenticate to Azure via OIDC federation — the service principal has no stored client secret. The `msgraph` provider cannot use OIDC-only authentication, so `terraform apply` in CI/CD skips (or fails on) agent identity resources.

**Current impact:** Agent Identity provisioning is **local-deploy-only**. The `deploy.ps1` / `deploy.sh` scripts create a temporary client secret for the deployer SP, run Terraform with it, then delete the secret. CI/CD workflows (`deploy.yml`) cannot replicate this flow.

**Options for the future:**

| Option | Trade-off |
| --- | --- |
| CI/CD job creates a temporary secret, uses it, deletes it | Most aligned with local flow; requires SP secret-management permissions in CI/CD |
| Store a long-lived client secret in GitHub Actions secrets | Simpler but weaker security posture (secret rotation burden) |
| Accept local-only provisioning | No CI/CD risk; agent identities change infrequently |

For now, **option 3 is accepted**. Agent Identity Blueprints are created once during initial deployment and rarely change. If automated re-provisioning becomes necessary, option 1 is the recommended path.

### AI Search Admin Key in Knowledge Source Provisioning

**What:** The `knowledge-source` Terraform module uses an AI Search admin API key in a `local-exec` provisioner to create the knowledge source index.

**Why:** Azure AI Search's Knowledge Source data-plane API (2025-11-01-preview) does not support RBAC — admin key authentication is the only option.

**Risk:** The admin key briefly lives in the shell environment during local deploys. It grants full read/write/admin access to the Search service.

**Mitigation:** The key is not stored in code, config files, or state. CI/CD pipelines use OIDC (no key exposure in automation). Track the Azure SDK roadmap for RBAC data-plane support and migrate when available.

**Status:** Accepted interim risk — no alternative available.

### Test User Passwords Visible in Terraform Plan

**What:** Test user passwords (Emma, James, Sarah, David, Lisa) are visible in `terraform plan` output.

**Why:** The `keyvault-secrets` module uses `nonsensitive()` deliberately so Terraform can write passwords to Key Vault without marking them as sensitive output, which would block the write operation.

**Risk:** Passwords for 5 test accounts are visible to anyone who runs `terraform plan` or inspects plan logs.

**Mitigation:** This is a lab-only pattern. If test users persist beyond the development workshop, rotate all passwords and remove the `nonsensitive()` wrapper to revoke Terraform plan visibility.

**Status:** Accepted for workshop use.
