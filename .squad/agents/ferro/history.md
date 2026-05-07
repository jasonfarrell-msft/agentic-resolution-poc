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

## Core Context

**Phase 1 (2026-04-29):**
- Single-project Blazor Server (.NET 10) at `src/AgenticResolution.Web/`
- Routes: `/tickets` (list), `/tickets/{number}` (detail), `/tickets/new` (form)
- TicketApiClient wraps REST calls to ticket CRUD endpoint
- Built successfully; no deployment

**Phase 2 (2026-05-06):**
- Blazor integration with Python Resolution API (SSE stream consumption)
- Resolution streaming route with terminal event detection
- Instant navigation with loading indicators
- API client hardened against HTML-as-JSON responses

**Current state (2026-05-07):**
- ✅ Ticket loading restored: TICKETS_API_URL environment variable prioritized over ApiClient:BaseUrl config
- ✅ Ticket list renders live tickets from ca-api (98 available)
- ✅ Local builds pass; no Azure deployment yet
- ✅ Routing verified: `/tickets` is UI route; CRUD API is ca-api `/api/tickets`

---

## 2026-05-07 — Azure Web Deployment

✅ **Deployed** AgenticResolution.Web to Azure App Service `app-agentic-resolution-web` in `rg-agentic-res-src-dev`.

- **Build**: Succeeded with local .NET 10
- **API Configuration**: `TICKETS_API_URL` environment variable now prioritized over JSON defaults
- **Validation**: Live `/tickets` endpoint returns HTTP 200, ticket rows render correctly, no routing errors
- **Config**: App Service settings updated with `ApiClient__BaseUrl` and `ResolutionApi__BaseUrl`
- **Limitation**: HTTP-based validation only (no browser automation)
- **Status**: Operational with corrected ticket API configuration

  - Production/default Blazor config now sets `ApiClient:BaseUrl` to `https://ca-api-tocqjp4pnegfo.graybush-af9ee262.eastus2.azurecontainerapps.io`; the previous empty default left `/tickets` in the API-not-configured state after the Python Resolution API/SSE pivot.
  - `Program.cs` now also accepts `TICKETS_API_URL` as a fallback for `TicketApiClient`, matching the Resolution API environment variable naming used by the Python service.
  - `Components/Pages/Tickets/Index.razor` awaits `LoadTicketsAsync()` from `OnInitializedAsync` instead of fire-and-forget startup loading, and `TicketApiClient` now returns status/body details for failed REST calls.
  - Infra App Service settings now persist both `ApiClient__BaseUrl` and `ResolutionApi__BaseUrl` for `rg-agentic-res-src-dev`; no Azure deployment was performed during the fix.

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

## 2026-05-06T194800 — Deployment Verification

**Trigger:** Production deployment validation of latest Blazor fixes and Resolution API integration.

**Status:** ✅ Success

**Actions:**
- Deployed latest Blazor web app fixes to Azure App Service `app-agentic-resolution-web`
- Verified HTTPS 200 response at https://app-agentic-resolution-web.azurewebsites.net/
- Confirmed production `ResolutionApi__BaseUrl` configuration points to `ca-resolution` endpoint
- Validated frontend connectivity to backend Resolution API

**Result:** Production Blazor web app operational and correctly configured to call Python Resolution API.

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

### 2026-05-06 — Resolution UI stream hardening
- Fixed the Blazor resolve page so starting a resolution no longer blocks the render lifecycle; streaming now runs in the background and reports failures inline.
- Replaced brittle single-line SSE parsing with tolerant `data:` handling, EOF flushing, malformed-event warnings, and terminal-state detection for completed/resolved/escalated/failed-style events.
- Ticket details now surfaces Status / State and Assignee / Assigned To, with API model support for `status` and `assignee` fields.
- Production `ResolutionApi:BaseUrl` now points at the Azure Container Apps endpoint; development remains local.
- Verified with `dotnet build src\dotnet\AgenticResolution.sln --nologo`.

### 2026-05-06 — Instant navigation + ticket detail polish
- Ticket detail now renders immediately with a skeleton while data loads, reports load failures inline, and offers Retry / Back to Tickets actions instead of leaving a blank page.
- Status and Assigned To moved into compact header summary pills so the values are easier to scan and no longer repeat inside the summary section.
- Resolution streaming route keeps rendering immediately, shows a clear stream-start loading card until SSE events arrive, preserves terminal-state handling, and redirects back to ticket detail after terminal completion.
- Ticket list initial load now starts in the background with existing skeleton rows and a retry affordance on failure.
- Verified with `dotnet build src\dotnet\AgenticResolution.sln --nologo`.

### 2026-05-06 — Azure Web App deployment
- Published `src\dotnet\AgenticResolution.Web` in Release and deployed it to Azure App Service `app-agentic-resolution-web` in `rg-agentic-res-src-dev`.
- Production app setting `ResolutionApi__BaseUrl` is set to `https://ca-resolution-tocqjp4pnegfo.graybush-af9ee262.eastus2.azurecontainerapps.io`.
- Verified `https://app-agentic-resolution-web.azurewebsites.net/` returned HTTP 200 after deployment.

### Cross-Agent Update: Backend Verification (2026-05-07)

From Hicks (Backend Dev): Ticket loading issue was verified as frontend-side configuration, not backend failure.
- Backend API at `ca-api-tocqjp4pnegfo` is healthy and returns expected paged JSON
- Live DB has 98 tickets available
- JSON shape matches client expectations (camelCase, string enums)
- No CRUD endpoint changes needed

**Implication:** Frontend config fix (above) is complete solution. No backend work required for ticket loading.

**Shared blocker:** Local dotnet build requires .NET 10; host has .NET 9 only. Cannot validate fixes via local build.

---

- **2026-05-07 — Blazor ticket crash diagnosis: `/tickets` is shell, API URL precedence was wrong.**
  - Root cause: the Blazor route `/tickets` correctly returns the app shell, including dormant `#blazor-error-ui`, reconnect modal markup, and `_framework/blazor.web*.js`; the actual ticket-loading failure was `TicketApiClient` choosing `ApiClient:BaseUrl` before `TICKETS_API_URL`.
  - In Development-hosted environments, `src/dotnet/AgenticResolution.Web/appsettings.Development.json` sets `ApiClient:BaseUrl` to `https://localhost:7001`, so a deployed App Service running with Development settings could ignore the corrected `TICKETS_API_URL` ca-api setting and call localhost instead.
  - Fixed `src/dotnet/AgenticResolution.Web/Program.cs` so `TICKETS_API_URL` wins over JSON `ApiClient:BaseUrl`, preserving `ApiClient:BaseUrl` as the local/default fallback.
  - Validated with `dotnet build src/dotnet/AgenticResolution.sln --nologo` and a local Development run using `TICKETS_API_URL=https://ca-api-tocqjp4pnegfo.graybush-af9ee262.eastus2.azurecontainerapps.io`; `/tickets` rendered real `INC...` rows. No deployment performed.

### 2026-05-07 — Blazor ticket crash diagnosis complete

Diagnosed `/tickets` returning Blazor shell as expected behavior. Root cause was `TicketApiClient` preferring `ApiClient:BaseUrl` (localhost:7001) over environment `TICKETS_API_URL`. Updated `Program.cs` to prioritize `TICKETS_API_URL`. Validated with local build and run showing live INC ticket rows. Status: Complete, ready for deployment.

**Collaboration note:** Hicks confirmed backend API is healthy and serving expected JSON. No backend changes required.

### 2026-05-07 — Azure Web App redeploy with corrected tickets API precedence
- Built `src/dotnet/AgenticResolution.sln` and published `src/dotnet/AgenticResolution.Web` with local .NET 10 (`DOTNET_ROOT=$HOME/.dotnet`).
- Deployed the Release zip artifact to Azure App Service `app-agentic-resolution-web` in `rg-agentic-res-src-dev`.
- Confirmed App Service settings include `ApiClient__BaseUrl` and `TICKETS_API_URL` pointing at the tickets Container App, plus `ResolutionApi__BaseUrl` pointing at the resolution Container App.
- Verified live `https://app-agentic-resolution-web.azurewebsites.net/tickets` returns HTTP 200, renders ticket rows, and the returned markup does not contain the previous API URL precedence/localhost error text.
- HTTP-only validation was used; browser automation was not run in this environment.

## Learnings
### 2026-05-07 — Resolve with AI button no-op fixed

- **Root cause**: Ticket detail rendered successfully, but the Blazor app shell did not opt `Routes`/`HeadOutlet` into Interactive Server render mode, so `@onclick` handlers were not hydrated; the navigation-only Resolve button therefore appeared to do nothing.
- **Files changed**: `src/dotnet/AgenticResolution.Web/Components/App.razor` enables `InteractiveServer`; `src/dotnet/AgenticResolution.Web/Components/Pages/Tickets/Detail.razor` now uses a real href to `/tickets/{Number}/resolve` for resilient navigation.
- **Deployment**: Published and zip-deployed `src/dotnet/AgenticResolution.Web` to App Service `app-agentic-resolution-web` in `rg-agentic-res-src-dev`.
- **Validation**: `dotnet build src/dotnet/AgenticResolution.sln --nologo` succeeded; local and live HTTP checks confirmed ticket `INC0010101` renders a Resolve link to `/tickets/INC0010101/resolve`, and the resolve route returns the progress page.


### 2026-05-07 — Ticket detail SSR load fix
- **Root cause:** `Components/Pages/Tickets/Detail.razor` started `LoadDetailsAsync` as fire-and-forget from `OnParametersSetAsync`. Because the app renders pages with static SSR unless an interactive render mode is applied, the live detail route prerendered only the loading skeleton and did not include ticket data in the initial response.
- **Files changed:** `src/dotnet/AgenticResolution.Web/Components/Pages/Tickets/Detail.razor` now awaits detail loading during `OnParametersSetAsync`; `src/dotnet/AgenticResolution.Web/Services/TicketApiClient.cs` now URL-encodes the ticket number when composing `/api/tickets/{number}/details`.
- **Validation:** `dotnet build src/dotnet/AgenticResolution.sln --nologo` succeeded with .NET 10 from `~/.dotnet`. Local SSR validation against ca-api showed `INC0010102` detail content rendered, then the web app was deployed to Azure App Service `app-agentic-resolution-web` in `rg-agentic-res-src-dev`; live `https://app-agentic-resolution-web.azurewebsites.net/tickets/INC0010102` renders ticket detail content and no longer stays loading-only. Confirmed the web app's own `/api/tickets/INC0010102/details` returns the intentional 404 guard, so detail data comes from the configured tickets API base URL rather than the Blazor route.

### 2026-05-07 — Ticket detail list navigation
- **Root cause/user need:** The ticket detail page preserved Resolve with AI and detail content, but its primary header had no always-visible path back to `/tickets`, making list/detail triage feel like a dead end.
- **Files changed:** `src/dotnet/AgenticResolution.Web/Components/Pages/Tickets/Detail.razor` adds Bootstrap outline-secondary back-to-list links near the header, in the not-found state, and after the long detail content.
- **Deployment:** Published and zip-deployed `src/dotnet/AgenticResolution.Web` to Azure App Service `app-agentic-resolution-web` in `rg-agentic-res-src-dev`.
- **Validation:** `dotnet build src/dotnet/AgenticResolution.sln --nologo` succeeded with .NET 10 from `~/.dotnet`; source and live checks confirmed detail markup includes `href="/tickets"` and “Back to ticket list” on `/tickets/INC0010104`.

---

## 2026-05-07 — Ticket Detail SSR Loading Fix

✅ **Implemented and Deployed**

**Root Cause:** Detail page was fire-and-forget loading in `OnParametersSetAsync`, so server-side prerender only shipped loading skeleton.

**Fix Applied:**
- `Detail.razor`: Changed lifecycle to await `StartLoadingDetailsAsync()` so `/tickets/{Number}` prerenders actual detail data before response sent
- `TicketApiClient.cs`: Added URL encoding for ticket number before calling detail endpoint

**Build & Deploy:**
- `dotnet build src/dotnet/AgenticResolution.sln --nologo` succeeded (.NET 10)
- Deployed to App Service `app-agentic-resolution-web` in `rg-agentic-res-src-dev`
- Live route `/tickets/INC0010102` now renders ticket detail content (no skeleton)

**Validation:**
- Local detail page rendered live ticket INC0010102 from ca-api
- Live URL `https://app-agentic-resolution-web.azurewebsites.net/tickets/INC0010102` shows full detail data

→ Decision recorded: `.squad/decisions.md` / "Ferro — Ticket Detail Static SSR Loading Fix" (2026-05-07)

### 2026-05-07 — Resolving screen terminal-result UX fix
- **Root cause:** `ResolutionEvent.IsTerminal` treated any `status: "completed"` event as terminal. The Python SSE stream emits `completed` for ordinary executor stages, so the resolving page could show completion UI before the workflow emitted its terminal `resolved`/`escalated` event.
- **Files changed:** `src/dotnet/AgenticResolution.Web/Services/ResolutionApiClient.cs` now requires an explicit terminal flag or result-specific terminal status; `Components/Pages/Tickets/RunProgress.razor` now shows completion UI only after terminal result, labels resolved vs. escalated, and provides a Ticket Detail link instead of auto-redirecting; `Components/Pages/Tickets/Detail.razor` and `Services/TicketApiClient.cs` now surface escalated agent action as ticket detail status when present.
- **Deployment:** Published and zip-deployed `src/dotnet/AgenticResolution.Web` to Azure App Service `app-agentic-resolution-web` in `rg-agentic-res-src-dev`.
- **Validation:** `dotnet build src/dotnet/AgenticResolution.sln --nologo` succeeded. A terminal parsing check confirmed stage-level `completed` is non-terminal while terminal `resolved`/`escalated` events are recognized. Live `/tickets/INC0010102/resolve` initially rendered only the running state (no "Resolution Complete"); live resolution API completed with terminal `resolved`; live `/tickets/INC0010102?refresh=live-validation` rendered the updated Resolved status.

### 2026-05-07 — Resolving Screen Final Result Display & Detail Navigation
- **Outcome 1:** Fixed resolving screen to wait for terminal SSE event before showing result
  - Removed static ticket status from resolving screen
  - Completion UI now displays resolved/escalated result with clear Ticket Detail link
  - Removed auto-redirect in favor of explicit user action
  - Deployed to app-agentic-resolution-web
- **Outcome 2:** Added detail-page ticket list navigation
  - Bootstrap btn-outline-secondary "← Back to ticket list" link near detail header
  - Secondary link placement after long detail content for scroll-free navigation
  - Live validation: /tickets/INC0010104 verified with working back button
- **Decisions recorded:**
  - `.squad/decisions.md` / "Ferro — Resolving Screen Result Link" (2026-05-07)
  - `.squad/decisions.md` / "Ferro — Ticket Detail Back-to-List Navigation" (2026-05-07)
- **Status:** Deployed and live
