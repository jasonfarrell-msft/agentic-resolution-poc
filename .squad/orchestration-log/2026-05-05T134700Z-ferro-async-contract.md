| Field | Value |
|-------|-------|
| **Agent routed** | Ferro (Frontend Developer) |
| **Why chosen** | User directive clarification: frontend must NOT wait synchronously for resolution. Instead, fire POST /resolve → receive 202 with runId → navigate to progress view → poll for events. Ferro owns Blazor UI layer. Must specify async, event-driven contract. |
| **Mode** | `sync` |
| **Why this mode** | Blocking decision: Frontend contract is prerequisite for Hicks (API response shape, events endpoint) and Bishop (progress event design). UI specification drives backend surface. |
| **Files authorized to read** | `.squad/decisions/inbox/copilot-directive-2026-05-05T134319-resolve-webhook.md`, `.squad/decisions/inbox/ferro-resolve-listening-flow.md`, `.squad/decisions/inbox/hicks-resolve-webhook-contract.md`, Phase 2.5 architecture context. |
| **File(s) agent must produce** | Updated `.squad/decisions/inbox/ferro-resolve-listening-flow.md` (specified async flow: POST /resolve → 202 with runId → navigate to progress view → poll GET /api/runs/{runId}/events → display executor lanes → stop when terminal). No production code changes (Web project not yet created; specification phase). |
| **Outcome** | Completed — async, event-driven contract specified. Clarified that frontend does NOT block on resolution; instead, navigates to progress view and polls for updates. Ready for Web project implementation. |
