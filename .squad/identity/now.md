---
updated_at: 2026-04-22T00:00:00.000Z
focus_area: Audit-driven cleanup — docs/code/charts now match intent
active_issues: []
---

# What We're Focused On

The codebase, docs, and squad metadata have all been re-aligned with the framework's stated intent (see `docs/business-scenario.md` and `docs/implementation-plan.md`):

- All 8 components (CRM API, CRM MCP, Knowledge MCP, CRM/Product/Orchestrator agents, BFF API, Blazor UI) now ship with their own Dockerfile and Helm chart.
- Conversation history is plumbed end-to-end: BFF → Orchestrator → specialist agents.
- SignalR is removed from docs (the codebase always used HTTP/JSON; the docs were aspirational).
- Tool counts, ports, and per-component README defaults match the implementation.

Next focus: stay vigilant on doc/code drift after every major implementation push — the audit pattern is now captured in `wisdom.md`.

