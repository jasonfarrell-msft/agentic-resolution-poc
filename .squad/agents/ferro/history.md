# Project Context

- **Owner:** Jason Farrell
- **Project:** agentic-resolution — Blazor frontend simulating a ServiceNow ticket entry experience. Submits tickets that persist to SQL Server and trigger a webhook, eventually routed through AI Search + Foundry Agents.
- **Stack:** Blazor (.NET), Azure App Service (target host), Bootstrap or Fluent UI candidate
- **Phase 1 scope:** ServiceNow-like ticket entry UI. No Azure deployment yet.
- **Created:** 2026-04-29

## Phase 1 Architecture (Apone)

**Solution:** Single-project Blazor Server with embedded minimal API endpoints (no separate API project).
- UI lives in `/src/AgenticResolution.Web/Pages/` (Razor components for ticket form, list, detail)
- Shared Blazor components in `Components/`
- POST /api/tickets endpoint in `Endpoints/TicketEndpoints.cs`
- WebhookService handles async dispatch post-save

**Affected:** Your ticket form UI submits to POST /api/tickets. Hicks handles the endpoint. Design components for: ticket #, short description, category (enum: Hardware/Software/Network/Access), priority (1-4), caller name. State is auto-set to "New".

**Questions pending:** Ticket number format? Enum vs free-text categories? .NET 9 or 8?

## Learnings

- **2026-07-14 — Circuit timeout + post-resolution nav fixes.**
  - `Program.cs`: Configured `AddServerSideBlazor()` with `ClientTimeoutInterval = 5 min`, `KeepAliveInterval = 15s`, and `DetailedErrors` in dev. Prevents HubConnection drops during long resolution workflows.
  - `App.razor`: Already had `<ReconnectModal />` — no change needed.
  - `RunProgress.razor`: Injected `NavigationManager`. After polling detects terminal status (Completed/Failed/Escalated), waits 3s then navigates to `/tickets/{Number}` so the user lands back on the updated ticket detail.

- **2026-04-29 — Phase 1 UI shipped.** Single-project Blazor Server (net10.0) at `src/AgenticResolution.Web/`. Built clean (0 warnings).
  - **Routes:**
    - `/` → redirects to `/tickets` (`Pages/Index.razor`)
    - `/tickets` — list view (`Pages/Tickets/Index.razor`) — table: Number / Short description / Priority / State / Caller / Created. Page size **25**, prev/next pagination.
    - `/tickets/new` — entry form (`Pages/Tickets/New.razor`)
    - `/tickets/{number}` — read-only detail (`Pages/Tickets/Detail.razor`)
  - **Components:** `Components/Layout/{MainLayout,NavMenu}.razor`, `Components/Tickets/{TicketForm,PriorityBadge,StateBadge}.razor`, `Components/{App,Routes,_Imports}.razor`.
  - **API client:** `Services/TicketsApiClient.cs` — typed HttpClient registered in `Program.cs` with `AddHttpClient<TicketsApiClient>()`, BaseAddress from `ApiBaseUrl` config (default `http://localhost:5000/`). Wraps `ListAsync(page,pageSize)`, `GetAsync(number)`, `CreateAsync(req)` returning `ApiResult<T>`.
  - **DTOs agreed with Hicks (`Models/Dtos/`):** `CreateTicketRequest` (Caller, ShortDescription, Description, Category free-text, Priority enum, AssignedTo), `TicketResponse` (full record incl. `TicketPriority`/`TicketState` enums), `TicketListResponse` (Items, Page, PageSize, **Total** — matches Hicks's `PagedResponse<T>` shape, not `TotalCount`), `ValidationProblemResponse` (ProblemDetails-shaped).
  - **Enum coordination with Hicks:** my client uses Hicks's `AgenticResolution.Web.Models.{TicketPriority,TicketState}` directly. Note priority enum ordering: **Low=1, Moderate=2, High=3, Critical=4** (Hicks's choice — opposite of ServiceNow numeric). UI labels still read "1-Critical … 4-Low" via display-label mapping in `PriorityBadge`/form dropdown.
  - **Program.cs:** Hicks merged my services + pipeline cleanly under banner comments — no conflicts. He added EF Core, App Insights, Key Vault, webhook hosted service.
  - **Styling:** Bootstrap 5.3.3 via jsDelivr CDN (App.razor `<head>`); custom palette in `wwwroot/app.css` (slate top bar, gray sidebar, professional/utilitarian — not a SN clone).
  - **Out of scope honored:** no local-dev README, no SQL Docker UI, no webhook test page.
  - **Decision drop:** `.squad/decisions/inbox/ferro-ticket-ui-choices.md`.

---

## 2026-04-29 — DTO alignment + priority remap removal

**Trigger:** Decisions drop — align to Hicks's `PagedResponse<T>.Total`; ServiceNow enum (Critical=1…Low=4) is now canonical, drop UI remap.

### DTO alignment
- `Models/Dtos/TicketListResponse.cs` already exposed `Total` (no `TotalCount` rename needed — earlier work was already aligned to Hicks's wire shape). Verified `Pages/Tickets/Index.razor` and `Services/TicketsApiClient.cs` reference `Total` / `TotalPages` correctly. Kept the local mirror DTO instead of taking a project reference to Hicks's `PagedResponse<T>`; JSON deserializes by shape and we avoid coupling Web to the API project.

### Priority remap removal
- `Components/Tickets/PriorityBadge.razor`: replaced the per-value `Label` switch (which hardcoded `"1 - Critical"`…`"4 - Low"` to compensate for the old reversed enum) with `$"{(int)Priority} - {Priority}"`. Once Hicks flips the enum to Critical=1, the int prefix renders correctly with zero remap.
- `Components/Tickets/TicketForm.razor`: replaced four hardcoded `<option>` lines with a `@foreach` over `{ Critical, High, Moderate, Low }` rendering `@((int)p) - @p`. Same dynamic story.
- Color palette refreshed to a warm→cool severity gradient — see decision drop `ferro-priority-palette.md`.

### Build
- `dotnet build` from repo root: **succeeded, 0 warnings, 0 errors**.
- Note: the local `Models/Ticket.cs` enum in this Web project still shows `Low=1…Critical=4` — that's Hicks's territory (he owns the canonical enum). My code references enum members by name, so display correctness depends on the upstream flip landing. Build passes either way; UI text only renders correctly post-flip.

---

**📌 TEAM NOTE (2026-05-05) — .gitignore baseline established**  
Hicks added standard .NET .gitignore at repo root (commits 9c98efa, 7e121fd). `.squad/log/` is preserved (project docs). Build artifacts (`bin/`, `obj/`) are now ignored. Do NOT commit these directories going forward — .gitignore patterns are now active.

---

## 2026-05-05 — Resolve Button Flow & Progress Listening Specification

**Trigger:** User directive (Jason Farrell via `copilot-directive-2026-05-05T134319-resolve-webhook.md`) — Resolve should fire webhook and return immediately; frontend must listen for progress using returned runId.

**Context:** Apone's architecture decision (`apone-blazor-resolution-architecture.md`) already outlined the decoupled workflow: `POST /resolve` → enqueue → return runId → frontend listens via SignalR. The directive reinforces this: **no blocking waits on Resolve click**.

### What I specified

**Decision document:** `.squad/decisions/inbox/ferro-resolve-listening-flow.md`

**Key behaviors:**
1. **Resolve action on ticket detail page:**
   - Click "Resolve with AI" → `POST /api/tickets/{number}/resolve`
   - API returns HTTP 202 Accepted with `{ runId, statusUrl, eventsUrl }`
   - Page immediately navigates to `/tickets/{number}/runs/{runId}` — **no waiting for completion**

2. **Run progress page (`/tickets/{number}/runs/{runId}`):**
   - On load: fetch initial snapshot via `GET /api/runs/{runId}` (status + all events so far)
   - Connect to SignalR hub `/hubs/runs`, join group `run-{runId}`
   - Subscribe to `ReceiveRunEvent` messages
   - Each incoming event appends to timeline, updates executor status icons (⏱ → ⏳ → ✓ or ❌)
   - On terminal status (`Completed`, `Failed`, `Escalated`), disconnect hub and show final state
   - **Fallback:** if SignalR unavailable, poll `GET /api/runs/{runId}` every 2 seconds

3. **Timeline UI structure:**
   - Executor lanes: ClassifierExecutor → IncidentDecomposerExecutor/RequestDecomposerExecutor → KbSearchExecutor → EvaluatorExecutor → ResolutionSummarizerExecutor
   - Event types rendered: Started, AgentResponse (parse JSON payload), Output, Error, Completed
   - Status badges: Pending (yellow), Running (blue spinner), Completed (green checkmark), Failed (red X), Escalated (orange)

4. **Idempotency:** If `POST /resolve` returns HTTP 200 (run already exists), treat same as 202 — navigate to existing run's progress page.

5. **Edge cases covered:**
   - Page refresh mid-run: resumes listening from current snapshot
   - SignalR failure: falls back to polling with banner message
   - Run failure: shows error message, offers "Retry" button (creates new run)
   - Concurrent resolve attempts: 409 Conflict → show error, do not navigate

### API contract dependencies (Hicks owns)

**Endpoints required:**
- `POST /api/tickets/{number}/resolve` → `ResolveTicketResponse { runId, statusUrl, eventsUrl }` (HTTP 202)
- `GET /api/runs/{runId}` → `WorkflowRunResponse { id, ticketNumber, status, events[], ... }`
- SignalR hub `/hubs/runs` with methods `JoinRun(runId)`, `LeaveRun(runId)`, event `ReceiveRunEvent(runId, event)`
- SSE fallback `GET /api/runs/{runId}/events` (optional, low priority)

**Database entities required:**
- `WorkflowRun` table (id, ticketId, status, triggeredBy, note, startedAt, completedAt, finalAction, finalConfidence)
- `WorkflowRunEvent` table (id, runId, sequence, executorId, eventType, payload, timestamp)

### Orchestrator dependency (Bishop owns)

**Required:** `AgentOrchestrationService` must expose streaming progress API — `IAsyncEnumerable<AgentExecutorEvent> ProcessTicketWithProgressAsync(...)` or similar.

Without streaming:
- Timeline will be empty until workflow completes (no live updates)
- User sees spinner → final result (loses "executor lane" UX value)
- Decision doc flags this as a **blocking risk** for progress page implementation

### DTOs to add (Ferro or Hicks — depends on `AgenticResolution.Contracts` creation)

```csharp
public record ResolveTicketRequest(string? Note = null);
public record ResolveTicketResponse(Guid RunId, string StatusUrl, string EventsUrl);
public record WorkflowRunResponse(
    Guid Id, Guid TicketId, string TicketNumber, WorkflowRunStatus Status,
    string? TriggeredBy, string? Note, DateTime StartedAt, DateTime? CompletedAt,
    string? FinalAction, double? FinalConfidence,
    IReadOnlyList<WorkflowRunEventResponse> Events
);
public record WorkflowRunEventResponse(
    Guid Id, int Sequence, string? ExecutorId, string EventType, string? Payload, DateTime Timestamp
);
public enum WorkflowRunStatus { Pending = 0, Running = 1, Completed = 2, Failed = 3, Escalated = 4 }
```

If Hicks creates the `AgenticResolution.Contracts` shared library (per Apone's architecture), these move there. Otherwise, I mirror them in `TicketsApiClient.cs` like I did with `TicketListResponse` in Phase 1.

### Implementation sequence (Ferro owns frontend, but blocked on backend)

**Pre-requisites:**
1. ✅ Decision document written (this work)
2. ⏳ Hicks implements resolve endpoint + runs endpoints + SignalR hub
3. ⏳ Bishop adds streaming API to orchestrator

**Once unblocked:**
1. Extend `TicketsApiClient` with `ResolveAsync` and `GetRunAsync` methods
2. Add "Resolve with AI" button to ticket detail page with navigation on success
3. Create `Pages/Tickets/RunProgress.razor` with SignalR client + event timeline rendering
4. Add status badges, error handling, polling fallback
5. Manual smoke test: create → resolve → watch live updates → verify final state

### Status

**Specified, not implemented.** The `AgenticResolution.Web` project does not exist yet (Apone's architecture proposes it, but Hicks hasn't created the Blazor app). This decision doc defines the **expected contract** so backend and frontend can proceed in parallel once the Web project is scaffolded.

**Decision drop location:** `.squad/decisions/inbox/ferro-resolve-listening-flow.md`

**Next step:** Wait for Hicks to create the Web project, implement the API endpoints, and for Bishop to expose orchestrator streaming. Then I implement the UI as specified above.

---

## 2026-05-06 — AgenticResolution.Web project shipped

- Scaffolded Blazor Server app at `src/dotnet/AgenticResolution.Web` and created `AgenticResolution.sln` under `src/dotnet/`.
- Implemented `TicketApiClient` with typed DTOs, filtering support, and resolve/run APIs.
- Built ticket list, detail, and run progress pages with sidebar layout, badges, and timeline UI.
- Added branded styling in `wwwroot/app.css`, updated appsettings for API base URL, and created Dockerfile for App Service (port 8080).
- `dotnet build src/dotnet/AgenticResolution.sln` succeeded (warning: NU1510 for Microsoft.Extensions.Http).

## 2026-07-15 — Resolution Flow Rewired to Python SSE API

**Trigger:** Architecture pivot — .NET API is now CRUD-only. Resolution orchestration moved to Python Resolution API with SSE streaming.

### Changes Made

1. **New `ResolutionApiClient` service** (`Services/ResolutionApiClient.cs`):
   - Calls `POST {ResolutionApi:BaseUrl}/resolve` with `{"ticket_number": "..."}`.
   - Uses `HttpCompletionOption.ResponseHeadersRead` + `ReadAsStreamAsync` for SSE consumption.
   - Returns `IAsyncEnumerable<ResolutionEvent>` — each event has `Stage`, `Status`, `Timestamp`, `Result`, `Error`.
   - 5-minute HttpClient timeout configured for long-running resolutions.

2. **Configuration** (`appsettings.json` / `appsettings.Development.json`):
   - Added `ResolutionApi:BaseUrl` key. Dev default: `http://localhost:8000`.

3. **Rewired `Detail.razor`**:
   - "Resolve with AI" button now navigates to `/tickets/{Number}/resolve` (SSE streaming page).
   - Removed old `ResolveTicketAsync()` method that called `POST /api/tickets/{number}/resolve` on .NET API.

4. **Replaced `RunProgress.razor`** (now at route `/tickets/{Number}/resolve`):
   - Consumes SSE stream in real-time, updates stage progress via `StateHasChanged()`.
   - Shows timeline with stages: Classifier → Incident/Request Fetch → Decomposer → Evaluator → Resolution.
   - Stage indicators: running (blue), completed (green), failed (red).
   - On completion: shows "Complete" badge, navigates back to ticket detail after 2s.
   - On error: shows error message with Retry button.

5. **Dead code removed**:
   - `TicketApiClient.ResolveTicketAsync()` — called old .NET resolve endpoint.
   - `TicketApiClient.GetRunAsync()` / `GetRunEventsAsync()` — polled old .NET run endpoints.
   - `StartResolveResponse`, `ResolveTicketRequest`, `WorkflowRunDetailResponse` DTOs.
   - `Microsoft.AspNetCore.SignalR.Client` NuGet package (unused).
   - `Microsoft.Extensions.Http` NuGet package (framework-provided, NU1510 warning gone).

6. **Build:** `dotnet build src/dotnet/AgenticResolution.sln` — 0 warnings, 0 errors.

### SSE Pattern Notes
- Blazor Server SSE: HttpClient runs server-side, pushes to component via `InvokeAsync(StateHasChanged)`.
- `ReadLineAsync` loop (not `EndOfStream` check) avoids CA2024 analyzer warning.
- SSE lines prefixed `data: ` contain JSON; blank lines and non-data lines are skipped.
- `HttpCompletionOption.ResponseHeadersRead` is critical — without it, HttpClient buffers the entire response.

**Integration Status (2026-05-06):**
- ✅ Blazor UI fully rewired to consume SSE from Python Resolution API directly
- ✅ Dead SignalR/polling code removed (0 errors, 0 warnings)
- ✅ ResolutionApiClient configured with 5-minute timeout for long-running workflows
- ✅ Ready to call `POST /resolve` on deployed ca-resolution-tocqjp4pnegfo (managed identity, external ingress)
- ✅ Dynamic stage rendering resilient to Python API schema evolution
