# Squad Decisions

### 2026-05-06: Bishop — ca-resolution Container App Deployed

**By:** Bishop (AI/Agents Specialist)  
**Requested by:** Jason Farrell

**Status:** Deployed and verified.

**What was done:**
1. Deleted unused `ca-agres-tocqjp4pnegfo` container app
2. Fixed Dockerfile COPY path (build context is `src/python/`, not repo root)
3. Fixed pydantic version pin (`>=2.11.0` required by `mcp` package)
4. Built image via `az acr build` (cloud build, no local Docker needed)
5. Created `ca-resolution-tocqjp4pnegfo` with system-assigned managed identity
6. Granted "Cognitive Services OpenAI User" RBAC on Azure OpenAI resource
7. Verified health endpoint returns `{"status":"healthy"}`

**Container App:**
- Name: `ca-resolution-tocqjp4pnegfo`
- FQDN: `https://ca-resolution-tocqjp4pnegfo.graybush-af9ee262.eastus2.azurecontainerapps.io`
- Port: 8000 (external ingress)
- Scale: 0–1 replicas
- Identity: System-assigned MI (ACR pull + Azure OpenAI access)

**Environment:**
- `AZURE_AI_ENDPOINT` → oai-agentic-res-src-dev
- `MCP_SERVER_URL` → ca-mcp internal URL
- `TICKETS_API_URL` → ca-api external URL

**Impact:**
- Blazor UI can now call `POST /resolve` on this endpoint for SSE-streamed agent workflow
- Replaces the previously unused ca-agres stub
- Uses same Azure AI Foundry endpoint as classifier/incident/request agents

---

### 2026-05-06: Ferro — Resolution UI Rewired to Python SSE API

**By:** Ferro (Frontend Developer)  
**Status:** Implemented

## Context

Architecture pivot: .NET API is now CRUD-only (tickets, KB articles). Resolution orchestration moved to a **Python Resolution API** (`POST /resolve`) that streams SSE events.

## Decision

Blazor calls the Python Resolution API **directly** (not through .NET). The SSE stream is consumed server-side via `HttpClient.SendAsync(ResponseHeadersRead)` + `StreamReader.ReadLineAsync`, pushing updates to the Razor component via `InvokeAsync(StateHasChanged)`.

## Key Choices

1. **Separate `ResolutionApiClient`** — not merged into `TicketApiClient`. Different base URL, different concern (streaming vs REST), different timeout (5 min vs default).

2. **New route `/tickets/{Number}/resolve`** — replaces old `/tickets/{Number}/runs/{RunId}`. No run ID concept from Python API; the stream is the entire lifecycle.

3. **Stage dictionary** — events update a `Dictionary<string, StageState>` keyed by stage name. Stages appear dynamically as events arrive (no hardcoded list needed), making the UI resilient to Python API adding/removing stages.

4. **Auto-redirect on completion** — 2-second delay then navigate to ticket detail. User sees final "Complete" state briefly before redirect.

5. **Retry on error** — button re-invokes `POST /resolve`. No deduplication logic (Python API handles idempotency if needed).

## Removed

- `POST /api/tickets/{number}/resolve` call (endpoint deleted from .NET)
- `GET /api/runs/{runId}` / `GET /api/runs/{runId}/events` polling
- SignalR client package (no longer needed)
- Old DTOs: `StartResolveResponse`, `ResolveTicketRequest`, `WorkflowRunDetailResponse`

## Configuration

| Key | Purpose | Dev Default |
|-----|---------|-------------|
| `ResolutionApi:BaseUrl` | Python Resolution API | `http://localhost:8000` |
| `ApiClient:BaseUrl` | .NET CRUD API | `https://localhost:7001` |

## Impact

- **Detail page**: "Resolve with AI" navigates to new streaming page.
- **Ticket CRUD**: Unaffected — still uses `TicketApiClient` against .NET API.
- **Old run history**: The "Workflow Runs" section on ticket detail still displays runs from .NET API (historical data). No new runs will be created there.

---

### 2026-05-05: Bishop — Split DecomposerAgent into IncidentDecomposer + RequestDecomposer

**By:** Bishop (AI/Agents Specialist)  
**Requested by:** Jason Farrell

**Status:** Implemented and validated.

**Context:** The `DecomposerAgent` was a single agent performing question-driven KB retrieval for all ticket types. Incident tickets require **failure-mode thinking** (root cause, scope, recovery); service request tickets require **fulfillment thinking** (prerequisites, procedure, approval). A single SYSTEM_PROMPT cannot simultaneously frame both mindsets at the level of precision needed for accurate KB retrieval.

**Decision:** Split into two specialized agents with distinct SYSTEM_PROMPTs, question archetypes, and KB search strategies:

- **IncidentDecomposer** (`src/agents_py/agents/incident_decomposer/__init__.py`): Diagnostic framing with question archetypes (ROOT CAUSE, SCOPE, RECOVERY, VALIDATION) and search strategy (symptom patterns, component/service name, error codes, troubleshooting KB tags)
- **RequestDecomposer** (`src/agents_py/agents/request_decomposer/__init__.py`): Process-oriented framing with question archetypes (PREREQUISITES, PROCEDURE, APPROVAL, VERIFICATION) and search strategy (service/software/access name, onboarding procedures, approval workflows, how-to KB tags)

Both produce identical `ResolutionAnalysis` messages; `EvaluatorAgent` unchanged. Deleted: `src/agents_py/agents/decomposer/__init__.py`, `decomposer_agent` reference in `devui_serve.py`.

**Rationale:** (1) Better KB retrieval accuracy — type-specific search framing surfaces more relevant articles; (2) Cleaner intent preservation — incident/request distinction from classification carried through decomposition; (3) Prompt precision — incident engineers ask "what failed?" vs. service desk asks "what's needed?" — different prompts; (4) Debuggability — failure mode clearly incident-related or request-related, not ambiguous.

**Tradeoffs:** (+) More accurate question generation, better KB targeting; (-) Two agents to maintain, slightly more workflow complexity.

---

### 2026-05-04: Hicks — Gitignore baseline established

**By:** Hicks (Backend Dev)

**Status:** Implemented (2 commits on main, not pushed)

**Decision:** Added .NET standard .gitignore at repo root. Modified to preserve `.squad/log/` (project documentation) via negation pattern `!.squad/log/` after `[Ll]og/` exclusion.

**Rationale:** (1) Repo had no .gitignore; 184 build artifacts (155 bin/, 29 obj/) were tracked; (2) `.squad/log/` contains project session logs and architectural decisions (tracked), not build logs; (3) Negation pattern is specific and surgical.

**Files:** Created `/.gitignore` (487 lines via `dotnet new gitignore` + `!.squad/log/` negation). Untracked 184 build artifacts with `git rm --cached`. Commits: 9c98efa (add .gitignore), 7e121fd (untrack artifacts). No further cleanup needed.

---

### 2026-04-29T21:00:00Z: Apone & Bishop — Phase 2 Architecture & Search Index Schema

**By:** Apone (Lead) & Bishop (AI/Agents)

**What (Apone – Architecture):** Phase 2 establishes five integrated subsystems: (1) Azure Function (Consumption, .NET 10) as webhook receiver with HMAC validation; (2) Single AI Search index `tickets-index` with hybrid search (BM25 + vector, semantic reranking, text-embedding-3-small 1536d embeddings); (3) Two Foundry agents (gpt-4o-mini): triage-agent (classify, auto-resolve, escalate) and resolution-summarizer (polish resolutions); (4) PUT /api/tickets/{id} endpoint accepting structured agent results (state, assignedTo, resolutionNotes, agentAction, agentConfidence, matchedTicketNumber) with HMAC validation; (5) Bicep modules for AI Search (Basic), OpenAI (gpt-4o-mini + text-embedding-3-small), Foundry Hub/Project, Function App (Consumption), Storage Account. Incremental cost ~$78–81/mo. Nine gate criteria (G1–G9) lock sequencing: Hicks owns G1–G7 (infra/backend), Vasquez owns G8–G9 (tests), Bishop gated on G1–G7 for agent work.

**What (Bishop – Search Index):** Single index `tickets-index` with 14 fields: keyword fields (id, number, shortDescription, description, category, priority, state, assignedTo, caller, resolutionNotes) + vector field (contentVector, 1536d, cosine HNSW). Semantic config `ticket-semantic` uses shortDescription as title, [description, resolutionNotes] as content, [category, number] as keywords. Hybrid search strategy: embed concatenation of [shortDescription + description + category], query via BM25 + vector similarity, rerank semantically. Top 5 results passed to triage agent. Seed 25 pre-resolved IT scenarios (password resets, VPN, Outlook, etc.) into both SQL and index at gate time (G2/G7).

**Why:** Phase 2 kickoff locked. Architecture avoids premature complexity (no Durable Functions, no multi-index KB corpus, no visual Logic Apps), prioritizes demo reproducibility (`azd up`), enables parallel team tracks (Hicks on infra, Vasquez on tests, Bishop on standby), and establishes unambiguous dependencies via 9 gate criteria. Demo cost remains low (~$103/mo combined). All decisions made once; no blocking questions.

---

### 2026-04-29T17:50:00Z: Vasquez — Phase 2 SQL test infrastructure plan

**By:** Vasquez (Tester)

---

### 2026-05-05T13:43:19-04:00: User directive — Jason Farrell

**What:** Resolve should fire the webhook and return. Once returned successfully, the frontend should start listening for changes so the user can track the resolution.  
**Why:** User request — captured for team memory.

---

### 2026-05-05: Ferro — Resolve Button Flow & Progress Listening

**By:** Ferro (Frontend Developer)  
**Requested by:** Jason Farrell  
**Status:** Specified — awaiting AgenticResolution.Web implementation  
**Related:** `apone-blazor-resolution-architecture.md`, `bishop-webhook-run-correlation.md`, `copilot-directive-2026-05-05T134319-resolve-webhook.md`

**Summary:** Clicking "Resolve with AI" must not block waiting for agent workflow completion. Instead:
1. The API enqueues the resolution workflow and returns immediately with a run identifier (HTTP 202 Accepted).
2. The frontend navigates to a progress view and starts listening for workflow events via `GET /api/runs/{runId}/events` (polling) or SignalR hub `/hubs/runs` group `run-{runId}` (future push).
3. The user sees real-time progression through classifier → decomposer → evaluator → summarizer stages.

**API Contract:**
- `POST /api/tickets/{number}/resolve` → HTTP 202 with `{ runId, ticketNumber, ticketId, statusUrl, eventsUrl }`
- Idempotent: If a run for this ticket is already Pending/Running, return HTTP 200 with existing run.
- Frontend then polls `GET /api/runs/{runId}/events` to stream executor events until terminal state (Completed/Escalated/Failed).

**Key decision:** Resolving the user's perceived ambiguity — the resolve endpoint is **async and returns immediately**; async orchestration happens in background via ResolutionRunnerService; frontend drives the UI state via polling.

---

### 2026-05-05: Hicks — Corrected Backend Contract: Manual Resolve Webhook Flow

**By:** Hicks (Backend Dev)  
**Status:** Implemented  
**Context:** User clarification on Phase 2.5 manual resolve flow — resolve endpoint should fire webhook to Azure Function receiver, not process in-process.

**Problem:** Initial implementation of `POST /api/tickets/{number}/resolve` created a WorkflowRun and enqueued to internal `ResolutionQueue` processed by `ResolutionRunnerService`, conflating the API with the orchestrator.

**Solution:** Corrected to follow Phase 2 webhook-driven architecture:
1. **Webhook Payload Extension:** Added `resolution.started` event type with `run_id` field.
2. **Resolve Endpoint Behavior:**
   - Look up ticket by number
   - Check for existing Pending/Running WorkflowRun — if found, return HTTP 200 with existing run (idempotent)
   - Create new WorkflowRun with status Pending, TriggeredBy = "manual"
   - **Enqueue webhook** via `IWebhookDispatcher` with `ForResolutionStarted(ticket, runId)`
   - Return HTTP 202 Accepted immediately
3. **Response (HTTP 202 Accepted):** `{ runId, ticketNumber, ticketId, statusUrl, eventsUrl }`
4. **Config flag:** `Webhook:AutoDispatchOnTicketWrite` (default `false` in appsettings.json) — when false, ticket create/update do NOT enqueue webhooks.
5. **Azure Function Responsibilities:** Receiver validates HMAC, deduplicates via event_id, sets run status to Running, invokes agent orchestration, writes WorkflowRunEvent rows, updates run status to Completed/Escalated/Failed, optionally calls back to `PUT /api/tickets/{id}`.
6. **Frontend Listen Flow:** POST /resolve → 202 with runId → start polling `GET /api/runs/{runId}/events` → render executor lanes → stop when run reaches terminal state.

**Verification:** Build succeeded; webhook payload extended; resolve endpoint fires webhook; create/update respect config flag (default false).

**Known gaps:** (1) Azure Function not yet implemented; (2) SignalR hub not yet implemented (frontend must poll); (3) ResolutionRunnerService now unused (can be deleted or repurposed).

**Contract now matches user directive:** Resolve fires webhook and returns immediately with complete context for frontend to start listening for progress events.

---

### 2026-05-05: Hicks — Decision: Ticket API Contract Extensions for Blazor UI

**By:** Hicks (Backend Dev)  
**Status:** Implemented  
**Context:** Phase 2.5 Blazor frontend needs enhanced filtering, comments, manual resolution flow, and workflow run visibility.

**Decision:** Extended the ticket API with new endpoints while preserving backward compatibility:

1. **Enhanced List Filtering (`GET /api/tickets`):**
   - `assignedTo` (exact match; `"unassigned"` sentinel for NULL)
   - `state` (comma-separated; e.g., `"New,InProgress"`)
   - `category` (exact match)
   - `priority` (comma-separated; e.g., `"Critical,High"`)
   - `q` (substring match on ShortDescription/Description, case-insensitive)
   - `sort` (enum: `"created"` | `"modified"`, default `"created"`)
   - `dir` (enum: `"asc"` | `"desc"`, default `"desc"`)
   - `page` (≥1), `pageSize` (1–100)
   - Sort field whitelist enforced via switch expression (no SQL injection).

2. **Ticket Detail Endpoint (`GET /api/tickets/{number}/details`):**
   - Response: `TicketDetailResponse { Ticket, Comments[], Runs[] }`
   - Single round-trip for detail page; existing `/tickets/{number}` unchanged (backward compatible).

3. **Comments:**
   - `GET /api/tickets/{number}/comments` → `CommentResponse[]` ordered by CreatedAt asc
   - `POST /api/tickets/{number}/comments` body `{ author, body, isInternal }` → `CommentResponse`
   - Validation: author 1–100 chars, body 1–4000 chars. Append-only (no update/delete in Phase 2.5).
   - **Auth caveat:** Author is free-text (no identity verification); flagged for follow-up before external demo.

4. **Manual Resolve Trigger (`POST /api/tickets/{number}/resolve`):**
   - Request body (optional): `{ note?: string }`
   - Response: HTTP 202 Accepted with `{ runId, ticketNumber, ticketId, statusUrl, eventsUrl }`
   - **Idempotency:** If Pending/Running run exists, returns HTTP 200 with existing run.
   - Creates `WorkflowRun` record with status Pending; does NOT immediately invoke agent (that's ResolutionRunnerService's job).

5. **Workflow Run Visibility:**
   - `GET /api/tickets/{number}/runs` → `WorkflowRunResponse[]` ordered by StartedAt desc
   - `GET /api/runs/{runId}` → `WorkflowRunDetailResponse { Run, Events[] }`
   - `GET /api/runs/{runId}/events` → `WorkflowRunEventResponse[]` ordered by Sequence asc
   - **Note:** Current implementation returns all events from DB. Live streaming (SSE/SignalR) not yet implemented.

6. **Webhook Auto-Dispatch Flag:**
   - Config key: `Webhook:AutoDispatchOnTicketWrite` (boolean, default `false`)
   - When false (default): CreateAsync/UpdateAsync skip webhook dispatch.
   - When true: Webhook dispatch fires as in Phase 1.
   - Rationale: Phase 2.5 decouples ticket write from agent invocation; resolution is manual via POST /resolve.

**Data Model Changes:**
- **TicketComment:** Id, TicketId (FK), Author, Body, IsInternal, CreatedAt; index on TicketId.
- **WorkflowRun:** Id, TicketId (FK), Status, TriggeredBy, Note, StartedAt, CompletedAt, FinalAction, FinalConfidence; composite index (TicketId, Status).
- **WorkflowRunEvent:** Id, RunId (FK), Sequence, ExecutorId, EventType, Payload (JSON), Timestamp; composite index (RunId, Sequence).
- New indexes on Ticket: UpdatedAt, AssignedTo, Category.

**Backward Compatibility:** Existing `GET /api/tickets/{number}` unchanged; existing POST/PUT unchanged; webhook behavior changed only if config flag is true (safe default).

**Known gaps:** (1) Resolution runner not implemented (creates Pending run, doesn't invoke pipeline); (2) No SignalR hub (Ferro must poll); (3) No auth on comments/resolve; (4) Infra Bicep stubs are 0-byte (blocks deployment).

**Validation:** Build succeeded; EF migration valid; webhook auto-dispatch flag tested with default false.

---

### 2026-05-05: Bishop — Webhook/RunId Correlation Contract & Async Workflow Pattern

**By:** Bishop (AI/Agents Specialist)  
**Status:** Implemented  
**Context:** User directive clarified contract for manual resolution workflow; initial implementation had a critical bug where resolve endpoint enqueued webhook but never started agent orchestration.

**Fixed Architecture:**

**Resolve Endpoint Contract:**
```
POST /api/tickets/{number}/resolve
Request:  { note?: string }

Flow:
  1. Create WorkflowRun (status=Pending, TriggeredBy=manual)
  2. Enqueue ResolutionRunRequest → IResolutionQueue (triggers orchestration)
  3. (Optional) Fire webhook "resolution.started" if Webhook:FireOnResolutionStart=true
  4. Return 202 Accepted with { runId, ticketNumber, ticketId, statusUrl, eventsUrl }

Response: 
{
  "runId": "00000000-0000-0000-0000-000000000000",
  "ticketNumber": "INC0010001",
  "ticketId": "...",
  "statusUrl": "/api/runs/{runId}",
  "eventsUrl": "/api/runs/{runId}/events"
}
```

**Critical fix:** Changed from `dispatcher.Enqueue(WebhookPayload.ForResolutionStarted(...))` to `resolutionQueue.Enqueue(new ResolutionRunRequest(run.Id, ticket.Number))`. API returns immediately (202 Accepted); background orchestration starts asynchronously; frontend polls `GET /api/runs/{runId}/events` for progress.

**Webhook Event Naming & RunId Correlation:**
- `ticket.created`: Ticket created (POST /tickets), config flag `Webhook:AutoDispatchOnTicketWrite`, no RunId
- `ticket.updated`: Ticket updated (PUT /tickets/{id}), config flag `Webhook:AutoDispatchOnTicketWrite`, no RunId
- `resolution.started`: Resolve endpoint called, config flag `Webhook:FireOnResolutionStart`, includes RunId ✅
- `workflow.running`: Pending→Running transition, config flag `Webhook:FireOnWorkflowProgress`, includes RunId ✅
- `workflow.completed`: Run completes (Completed status), config flag `Webhook:FireOnWorkflowProgress`, includes RunId ✅
- `workflow.escalated`: Run completes (Escalated status), config flag `Webhook:FireOnWorkflowProgress`, includes RunId ✅
- `workflow.failed`: Run fails (Failed status), config flag `Webhook:FireOnWorkflowProgress`, includes RunId + ErrorMessage ✅

**WebhookPayload Schema:** All workflow events carry `run_id` for correlation. External systems can store `run_id` → `ticket.number` mapping, listen for subsequent `workflow.*` events.

**Configuration Flags (all opt-in):**
```json
{
  "Webhook": {
    "TargetUrl": "https://external-system.example.com/webhooks",
    "Secret": "...",
    "AutoDispatchOnTicketWrite": false,
    "FireOnResolutionStart": false,
    "FireOnWorkflowProgress": false
  }
}
```

**ResolutionRunnerService Changes:**
- On Pending→Running transition: Fire `workflow.running` event
- On completion: Fire `workflow.completed` or `workflow.escalated`
- On orchestration failure: Fire `workflow.failed` with error message

**Frontend Contract (no webhook involvement):** 
```
User clicks "Resolve with AI"
  → POST /api/tickets/{number}/resolve
  → Returns 202 with runId
  → Navigate to /tickets/{number}/runs/{runId}
  → Poll GET /api/runs/{runId}/events every 2-3 seconds
  → Display executor lanes: ClassifierExecutor → IncidentFetchExecutor → ...
  → Stop polling when run.status ∈ {Completed, Escalated, Failed}
```

**Files Changed:**
- `src/dotnet/AgenticResolution.Api/Api/TicketsEndpoints.cs`: StartResolveAsync now enqueues to IResolutionQueue (not IWebhookDispatcher).
- `src/dotnet/AgenticResolution.Api/Agents/ResolutionRunnerService.cs`: ProcessRunAsync fires workflow progress webhooks.
- `src/dotnet/AgenticResolution.Api/Webhooks/WebhookDispatchService.cs`: Added ErrorMessage property and factory methods ForWorkflowRunning/Completed/Escalated/Failed.

**Behavior Guarantees:**
- ✅ Asynchronous execution: POST /resolve returns 202 immediately.
- ✅ Webhook RunId correlation: All workflow webhooks carry run_id.
- ✅ Opt-in webhooks: All firing controlled by config flags; default all disabled.
- ✅ No frontend dependency on webhooks: Frontend polls GET /api/runs/{runId}/events directly.
- ✅ Idempotent re-trigger: Duplicate POST /resolve returns HTTP 200 with existing runId if Pending/Running.

**Testing contract:** (1) Manual resolution triggers orchestration; (2) Webhook firing respects config flags; (3) RunId correlation in webhook payload; (4) Idempotency; (5) Failure path.

**Integration points for Hicks:** SignalR Hub (Phase 3) — create `IWorkflowProgressBroadcaster` wrapping persistence + SignalR.

---

### 2026-05-05: Bishop — Workflow Event Progress Surface for Manual Resolution

**By:** Bishop (AI/Agents Specialist)  
**Status:** Implemented and verified  
**Context:** Manual "Resolve with AI" flow must provide live workflow progression to Blazor UI (executor lanes, event sequence, final status).

**What Changed:**

**1. Progress Tracking Infrastructure:**
- Created `IWorkflowProgressTracker` interface — contract for emitting executor events.
- Created `WorkflowProgressTracker` implementation — persists events to `WorkflowRunEvent` table with monotonic sequence numbers.
- Key methods: ExecutorStartedAsync, ExecutorRoutedAsync, ExecutorOutputAsync, ExecutorErrorAsync, ExecutorCompletedAsync.
- Rationale: Decoupled progress tracking from orchestration logic; future enhancement can broadcast to SignalR hub without code duplication.

**2. AgentOrchestrationService Instrumentation:**
- Modified `ProcessTicketAsync` to accept optional `runId` and `IWorkflowProgressTracker progress` parameters.
- When `progress` provided, orchestrator emits events at each stage (ClassifierExecutor, IncidentFetchExecutor, IncidentDecomposerExecutor, EvaluatorExecutor, ResolutionExecutor OR EscalationExecutor).
- Executor IDs mirror Python workflow structure (documented in `WORKFLOW_SEQUENCE_NAMES.md`).
- Backward compatibility: Existing callers omitting `runId` and `progress` get same behavior (no event emission).

**3. Resolution Runner Service:**
- Created `IResolutionQueue` / `ResolutionQueue` — in-memory channel (512-item capacity) for resolution run requests.
- Created `ResolutionRunnerService` (BackgroundService) — dequeues requests, invokes orchestrator with progress tracking, updates WorkflowRun status.
- Flow: POST /resolve → Create WorkflowRun (Pending) → Enqueue ResolutionRunRequest → Return 202. Background: Dequeue, set status Running, invoke orchestrator, on success set Completed/Escalated/FinalAction/FinalConfidence, on failure set Failed + emit ExecutorError.
- No automatic agent triggering: Resolution runner ONLY invoked via explicit POST /resolve. Webhook path (Create/Update ticket) remains decoupled; webhook auto-dispatch still gated by config flag (default false).

**4. Endpoint Integration:**
- Modified `StartResolveAsync` in `TicketsEndpoints` to inject `IResolutionQueue` and enqueue run after persisting.
- Idempotency: If Pending/Running run exists, returns HTTP 200 with existing runId (no duplicate run).

**5. Service Registration:**
- `Program.cs` registers:
  - `IWorkflowProgressTracker` (scoped) → `WorkflowProgressTracker`
  - `IResolutionQueue` (singleton) → `ResolutionQueue`
  - `ResolutionRunnerService` (hosted service)
- Lifetime rationale: WorkflowProgressTracker scoped (fresh per run, safe for IServiceScopeFactory in background service); ResolutionQueue singleton (shared channel); ResolutionRunnerService hosted (single worker).

**6. Documentation:**
- Created `WORKFLOW_SEQUENCE_NAMES.md` — executor sequence (ClassifierExecutor, IncidentFetchExecutor, IncidentDecomposerExecutor, EvaluatorExecutor, ResolutionExecutor OR EscalationExecutor), event types (Started, Routed, Output, Error, Completed), UI display guidance for Ferro.

**Behavior Guarantees:**
- ✅ Explicit execution only: No hidden agent runs; resolution only via POST /resolve.
- ✅ Progress visibility: Every executor transition persisted to WorkflowRunEvent; UI can reconstruct exact sequence.
- ✅ No silent failures: Agent pipeline failure → run transitions Failed, ExecutorError event emitted.
- ✅ Idempotent re-trigger: Clicking Resolve twice for same ticket returns existing run if Pending/Running; no duplicate processing.

**Files Changed:**
- **New:** `IWorkflowProgressTracker.cs`, `WorkflowProgressTracker.cs`, `ResolutionRunnerService.cs`, `WORKFLOW_SEQUENCE_NAMES.md`
- **Modified:** `AgentOrchestrationService.cs` (added runId/progress params, emit events), `TicketsEndpoints.cs` (StartResolveAsync enqueues to IResolutionQueue), `Program.cs` (register new services)
- **No changes to:** WebhookDispatchService, TicketsEndpoints.CreateAsync/UpdateAsync (already gated by config flag).

**Integration points for Hicks:** IWorkflowProgressTracker is the contract; if Hicks implements SignalR hub, create `SignalRWorkflowProgressTracker` that broadcasts to `run-{runId}` group in addition to persisting events. No merge conflicts; WorkflowRun/WorkflowRunEvent models already in place.

**Integration points for Ferro:**
- Endpoint: `GET /api/runs/{runId}/events` returns all events ordered by sequence.
- Polling strategy: Poll every 2-3 seconds while WorkflowRun.Status is Pending/Running; stop when Completed/Escalated/Failed.
- Display sequence: See `WORKFLOW_SEQUENCE_NAMES.md` for executor IDs and event types.
- Future: When Hicks adds SignalR hub, Ferro can subscribe to `run-{runId}` group for push updates.

**Tradeoffs:**
- ✅ Pros: Clean separation (webhook path vs. manual resolution path); progress events enable DevUI-style executor lanes; no blocking behavior; idempotent re-trigger.
- ⚠️ Cons: Polling `GET /api/runs/{runId}/events` every 2-3 seconds (acceptable for Phase 2.5; SignalR is Phase 3 enhancement); executor events synthesized by orchestrator, not emitted by agents (future: agents stream structured progress); single background worker processes all runs sequentially (acceptable for demo; Phase 3 can parallelize).

**Open questions:** (1) SignalR hub — Hicks to implement; (2) Request classification — RequestDecomposerExecutor will appear when enabled.

**Verification:** Build succeeded (`dotnet build` passed with 1 unrelated package version warning). Manual test plan: create ticket → no agent fires (unless AutoDispatchOnTicketWrite=true) → POST /resolve → 202 with runId → poll GET /api/runs/{runId} → status transitions Pending→Running→Completed/Escalated → GET /api/runs/{runId}/events → executor events sequence appears → retry POST /resolve for same ticket → 200 with existing runId (idempotent) → agent failure scenario → run transitions Failed, error event recorded.

**Next steps:** Ferro implements workflow progression UI using WORKFLOW_SEQUENCE_NAMES.md; Hicks optionally replaces WorkflowProgressTracker with SignalR-enabled version; Vasquez writes integration tests; Bishop standby for feedback.

---

### 2026-05-05: Apone — Architecture Decision: Blazor Frontend, Manual Resolve Flow, Workflow Visibility

**By:** Apone (Lead/Architect)  
**Status:** Proposed — pending team acknowledgement before implementation  
**Scope:** Phase 2.5 — Blazor UI rebuild + decoupling agent pipeline from webhook dispatch + workflow progress visibility.

**Summary:** Add new `AgenticResolution.Web` project (Blazor Web App, .NET 10, Interactive Server render mode) as separate deployment from Api. Extract shared DTOs into `AgenticResolution.Contracts` library. Implement new API endpoints for enhanced filtering, comments, manual resolve trigger, workflow run visibility. Decouple agent pipeline invocation from webhook dispatch — resolution only happens via explicit POST /resolve (not auto-enqueue on ticket create/update). Implement ResolutionRunnerService (BackgroundService) to dequeue resolution requests and invoke agent orchestration with progress tracking. Implement progress event persistence to WorkflowRunEvent table and optional SignalR hub for live Blazor UI updates.

**Solution Topology:**
- **AgenticResolution.Web** (Blazor Server, .NET 10, Interactive Server) → App Service for Linux, WebSockets enabled, MI + Key Vault read.
- **AgenticResolution.Api** (.NET API) → Container Apps (external ingress), pinned to 1 replica (SignalR scale-out consideration).
- **AgenticResolution.Contracts** → Shared DTOs (TicketResponse, PagedResponse<T>, TicketDetailResponse, CommentResponse, WorkflowRunResponse, WorkflowRunEventResponse), referenced by Api/Web/McpServer.
- **Backend components:** ResolutionRunnerService (in-process BackgroundService in Api), SignalR hub `/hubs/runs` group `run-{runId}` (Phase 2.5 optional; Phase 3 planned).

**API Contract Changes (Hicks owns):**
1. Enhanced `GET /api/tickets` filtering (assignedTo, state list, category, priority list, q, sort/dir, paging).
2. `GET /api/tickets/{number}/details` → TicketDetailResponse with Ticket, Comments[], Runs[].
3. Comments: `GET /api/tickets/{number}/comments`, `POST /api/tickets/{number}/comments` (author, body, isInternal; append-only; free-text author).
4. Manual resolve: `POST /api/tickets/{number}/resolve` (optional { note }) → HTTP 202 with { runId, statusUrl, eventsUrl }; idempotent.
5. Workflow runs: `GET /api/tickets/{number}/runs`, `GET /api/runs/{runId}`, `GET /api/runs/{runId}/events` (SSE stream or polling; backed by in-process pub/sub + SQL).
6. Config flag: `Webhook:AutoDispatchOnTicketWrite` (default false) — when false, Create/Update skip webhook enqueue; when true, behaves as Phase 1.

**Database/Model Changes (Hicks owns; Vasquez writes tests):**
- **TicketComment:** Id (PK), TicketId (FK, cascade delete), Author, Body, IsInternal, CreatedAt, index (TicketId).
- **WorkflowRun:** Id (PK), TicketId (FK, cascade delete), Status (Pending=0, Running=1, Completed=2, Failed=3, Escalated=4), TriggeredBy, Note, StartedAt, CompletedAt, FinalAction, FinalConfidence, composite index (TicketId, Status).
- **WorkflowRunEvent:** Id (PK), RunId (FK, cascade delete), Sequence (per-run monotonic), ExecutorId, EventType, Payload (JSON), Timestamp, composite index (RunId, Sequence).
- New indexes on Ticket: UpdatedAt, AssignedTo, Category.
- Single migration: `20260507000000_AddCommentsAndWorkflowRuns`. No data backfill required.

**Removing Automatic Webhook → Agent Trigger:**
- **Delete implicit agent pipeline run inside WebhookDispatchService** — agent pipeline MUST only run via explicit POST /resolve.
- **Keep webhook plumbing but make opt-in:** Gate enqueue behind config flag `Webhook:AutoDispatchOnTicketWrite` (default false in Phase 2.5). When false, Create/Update skip dispatcher.Enqueue; when true, behaves as Phase 1.
- **Move orchestration to ResolutionRunner:** POST /resolve → create WorkflowRun (Pending), enqueue ResolutionRunRequest(runId, ticketNumber) → return 202. ResolutionRunner (BackgroundService) → dequeue, set run Running, invoke AgentOrchestrationService.ProcessTicketAsync(...), on each executor event subscribe to progress callbacks or poll wrapper, write WorkflowRunEvent row AND publish to SignalR group "run-{runId}", on terminal state set run Completed/Failed/Escalated.
- **Prerequisite:** Bishop owns adding progress callbacks to AgentOrchestrationService (IProgress<AgentExecutorEvent> or IAsyncEnumerable<AgentExecutorEvent> overload); Python workflow already streams executor events; .NET orchestrator must mirror that surface for UI.

**Blazor Pages (Ferro to spec):**
- `/tickets` — list with filters (assignee, status, category, priority, free-text q), sort Created/Modified asc/desc, paged.
- `/tickets/{number}` — full detail + comments timeline + "Add comment" form + **"Resolve with AI"** action button.
- `/tickets/{number}/runs/{runId}` — live workflow progression view (DevUI-style executor lane + streamed events).
- Colorful UI via single design-token CSS file + status/priority chips; no heavy component library required.

**Azure Deployment (Hicks checklist):**
- Repopulate `azure.yaml`, `infra/main.bicep`, `infra/resources.bicep` (currently empty).
- New module `infra/modules/appservice-web.bicep` for App Service plan + site (webSocketsEnabled: true, MI, app settings).
- Fill `infra/modules/containerapp-api.bicep` (0 bytes) with .NET API container, env vars (ConnectionStrings, CORS, Webhook flags).
- Create `infra/modules/containerappenvironment.bicep` (0 bytes).
- CORS: API allows App Service default hostname + custom domain; Program.cs reads Cors:AllowedOrigins (good, no code change).
- **SignalR sticky-session note:** Blazor Server circuit lives in Web App, hub runs inside Api Container App. Multiple Api replicas need Redis backplane **OR** pin `minReplicas==maxReplicas==1` for Phase 2.5 (decision: pin to 1 replica; document it).
- Aspire AppHost (0 bytes) — flesh out as local dev orchestrator (Web + Api + SQL + DevUI sidecar) or delete (decision: keep and flesh out).

**Risks & Sequencing:**
- **Risk:** SignalR scale-out (pinned to 1 replica); agent orchestrator progress surface (Bishop must add streaming hooks or UI degrades); DevUI vs .NET orchestrator drift; CORS/mixed content (Web on App Service HTTPS, Api on Container App HTTPS); comment authorship (free-text, no auth); webhook removal regression in tests.
- **Sequencing (parallel):** Step 1 — Hicks (Contracts + migrations + endpoints + infra re-baseline), Bishop (progress surface), Ferro (spec + wireframes). Step 2 (after contracts merge) — Ferro (Web project + pages), Hicks (ResolutionRunner + SignalR + SSE), Vasquez (tests). Step 3 — Hicks (webhook flag, delete RunAgentAsync), Vasquez (E2E test), Apone (review gate). Step 4 — Hicks (azd up + smoke test).

**Handoffs:**
- **Ferro:** Build AgenticResolution.Web (Interactive Server); pages /tickets, /tickets/{number}, /tickets/{number}/runs/{runId}; run view consumes SignalR hub (falls back to polling); single app.css with status/priority chips; typed ITicketsApiClient from AgenticResolution.Contracts.
- **Hicks:** Create AgenticResolution.Contracts; implement endpoint changes (sort whitelist); EF entities + migration; remove auto-enqueue webhook behind config flag (default false); implement IResolutionRunner hosted service + SignalR hub + SSE; re-baseline azure.yaml, infra/main.bicep, modules, pin Api to 1 replica with documented reason, flesh out AppHost.
- **Bishop:** Expose progress events from AgentOrchestrationService (IAsyncEnumerable<AgentExecutorEvent> preferred); mirror Python workflow structure (executor IDs); no new agents; confirm orchestrator can be invoked by runId and report transitions.
- **Vasquez:** Update tests assuming webhook fires on create/update (pass with flag off, pass with on); new tests — filter/sort matrix, comment add/list, resolve flow, run events SSE/SignalR smoke test.

**Open questions (non-blocking; Apone decides if no answer in 24h):**
1. **Auth on /resolve** — leave open for demo, or require static header secret? Default: leave open in dev, require header in deployed env.
2. **Comment authorship** — free-text now, real identity later? Default: yes.
3. **Run retention** — keep forever or trim after N per ticket? Default: keep all in Phase 2.5; reassess at first cost review.

---

### 2026-05-06T193815: User Directive — Instant Navigation with Loading Indicators

**By:** Jason Farrell (via Copilot)  
**Status:** Implemented

**What:** Page changes in the Blazor UI should happen instantly. If a target page needs to load data, the route should render immediately and show a loading indicator while data is fetched instead of delaying navigation.

**Why:** Improve perceived performance and avoid page-to-page UI delays.

---

### 2026-05-06: Bishop — Resolution API Streaming Fix & Terminal SSE Contract

**By:** Bishop (AI/Agents Specialist)  
**Status:** Implemented and deployed

## Context

After ca-resolution deployment, clicking Resolve showed the quick-start/loading experience, then hung or crashed instead of completing. Azure verification showed the Python API failed with:

```text
'Workflow' object has no attribute 'run_stream'
```

## Decision

The Python API will use the Agent Framework-supported streaming API:

```python
workflow.run(ticket_input, stream=True)
```

It will translate Agent Framework executor events into the frontend stage event contract and always emit a deterministic terminal SSE event before ending the response.

## Terminal SSE Contract

`POST /resolve` must always end the SSE stream with exactly one terminal workflow event before closing.

All SSE messages use:

```json
{
  "stage": "workflow",
  "status": "resolved",
  "event": "resolved",
  "terminal": true,
  "timestamp": "2026-05-06T23:43:08.706817Z",
  "message": "Ticket resolution completed.",
  "result": {}
}
```

Terminal statuses:
- `resolved` — automation applied a resolution to the ticket
- `escalated` — automation routed the ticket to a human assignee
- `completed` — workflow finished cleanly but did not apply resolution or escalation
- `failed` — workflow raised an exception, emitted a framework failure event, or exceeded the per-run timeout

## Reliability Guardrails

- Workflow errors are caught and converted to terminal `failed` SSE events
- Runtime is bounded by `RESOLUTION_RUN_TIMEOUT_SECONDS` (default 240 seconds)
- Timeout failures emit terminal `failed` SSE events
- Successful resolution emits terminal `resolved`
- Successful escalation emits terminal `escalated`
- Clean workflow completion without an action emits terminal `completed`

## Deployment

Deployed to `ca-resolution-tocqjp4pnegfo` as:

```text
acragressrcdevtocqjp4pnegfo.azurecr.io/resolution-api:terminal-events-20260506193957
```

Verified `/health` and `/resolve` against ticket `INC0010102`; the stream ended with terminal `resolved`, and the ticket moved to `Resolved`.

---

### 2026-05-06: Ferro — Instant Navigation Polish

**By:** Ferro (Frontend Developer)  
**Requested by:** Jason Farrell  
**Status:** Implemented and deployed

## Decision

Data-fetching Blazor routes should render route chrome immediately, then start API/SSE work in the background and show an explicit loading state until data or events arrive.

## Applied Pattern

- **Ticket detail:** Background load with cancellation on route changes/dispose, skeleton while loading, inline Retry / Back to Tickets on failure
- **Resolution streaming route:** Starts SSE work after first route render, shows an "opening stream" loading card before first event, keeps terminal detection sticky, navigates back to ticket detail after terminal completion
- **Ticket list:** Initial load follows the same non-blocking route render pattern with existing skeleton rows and retry on failure

## UX Note

Status and Assigned To belong in compact header summary pills on ticket detail. They should not also be repeated in the body summary unless a future layout explicitly requires it.

---

### 2026-05-06: Ferro — Blazor Resolution Stream Hardening

**By:** Ferro (Frontend Developer)  
**Status:** Implemented and deployed

## Decision

The Blazor resolution page treats the Python Resolution API as the only streaming contract and does not depend on SignalR or deleted .NET workflow endpoints.

## UI Contract Expectations

- `POST /resolve` returns `text/event-stream`
- SSE events may use standard `data:` lines with or without a space after the colon; blank lines delimit events
- SSE `event:` names are treated as status/event hints when the JSON payload omits an explicit terminal field
- Event JSON may include `stage`, `status`, `state`, `event`, `timestamp`, `result`, `message`, and/or `error`
- Terminal values include `completed`, `complete`, `done`, `resolved`, `success`, `succeeded`, `escalated`, `failed`, `failure`, `error`, `finished`, `finish`, or a JSON `terminal: true` flag

## UI Outcomes

- Starts streaming without blocking initial render
- Displays parse/API/stream errors inline
- Navigates back to ticket details after terminal stream completion
- Shows inline error if stream ends without terminal state

---

**Earlier decisions archived to `decisions-archive-2026-04-29.md`** (Phase 1 scope, resources, test infrastructure, etc.)
