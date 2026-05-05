# Hicks — Decision: Ticket API Contract Extensions for Blazor UI

**By:** Hicks (Backend Dev)  
**Date:** 2026-05-07  
**Status:** Implemented  
**Context:** Phase 2.5 Blazor frontend needs enhanced filtering, comments, manual resolution flow, and workflow run visibility.

---

## Decision

Extended the ticket API with new endpoints and capabilities while preserving backward compatibility:

### 1. Enhanced List Filtering

**Endpoint:** `GET /api/tickets`

**Query Parameters:**
- `assignedTo` (string): Exact match; sentinel value `"unassigned"` matches NULL
- `state` (string): Comma-separated TicketState values (e.g., `"New,InProgress"`)
- `category` (string): Exact match
- `priority` (string): Comma-separated TicketPriority values (e.g., `"Critical,High"`)
- `q` (string): Substring match on ShortDescription or Description (case-insensitive)
- `sort` (enum): `"created"` | `"modified"` (default: `"created"`)
- `dir` (enum): `"asc"` | `"desc"` (default: `"desc"`)
- `page` (int): Page number ≥1
- `pageSize` (int): 1–100 items per page

**Sort field whitelist:** Enforced via `switch` expression to prevent arbitrary SQL column injection.

### 2. Ticket Detail Endpoint

**Endpoint:** `GET /api/tickets/{number}/details`

**Response:** `TicketDetailResponse { Ticket, Comments[], Runs[] }`

Single round-trip for detail page; cheaper than 3 separate calls.

### 3. Comments

**Endpoints:**
- `GET /api/tickets/{number}/comments` → `CommentResponse[]` ordered by CreatedAt asc
- `POST /api/tickets/{number}/comments` body: `{ author, body, isInternal }` → `CommentResponse`

**Validation:**
- `author`: 1–100 chars
- `body`: 1–4000 chars
- No update/delete in Phase 2.5 — append-only

**Auth caveat:** Author is free-text (no identity verification). Flagged for follow-up before external demo.

### 4. Manual Resolve Trigger

**Endpoint:** `POST /api/tickets/{number}/resolve`

**Request body (optional):** `{ note?: string }`

**Response:** HTTP 202 Accepted with `{ runId, statusUrl, eventsUrl }`

**Idempotency:** If a Pending or Running run already exists for the ticket, returns HTTP 200 with existing run instead of creating a duplicate.

**Behavior:** Creates a `WorkflowRun` record with status Pending. Does NOT immediately invoke the agent pipeline — that's the responsibility of the `IResolutionRunner` BackgroundService (not yet implemented; Bishop's integration point).

### 5. Workflow Run Visibility

**Endpoints:**
- `GET /api/tickets/{number}/runs` → `WorkflowRunResponse[]` ordered by StartedAt desc
- `GET /api/runs/{runId}` → `WorkflowRunDetailResponse { Run, Events[] }`
- `GET /api/runs/{runId}/events` → `WorkflowRunEventResponse[]` ordered by Sequence asc

**Event streaming note:** Current implementation returns all events from DB. Live streaming via SSE or SignalR not yet implemented — Ferro's run detail page will need to poll or wait for SignalR hub wiring.

### 6. Webhook Auto-Dispatch Flag

**Config key:** `Webhook:AutoDispatchOnTicketWrite` (boolean, default `false`)

**Behavior:**
- When `false` (default): `CreateAsync` and `UpdateAsync` skip `dispatcher.Enqueue(...)` entirely
- When `true`: Webhook dispatch fires as in Phase 1

**Rationale:** Phase 2.5 decouples ticket write from agent invocation. Resolution is now manual via `POST /resolve`. Webhook plumbing preserved for future integration (e.g., external ServiceNow sync).

---

## Data Model Changes

Three new entities added via EF migration `20260507000000_AddCommentsAndWorkflowRuns`:

### TicketComment
- Id (Guid, PK, NEWSEQUENTIALID())
- TicketId (Guid, FK → Ticket.Id, cascade delete)
- Author (nvarchar(100), NOT NULL)
- Body (nvarchar(max), NOT NULL)
- IsInternal (bit, NOT NULL, default 0)
- CreatedAt (datetime2, SYSUTCDATETIME())
- Index: (TicketId)

### WorkflowRun
- Id (Guid, PK, NEWSEQUENTIALID())
- TicketId (Guid, FK → Ticket.Id, cascade delete)
- Status (int: Pending=0, Running=1, Completed=2, Failed=3, Escalated=4)
- TriggeredBy (nvarchar(100), NULL)
- Note (nvarchar(500), NULL)
- StartedAt (datetime2, SYSUTCDATETIME())
- CompletedAt (datetime2, NULL)
- FinalAction (nvarchar(100), NULL)
- FinalConfidence (float, NULL)
- Composite index: (TicketId, Status)

### WorkflowRunEvent
- Id (Guid, PK, NEWSEQUENTIALID())
- RunId (Guid, FK → WorkflowRun.Id, cascade delete)
- Sequence (int, NOT NULL)
- ExecutorId (nvarchar(100), NULL)
- EventType (nvarchar(50), NOT NULL)
- Payload (nvarchar(max), NULL — JSON)
- Timestamp (datetime2, SYSUTCDATETIME())
- Composite index: (RunId, Sequence)

### Ticket Table Indexes Added
- UpdatedAt (for sort=modified filter)
- AssignedTo (for assignedTo filter)
- Category (for category filter)

---

## Backward Compatibility

- Existing `GET /api/tickets/{number}` unchanged — still returns `TicketResponse` without comments/runs
- Existing `POST /api/tickets` and `PUT /api/tickets/{id}` still accept same request shapes
- Webhook behavior changed only if config flag is true; default is no-op (safe for existing tests)
- MCP server endpoints unchanged — they don't use the new filtering or comments

---

## Known Gaps / Future Work

1. **Resolution runner not implemented**: `POST /resolve` creates a Pending run but does not actually invoke the agent pipeline. Needs `IResolutionRunner` BackgroundService from Bishop.
2. **No SignalR hub**: Live event streaming for workflow runs not implemented. Ferro will need to poll `/api/runs/{runId}/events` until SignalR hub `/hubs/runs` is added.
3. **No auth on comments or resolve**: Author is free-text; resolve is open. Needs identity integration before external demo.
4. **Infra deployment blocker**: All infra Bicep files are 0-byte stubs. Cannot deploy backend Container App without baseline infra from Apone.

---

## Rationale

**Why detail endpoint instead of expanding the base ticket response?**  
Backward compatibility. Existing MCP server and Phase 1 consumers expect the lean `TicketResponse`. A separate `/details` endpoint lets Ferro get everything in one call without breaking existing clients.

**Why idempotent resolve?**  
Prevents duplicate active runs when user spams the button or refreshes the page. Frontend can safely retry on network error.

**Why not immediately run the agent pipeline in the resolve endpoint?**  
(1) Keeps the HTTP request/response path fast (202 Accepted, not blocking on pipeline completion); (2) Separates concerns — endpoint owns persistence, runner owns orchestration; (3) Allows queuing/throttling if we hit scale limits later.

**Why disable webhook auto-dispatch by default?**  
Phase 2.5 requirement. Manual resolution is the UX path. Auto-dispatch was a Phase 1 demo convenience that conflates ticket write with agent invocation. Preserving the plumbing lets us re-enable it later for integration scenarios (e.g., ServiceNow webhook forwarding).

---

## Tradeoffs

**Pros:**
- Clean separation between ticket CRUD and resolution flow
- Idempotent resolve reduces error surface
- Enhanced list filters eliminate need for client-side filtering on large datasets
- Backward-compatible — no breaking changes to existing endpoints

**Cons:**
- Resolve endpoint creates a Pending run but does nothing with it — needs follow-up from Bishop
- No live event streaming yet — Ferro must poll for now
- Auth on comments is deferred — author is free-text
- Three new tables increase schema complexity

---

## Validation

- Build succeeded: 0 errors, 2 warnings (Azure.AI.Agents.Persistent version resolution — non-blocking)
- EF migration valid: created manually following existing migration patterns
- Model snapshot updated to reflect all new entities and indexes
- Webhook auto-dispatch flag tested via `IConfiguration.GetValue` with default false
