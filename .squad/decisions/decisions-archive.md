# Archived Project Decisions — Agentic Resolution

**Archive Date:** 2026-05-07  
**Criteria:** Decisions older than 30 days (before 2026-04-07)

---

## Architecture: Solution Split

**Date:** 2025-07-25  
**Author:** Apone (Lead/Architect)  
**Status:** ✅ Implemented by Hicks (2026-04-30)

### Decision
Split monolithic Blazor server into three-project layout:
- **AgenticResolution.Api** — EF Core, SQL, webhooks, CRUD endpoints
- **AgenticResolution.Web** — Blazor UI only, calls Api over HTTP
- **AgenticResolution.McpServer** — MCP wrapper around Api, exposes tools to Foundry agents

### Rationale
- Independent scaling (UI vs. data layer)
- Clean security boundary (only Api touches SQL)
- MCP server access requires separate process
- Jason's explicit requirement

### Implementation
| Component | Location | Port (dev) | Azure |
|-----------|----------|-----------|-------|
| Api | `src/AgenticResolution.Api/` | 5001 | App Service (new) |
| Web | `src/AgenticResolution.Web/` | 5000 | App Service (existing) |
| McpServer | `src/TicketsApi.McpServer/` | 5002 | Container App (Phase 2) |

### Payload: ServiceNow-Style Webhook Snapshot
Webhook now sends `TicketWebhookSnapshot` (partial):
- **Included:** Number, ShortDescription, Category, Priority, Urgency, Impact, State, Caller, AssignmentGroup, OpenedAt
- **Excluded:** Id, Description, UpdatedAt (fetch on-demand via API if needed)

### Test Projects
- `AgenticResolution.Api.Tests` — targets Api endpoints via `WebApplicationFactory`
- `AgenticResolution.Web.ComponentTests` — targets Blazor components (unchanged)

### Build Status
✅ `dotnet build AgenticResolution.sln` → 0 errors, 0 warnings

---

**Archive maintained at:** `.squad/decisions/decisions-archive.md`  
**Restored decisions:** None (all archived entries remain archived)
