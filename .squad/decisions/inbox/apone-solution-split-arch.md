# Architecture Decision: Solution Split + ServiceNow Webhook + MCP Server

**Author:** Apone (Lead/Architect)  
**Date:** 2025-07-25  
**Status:** Approved  
**Requested by:** Jason Farrell  
**Scope:** Phase 2+ — restructures solution from single-project to three-project layout

---

## Decision 1: Solution Layout — Three-Project Split

### New Layout

```
/src
  AgenticResolution.Api/              # ASP.NET Core Web API (tickets CRUD, EF Core, SQL, webhook dispatch)
    Api/
      TicketsEndpoints.cs             ← moved from Web
    Data/
      AppDbContext.cs                 ← moved from Web
      TicketNumberGenerator.cs        ← moved from Web
    Migrations/                       ← moved from Web
    Models/
      Ticket.cs                       ← moved from Web
      Dtos/                           ← shared DTO records (TicketResponse, CreateTicketRequest, PagedResponse, etc.)
    Webhooks/
      WebhookPayload.cs              ← moved from Web (trimmed — see Decision 2)
      WebhookDispatcher.cs           ← moved from Web
      WebhookDispatchService.cs      ← moved from Web
    Program.cs                        # new — Web API host, EF, webhook services
    appsettings.json

  AgenticResolution.Web/              # Blazor Server only — UI, calls Api over HTTP
    Components/                       ← stays
    Layout/                           ← stays
    Pages/                            ← stays
    Services/
      TicketsApiClient.cs             ← stays (base URL now points to separate Api)
    Models/
      Dtos/                           ← REFERENCES Api project's DTO types OR duplicates (see below)
    wwwroot/                          ← stays
    Program.cs                        # trimmed — no more EF/webhook, only Blazor + HttpClient
    appsettings.json

  AgenticResolution.McpServer/        # MCP server — wraps Api over HTTP
    Tools/
      TicketTools.cs                  # MCP tool definitions (get_ticket, list_tickets, search_tickets)
    Program.cs                        # stdio-based MCP host
    appsettings.json
```

### Key Decisions

#### Q1: What goes in each project?

| Concern | Target Project |
|---------|---------------|
| EF Core (DbContext, Migrations, Models) | **Api** |
| Minimal API endpoints (`/api/tickets`) | **Api** |
| Webhook dispatch (Channel, BackgroundService, HMAC) | **Api** |
| Ticket number generation | **Api** |
| Blazor components, pages, layout, wwwroot | **Web** |
| `TicketsApiClient` (typed HttpClient) | **Web** |
| MCP tool definitions + MCP host | **McpServer** |

#### Q2: Separate processes / separate Azure deployments?

**Yes — separate processes, separate deployments.** Jason's intent is unambiguous.

- `AgenticResolution.Api` → **Azure App Service** (new, separate plan or same plan, separate app). Name: `app-agres-api-{env}-{suffix}`
- `AgenticResolution.Web` → **Existing App Service** (`app-agentic-res-agentic-resolution-dev-ie6eryvrpccqa`). Continues as the user-facing frontend.
- `AgenticResolution.McpServer` → **Deployed later** (Phase 2+). Local stdio for now; future deployment is Container App or sidecar.

**Justification:** Separation enables independent scaling, independent deployment, and clean security boundaries (only the Api touches SQL). The Web project becomes a pure UI shell.

#### Q3: What does `TicketsApiClient.cs` point to?

After split, `TicketsApiClient` uses a new config key:

```json
// AgenticResolution.Web/appsettings.json
{
  "ApiBaseUrl": "https://app-agres-api-dev-{suffix}.azurewebsites.net/"
}
```

In local dev: `"ApiBaseUrl": "https://localhost:5001/"` (Api project's Kestrel port).

No code changes needed in `TicketsApiClient` itself — it already uses `_http.BaseAddress` from config.

#### Q4: Test project disposition?

**Keep tests as-is initially.** They test the API endpoints via `WebApplicationFactory<Program>` — after the split, they target `AgenticResolution.Api` instead of `AgenticResolution.Web`. Rename to `AgenticResolution.Api.Tests` during the move. Component tests (`AgenticResolution.Web.ComponentTests`) stay targeting Web.

| Test Project | Target |
|--------------|--------|
| `AgenticResolution.Api.Tests` (renamed) | Api project endpoints/services |
| `AgenticResolution.Web.ComponentTests` | Blazor component rendering |

#### Q5: Azure hosting for Api?

**New App Service** in the same App Service Plan (B1 supports multiple apps). Dedicated app so it gets its own URL, managed identity, and deployment slot. Bicep module added for the second app.

---

## Decision 2: ServiceNow Webhook Partial Payload

### Rationale

ServiceNow outbound REST Message for `incident.created` sends a compact notification — just enough to identify and triage. Heavy fields (`description`, `work_notes`, resolution data) are fetched on-demand by the receiver if needed.

### New `WebhookPayload` Shape

The `TicketSnapshot` record is replaced with `TicketWebhookSnapshot` — a partial projection:

```csharp
/// <summary>
/// Partial ticket snapshot matching ServiceNow incident.created webhook semantics.
/// Heavy/optional fields excluded — receiver fetches full record via GET /api/tickets/{number}.
/// </summary>
public record TicketWebhookSnapshot(
    string Number,           // INC0010001 — ServiceNow "number"
    string ShortDescription, // ServiceNow "short_description"
    string Category,         // ServiceNow "category"
    string Priority,         // "1" through "4" — ServiceNow "priority"
    string Urgency,          // Derived from Priority for now — ServiceNow "urgency"
    string Impact,           // Derived from Priority for now — ServiceNow "impact"
    string State,            // "New" — ServiceNow "state"
    string Caller,           // ServiceNow "caller_id"
    string? AssignmentGroup, // ServiceNow "assignment_group" (nullable — may not be set)
    DateTime OpenedAt        // ServiceNow "opened_at" — maps to our CreatedAt
);
```

### Field Mapping

| Our Field | ServiceNow Equivalent | In Webhook? | Notes |
|-----------|----------------------|-------------|-------|
| `Id` (Guid) | `sys_id` | **NO** | Internal; receiver uses `Number` to fetch |
| `Number` | `number` | **YES** | Primary identifier for receiver |
| `ShortDescription` | `short_description` | **YES** | |
| `Description` | `description` | **NO** | Large; fetch on-demand via API |
| `Category` | `category` | **YES** | |
| `Priority` | `priority` | **YES** | Numeric string "1"–"4" |
| — | `urgency` | **YES** | Derived = Priority (simplified) |
| — | `impact` | **YES** | Derived = Priority (simplified) |
| `State` | `state` | **YES** | |
| `AssignedTo` | `assigned_to` | **NO** | Not set at creation |
| — | `assignment_group` | **YES** | Nullable; future Phase 2 field |
| `Caller` | `caller_id` | **YES** | |
| `CreatedAt` | `opened_at` / `sys_created_on` | **YES** | As `opened_at` |
| `UpdatedAt` | — | **NO** | Meaningless at creation |

### JSON Output (snake_case)

```json
{
  "event_id": "a1b2c3d4-...",
  "event_type": "ticket.created",
  "timestamp": "2025-07-25T14:30:00Z",
  "ticket": {
    "number": "INC0010001",
    "short_description": "Cannot access VPN",
    "category": "Network",
    "priority": "2",
    "urgency": "2",
    "impact": "2",
    "state": "New",
    "caller": "jason.farrell",
    "assignment_group": null,
    "opened_at": "2025-07-25T14:30:00Z"
  }
}
```

### What the Receiver Does

The Container App webhook receiver (`ca-agres-ie6eryvrpccqa`) receives this compact payload. If it needs full ticket data (e.g., for AI Search indexing), it calls back to `GET /api/tickets/{number}` on the Api. This is the ServiceNow pattern: webhook = notification, API = data retrieval.

---

## Decision 3: MCP Server Design

### Tool Definitions

The MCP server exposes three tools to Foundry agents:

| Tool | Description | Api Endpoint |
|------|-------------|--------------|
| `get_ticket` | Get full ticket details by number | `GET /api/tickets/{number}` |
| `list_tickets` | List tickets with optional state filter, pagination | `GET /api/tickets?state={s}&page={p}&pageSize={ps}` |
| `search_tickets` | Search tickets by keyword (short description match) | `GET /api/tickets/search?q={query}` (new endpoint) |

### Transport

**stdio transport** — standard for MCP servers consumed by AI agents/IDEs. The MCP server runs as a standalone process invoked by the Foundry agent runtime or local dev tools.

**Not** SSE/HTTP — Foundry agents and VS Code/Copilot MCP integrations use stdio. SSE would be Phase 3 if we need a shared hosted endpoint.

### Data Access

**HTTP-over-Api** — the MCP server calls the Api via `HttpClient`. It does NOT share the DbContext.

**Justification:**
1. Clean separation of concerns — MCP server is a thin adapter, not a data layer.
2. Api already has auth, validation, and business logic — no duplication.
3. MCP server can run anywhere (dev machine, container, agent sidecar) without SQL connectivity.
4. Security: MCP server only needs network access to the Api, not to SQL.

### Deployment

**Phase 2:** Local process only (stdio, launched by Foundry agent config or VS Code MCP settings).  
**Phase 3 (future):** Container App sidecar or standalone Container App with SSE transport for shared multi-agent access.

### Technology

- Use `ModelContextProtocol` NuGet package (official C# MCP SDK from Microsoft).
- .NET 10 console app.
- `IHostBuilder` with MCP server hosting.
- Typed `HttpClient` pointing to Api base URL.

---

## Decision 4: Build Sequencing

### Execution Order

| Step | What | Owner | Blocked By |
|------|------|-------|-----------|
| 1 | Create `AgenticResolution.Api` project — move EF Core, Models, Migrations, Endpoints, Webhooks | Hicks | Nothing |
| 2 | Update `AgenticResolution.Web` — remove moved code, configure `ApiBaseUrl` to point to Api | Hicks | Step 1 |
| 3 | Rename + retarget test project to `AgenticResolution.Api.Tests` | Hicks | Step 1 |
| 4 | Trim webhook payload to `TicketWebhookSnapshot` (partial, ServiceNow-style) | Hicks | Step 1 |
| 5 | Add `GET /api/tickets/search?q=` endpoint (needed by MCP) | Hicks | Step 1 |
| 6 | Add `PUT /api/tickets/{id}` endpoint (agent writeback) | Hicks | Step 1 |
| 7 | Create `AgenticResolution.McpServer` project | C# MCP Expert / Bishop | Steps 1, 5 |
| 8 | Update Bicep + `azure.yaml` for dual App Service deployment | Hicks | Steps 1, 2 |
| 9 | Integration test: Web → Api → SQL round-trip in CI | Vasquez | Steps 1, 2 |

### Parallelism

- Steps 1–4 are sequential (core split work).
- Steps 5 and 6 can run in parallel after Step 1.
- Step 7 (MCP server) can begin design/scaffolding after Step 1 lands — it only needs the Api running.
- Step 8 can run in parallel with Steps 5–7.

### Definition of Done

The split is complete when:
1. `dotnet build AgenticResolution.sln` succeeds with all three projects.
2. `dotnet test` passes for both test projects.
3. Web project starts, renders ticket list by calling Api over HTTP.
4. Api project starts, serves `/api/tickets`, dispatches trimmed webhook payload.
5. MCP server starts (stdio), successfully calls `get_ticket` against running Api.

---

## Impact on Existing Infrastructure

| Resource | Change |
|----------|--------|
| App Service (existing) | Continues hosting Web |
| App Service (new) | Hosts Api — same plan, new app |
| Azure SQL | No change — Api connects via managed identity |
| Container App (webhook) | No change — receives trimmed payload |
| Key Vault | Api gets `Key Vault Secrets User` role |
| `azure.yaml` | Add `api` service entry |
| Bicep | Add `appservice-api.bicep` module |

---

## Superseded Decision

This decision **supersedes** the Phase 1 decision "Single-project Blazor Server with embedded minimal API endpoints" (2025-07-25). The justification for extraction: Jason explicitly requires separation for MCP server access, independent scaling, and clear security boundaries between UI and data layers.
