# Project Context

- **Owner:** Almir Banjanovic
- **Project:** .NET Agent Framework — 8-container agentic AI system with Contoso Outdoors (Blazor WASM UI, BFF API, CRM API, CRM MCP, Knowledge MCP, CRM Agent, Product Agent, Orchestrator Agent)
- **Stack:** .NET 9, Minimal APIs, Blazor WebAssembly, MudBlazor, ModelContextProtocol C# SDK, Microsoft.Agents.AI, Azure.AI.OpenAI, Cosmos DB, Azure AI Search, Terraform, AKS, Helm, Docker
- **Created:** 2026-03-19

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

### 2026-03-19 — Frontend Analysis: blazor-ui Not Yet Implemented

**Finding:** The `src/blazor-ui/` directory does not exist on disk. The Blazor WASM UI is fully designed in the architecture (README, docs, repo structure docs) but has zero implementation. The solution file (`dotnet-agent-framework.sln`) only contains `simple-agent`, `config-sync`, and `seed-data`.

**Architecture Spec (from README):**
- **Project path:** `src/blazor-ui/`
- **Type:** Blazor WebAssembly SPA, deployed as static files in its own container
- **Stack:** MudBlazor components, Microsoft.Authentication.Msal (PKCE), SignalR.Client for streaming
- **Markdown:** Markdig — renders agent responses with image src rewriting to `/api/images/{filename}`
- **Auth:** MSAL PKCE flow, sends Bearer tokens to BFF API
- **BFF dependency:** `src/bff-api/` (also not yet implemented)
- **Traffic paths:** Browser → Blazor UI → BFF API (HTTP + SignalR)
- **Image proxy:** BFF proxies blob bytes; browser never gets a direct storage URL
- **Testing:** bUnit planned

**Key file paths to create when implementation begins:**
- `src/blazor-ui/blazor-ui.csproj`
- `src/blazor-ui/Program.cs`
- `src/blazor-ui/wwwroot/index.html`
- `src/blazor-ui/Pages/` (Chat.razor, etc.)
- `src/blazor-ui/Shared/` (MainLayout.razor, ChatPanel.razor, ChatMessage.razor)
- `src/blazor-ui/Services/` (ChatService.cs, etc.)

**Patterns from existing projects:**
- Each project is fully independent — own models, own Dockerfile, own Helm chart
- No shared project references; communication is HTTP/JSON only
- Shared `appsettings.json` referenced via relative path from `src/`

**Companion projects also not yet implemented:**
- `src/bff-api/` — BFF API (JWT validation, CRM proxy, image proxy, chat, conversation persistence)
- `src/crm-api/` — CRM Domain API (11 endpoints)
- All MCP servers and agents
- Only dev tools exist: `config-sync`, `seed-data`, `simple-agent`

### 2026-03-19 — Cross-Team Finding: Full Codebase Analysis Complete

**Team Update (from all 5 agents):** Architecture is fully specced and infrastructure is provisioned, but **zero application code exists yet.** This is the intended state at end of Phase 1 (infrastructure/tooling complete). All 5 agents confirm the critical path: CRM API is the foundation. Blazor UI depends on BFF API, which depends on CRM API and Orchestrator. Implementation order: CRM API → MCP Servers → Agents → BFF → UI. No fundamental re-design needed. All decisions merged into `.squad/decisions.md` with consensus.

### 2026-04-23 — Blazor WASM UI Implemented (Component 8)

**Finding:** Added the Contoso.BlazorUi WebAssembly frontend with MudBlazor layout, chat experience, orders/profile pages, and Markdig markdown rendering with image rewrite support. A dev auth selector now injects `X-Customer-Id` headers, and the UI is wired into the Aspire AppHost on port 5008 with BFF CORS default updated accordingly.
