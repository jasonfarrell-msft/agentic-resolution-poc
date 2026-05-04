# Decision: Solution Split Implemented

**Author:** Hicks (Backend Dev)
**Date:** 2026-04-30
**Status:** Done
**Requested by:** Jason Farrell / Apone architecture decision

---

## What Was Implemented

### AgenticResolution.Api (new project)
- **Location:** `src/AgenticResolution.Api/`
- **Type:** `Microsoft.NET.Sdk.Web`, net10.0 Web API
- All API, EF Core, Migrations, Webhooks moved from `AgenticResolution.Web`
- New CORS policy `BlazorFrontend` (configurable via `Cors:AllowedOrigins`)
- Migration on startup (skips if `(placeholder)` connection string)

### New Ticket Fields
Added to `Models/Ticket.cs` and migration `20260430000000_AddAgentFields`:
| Column | Type | Notes |
|--------|------|-------|
| `ResolutionNotes` | nvarchar(max), nullable | Free-text resolution |
| `AgentAction` | nvarchar(100), nullable | e.g. `password_reset_guided` |
| `AgentConfidence` | float, nullable | 0.0–1.0 |
| `MatchedTicketNumber` | nvarchar(20), nullable | e.g. `INC0009234` |

### New Endpoints
- `PUT /api/tickets/{id:guid}` — agent writeback; updates State, ResolutionNotes, AssignedTo, AgentAction, AgentConfidence, MatchedTicketNumber; fires `ticket.updated` webhook
- `GET /api/tickets/search?q=` — keyword search on ShortDescription + Description; pageSize capped at 50

### Webhook Payload Change
`TicketSnapshot` → `TicketWebhookSnapshot` (ServiceNow-style partial):
- Removed: `Id`, `Description`, `UpdatedAt`
- Added: `Urgency`, `Impact`, `AssignmentGroup` (null for now)
- Kept: `Number`, `ShortDescription`, `Category`, `Priority`, `State`, `Caller`, `OpenedAt`
- Added `ForTicketUpdated()` factory alongside `ForTicketCreated()`

### AgenticResolution.Web (cleaned up)
- Removed EF Core / webhook packages from csproj
- `Program.cs` trimmed to Blazor + HttpClient only
- `Models/TicketEnums.cs` added (preserves `TicketPriority`, `TicketState` for Blazor pages)
- `Models/Dtos/TicketResponse.cs` updated with new agent fields
- `appsettings.json` updated with `ApiBaseUrl: "http://localhost:5001/"`

### Solution File
All 4 projects now in `AgenticResolution.sln`:
- `AgenticResolution.Api` (new, GUID: C2D3E4F5-...)
- `AgenticResolution.Web` (existing)
- `AgenticResolution.Web.Tests` (retargeted to Api)
- `AgenticResolution.Web.ComponentTests` (stays on Web)

### Test Projects
- `AgenticResolution.Web.Tests` now references `AgenticResolution.Api` — `WebApplicationFactory<Program>` targets Api's `Program`
- `AgenticResolution.Web.ComponentTests` unchanged (Blazor component tests stay on Web)

## Build Result
`dotnet build AgenticResolution.sln` — **0 errors, 0 warnings**

## Deferred / Out of Scope
- Bicep updates for second App Service deployment (Step 8 per Apone's sequencing)
- `AgenticResolution.McpServer` project (not Hicks's task)
- `dotnet test` validation (Vasquez owns test suite)
