# Decision: TicketsApi.McpServer Scaffold

**Date:** 2026-04-30  
**Author:** Hicks (Backend Dev)  
**Requested by:** Jason Farrell

## Summary

Created `src/TicketsApi.McpServer/` — a standalone ASP.NET Core web app that wraps `AgenticResolution.Api` and exposes it as an MCP server for Azure AI Foundry agents.

## Key Decisions

### Transport: SSE (not stdio)
- Foundry agents connect over HTTP; they cannot spawn local processes.
- `WithHttpTransport()` + `MapMcp("/mcp")` from `ModelContextProtocol.AspNetCore` v1.2.0.
- MCP endpoint: GET `/mcp` (SSE stream), POST `/mcp` (client messages).

### No shared DbContext
- McpServer calls `AgenticResolution.Api` over HTTP via `TicketApiClient : ITicketApiClient`.
- BaseUrl configured via `TicketsApi:BaseUrl` (appsettings / user-secrets / env var).
- Optional `X-Api-Key` header support for future API key auth.

### 4 MCP Tools
| Tool | Purpose |
|------|---------|
| `get_ticket_by_number` | Fetch single ticket by INC number |
| `list_tickets` | Paged list with optional state filter |
| `search_tickets` | Keyword search against description fields |
| `update_ticket` | Agent writeback: state, resolution notes, confidence, action |

### Solution GUID
- Assigned `{F5A6B7C8-D9E0-4123-FA45-567890123456}` (task spec proposed `{D3E4F5A6-...}` which collided with Web.Tests).

### Hosting
- Standalone ASP.NET Core web app on its own port (local dev).
- Future: standalone Container App.
- Health probe at `/health` for liveness/readiness checks.

### Telemetry
- `Microsoft.ApplicationInsights.AspNetCore` v2.22.0 wired via `AddApplicationInsightsTelemetry()`.
- Connection string from `ApplicationInsights:ConnectionString` (empty = disabled locally).

## Build Result
`dotnet build AgenticResolution.sln` → **0 errors, 0 warnings**.
