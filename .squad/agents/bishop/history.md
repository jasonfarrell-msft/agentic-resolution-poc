# Project Context

- **Owner:** Jason Farrell
- **Project:** agentic-resolution — Phase 2+ work focuses on Azure AI Search and Foundry Agents (with Agent Framework) consuming the webhook fired on ticket save, then either auto-resolving or escalating/assigning the ticket.
- **Stack:** Azure AI Search, Azure AI Foundry Agents, Microsoft Agent Framework, .NET
- **Phase 1 scope:** standby — no AI work until ticket→DB→webhook path is in place.
- **Created:** 2026-04-29

## Core Context

**Current state (2026-05-05):**
- Phase 2 AI pipeline architecture finalized: specialized decomposers (IncidentDecomposer + RequestDecomposer) for type-specific KB retrieval
- Question-driven resolution pipeline: Classifier → Incident/RequestAgent (fetch) → DecomposerAgent (KB search) → EvaluatorAgent → Resolution/Escalation
- Hosted agents in Container Apps with Foundry `/invocations` protocol; MCP server for ticket operations
- Azure AI Search index `tickets-index` ready (14 fields, hybrid BM25+vector+semantic, text-embedding-3-small 1536d)
- Manual resolution workflow progress instrumented: AgentOrchestrationService emits executor events (Started/Routed/Output/Completed/Error) to WorkflowRunEvent table
- ResolutionRunnerService processes manual resolution runs via queue; no automatic agent triggering from webhooks
- Workflow executor sequence documented for Ferro's UI: ClassifierExecutor → IncidentFetchExecutor → IncidentDecomposerExecutor → EvaluatorExecutor → ResolutionExecutor/EscalationExecutor

**Current state (2026-05-06):**
- ✅ Deployed ca-resolution-tocqjp4pnegfo to Azure Container Apps (managed identity, external ingress port 8000)
- ✅ Deleted ca-agres-tocqjp4pnegfo (unused stub)
- ✅ Health endpoint verified: `GET /health` → `{"status":"healthy"}`
- ✅ Environment variables configured: AZURE_AI_ENDPOINT, MCP_SERVER_URL, TICKETS_API_URL
- Blazor UI ready to call `POST /resolve` on Python API for SSE-streamed resolution workflow

**Key locked decisions:**
- Hybrid search: BM25 + vector similarity + semantic reranking; top 5 results to triage agent
- Single index, no multi-index KB corpus (Phase 3+)
- Incident vs Request dichotomy preserved through decomposition
- Pre-fetch search results before agent eval (no tool-calling latency)
- Seed 25 pre-resolved IT scenarios at gate G7
- Manual resolution only: explicit POST /resolve triggers agent pipeline; webhook auto-dispatch default off

---

## Session Log

### 2026-05-05: Webhook/RunId Correlation & Async Resolution Flow Fix

**Requested by:** Jason Farrell  
**Scope:** Align workflow-progress design with clarified async contract: Blazor calls API → API fires webhook & returns → frontend listens for progress.

**Context:** User directive clarified that "Resolve should fire the webhook and return. Once returned successfully, the frontend should start listening for changes." Original implementation had critical bug: `StartResolveAsync` enqueued webhook but **never started agent orchestration** — ResolutionRunnerService was starved.

**Tasks:**
1. ✅ **Fixed orchestration trigger:** `POST /resolve` now enqueues to `IResolutionQueue` (not `IWebhookDispatcher`), actually starting agent work.
2. ✅ **Made webhooks opt-in:** New config flags `Webhook:FireOnResolutionStart` and `Webhook:FireOnWorkflowProgress` (default: false).
3. ✅ **Added workflow progress webhooks:** `workflow.running`, `workflow.completed`, `workflow.escalated`, `workflow.failed` events fired from ResolutionRunnerService at state transitions.
4. ✅ **Ensured RunId correlation:** All workflow webhooks carry `run_id` field; added `error_message` to `workflow.failed`.
5. ✅ **Updated webhook payload schema:** Extended `WebhookPayload` record with `ErrorMessage` property; added factory methods for new event types.
6. ✅ **Documented contract:** Created `.squad/decisions/inbox/bishop-webhook-run-correlation.md` with event naming, correlation strategy, config flags, and frontend/backend contracts.
7. ✅ **Verified build:** `dotnet build` succeeded with 1 unrelated package version warning.

**Critical fix detail:**
- **Before:** `dispatcher.Enqueue(WebhookPayload.ForResolutionStarted(ticket, run.Id))` — webhook fired, orchestration never started
- **After:** `resolutionQueue.Enqueue(new ResolutionRunRequest(run.Id, ticket.Number))` — orchestration starts; webhook optionally fires

**Behavior guarantees:**
- ✅ Async execution — API returns 202 immediately, orchestration runs in background
- ✅ RunId correlation — all workflow webhooks carry `run_id` for external system tracking
- ✅ Opt-in webhooks — default disabled, no surprise external traffic
- ✅ Frontend independence — UI polls `GET /api/runs/{runId}/events`, webhooks are parallel signal for external systems
- ✅ Idempotent re-trigger — duplicate POST /resolve returns existing runId

**Files modified:**
- `src/dotnet/AgenticResolution.Api/Api/TicketsEndpoints.cs` — StartResolveAsync enqueues to ResolutionQueue; webhook optional
- `src/dotnet/AgenticResolution.Api/Agents/ResolutionRunnerService.cs` — fires workflow progress webhooks at state transitions
- `src/dotnet/AgenticResolution.Api/Webhooks/WebhookDispatchService.cs` — added workflow event types, ErrorMessage field

**Files created:**
- `.squad/decisions/inbox/bishop-webhook-run-correlation.md` — comprehensive contract documentation

**Integration points:**
- **Ferro:** POST /resolve → poll GET /api/runs/{runId}/events for executor progression
- **Hicks:** (Optional) Replace polling with SignalR hub for live push updates (Phase 3)
- **Vasquez:** Test orchestration trigger, webhook flag behavior, runId correlation, idempotency, failure path
- **External systems:** Subscribe to webhook target, receive workflow.* events with run_id, query API for detailed progress

**Next:** Standby for integration test feedback. Ready to adjust webhook event schema if external system requirements emerge.

---

### 2026-05-05: Workflow Progress Instrumentation for Manual Resolution

**Requested by:** Jason Farrell  
**Scope:** Prepare agent workflow progress surface for manual Resolve UI (Phase 2.5 Blazor frontend)

**Context:** Apone's architecture decision established manual "Resolve with AI" flow with live workflow progression. WorkflowRun/WorkflowRunEvent models already in place (Hicks). Python DevUI already streams executor events. .NET orchestrator needed equivalent instrumentation.

**Tasks:**
1. ✅ Inspected existing agent orchestration — no automatic webhook triggering (already gated by `Webhook:AutoDispatchOnTicketWrite` flag, default false)
2. ✅ Created progress tracking infrastructure:
   - `IWorkflowProgressTracker` interface — contract for emitting executor events
   - `WorkflowProgressTracker` implementation — persists to `WorkflowRunEvent` table with monotonic sequence
3. ✅ Instrumented `AgentOrchestrationService.ProcessTicketAsync`:
   - Added optional `runId` and `progress` parameters (backward compatible)
   - Emits executor events: ClassifierExecutor → IncidentFetchExecutor → IncidentDecomposerExecutor → EvaluatorExecutor → ResolutionExecutor/EscalationExecutor
   - Event types: Started, Routed, Output, Error, Completed
4. ✅ Created resolution runner:
   - `IResolutionQueue` / `ResolutionQueue` — in-memory channel for manual resolution requests
   - `ResolutionRunnerService` (BackgroundService) — dequeues, invokes orchestrator with progress, updates run status
5. ✅ Integrated with `StartResolveAsync` endpoint — enqueues to resolution runner after creating Pending run
6. ✅ Registered services in `Program.cs` (scoped tracker, singleton queue, hosted service)
7. ✅ Documented workflow sequence for Ferro: `WORKFLOW_SEQUENCE_NAMES.md` with executor IDs, event types, UI guidance
8. ✅ Verified build succeeds (no compilation errors)

**Behavior guarantees:**
- ✅ Explicit execution only — no hidden agent runs, resolution ONLY via POST /resolve
- ✅ Progress visibility — every executor transition persisted to WorkflowRunEvent
- ✅ No silent failures — failed runs emit ExecutorError event and transition to Failed status
- ✅ Idempotent re-trigger — duplicate POST /resolve returns existing Pending/Running run

**Files created:**
- `src/dotnet/AgenticResolution.Api/Agents/IWorkflowProgressTracker.cs`
- `src/dotnet/AgenticResolution.Api/Agents/WorkflowProgressTracker.cs`
- `src/dotnet/AgenticResolution.Api/Agents/ResolutionRunnerService.cs`
- `src/dotnet/AgenticResolution.Api/Agents/WORKFLOW_SEQUENCE_NAMES.md`

**Files modified:**
- `src/dotnet/AgenticResolution.Api/Agents/AgentOrchestrationService.cs` — added progress tracking
- `src/dotnet/AgenticResolution.Api/Api/TicketsEndpoints.cs` — StartResolveAsync enqueues to resolution runner
- `src/dotnet/AgenticResolution.Api/Program.cs` — service registration

**Decision logged:** `.squad/decisions/inbox/bishop-workflow-events.md`

**Integration points:**
- **Ferro:** Poll `GET /api/runs/{runId}/events` for executor progression; display lanes per `WORKFLOW_SEQUENCE_NAMES.md`
- **Hicks:** (Optional) Replace `WorkflowProgressTracker` with SignalR-enabled version for live push updates

**Next:** Standby for Ferro's workflow UI integration. Ready to adjust executor sequence if Python workflow structure changes.

---

## Historical Summary

See `bishop-history-archive-2026-05-04.md` for detailed chronology (2026-04-29 through 2026-05-04):
- Phase 2 kickoff & search index schema finalization (2026-04-29)
- Phase 1 scaffold complete (2026-04-29)
- Foundry Agent wiring & hosted agents migration (2026-04-30)
- Question-driven resolution pipeline design (2026-05-04)
- DecomposerAgent split into IncidentDecomposer + RequestDecomposer (2026-05-04 decision)

---

## Learnings

### 2026-05-07: Final resolver status contract verified

**Verification target:** live `ca-resolution-tocqjp4pnegfo` and `ca-api-tocqjp4pnegfo` in `rg-agentic-res-src-dev`.

**Health evidence:** `GET /health` on the Resolution API returned healthy at `2026-05-07T02:36:49Z`.

**Live SSE evidence:** `POST /resolve` with `{"ticket_number":"INC0010102"}` returned HTTP 200 `text/event-stream` and streamed stage events through classifier, request fetch, request decomposer, evaluator, escalation, then terminal workflow event:

```json
{"stage":"workflow","status":"escalated","event":"escalated","terminal":true,"message":"Ticket was escalated to a human assignee."}
```

**Contract for Ferro:** The resolving UI must not treat ordinary stage `status: "completed"` events as workflow completion. It should wait for `terminal: true` and then read `status` or `event` on that terminal workflow event; observed terminal values are `resolved` and `escalated`, with `failed` for error paths.

**Persistence evidence:** After the terminal `escalated` event, `GET /api/tickets/INC0010102/details` showed the ticket updated through the MCP/.NET Tickets API with `state: "InProgress"`, `agentAction: "escalated_to_human"`, `assignedTo: "woo.jinchul@corp"`, confidence `0.2`, and escalation notes. The current .NET ticket enum does not include an `Escalated` state, so detail UI should display escalation from `agentAction == "escalated_to_human"` while resolved tickets use `state == "Resolved"` / `agentAction == "auto_resolved"`.

**Deployment:** No deploy was needed. Live final status emission and resolver-side persistence are working; the only mismatch is UI terminology versus the existing .NET `TicketState` model.

---

### 2026-05-07: Resolution API production contract verification

**Verification target:** `ca-resolution-tocqjp4pnegfo` in resource group `rg-agentic-res-src-dev`.

**Deployed URL:** `https://ca-resolution-tocqjp4pnegfo.graybush-af9ee262.eastus2.azurecontainerapps.io`

**Container App status:** Azure reported provisioning `Succeeded`, running status `Running`, latest revision `ca-resolution-tocqjp4pnegfo--0000003`, and 100% traffic to latest revision.

**Health:** `GET /health` returned HTTP 200 with JSON body `{"status":"healthy","timestamp":"2026-05-07T02:27:50.800515Z"}`.

**Resolve contract verified from deployed OpenAPI and live probes:**
- Method/path: `POST /resolve`
- Headers: `Content-Type: application/json`, `Accept: text/event-stream`
- Payload: `{"ticket_number":"INC0010102"}`; camelCase `ticketNumber` is rejected with HTTP 422 because `ticket_number` is required.
- Response: HTTP 200, `Content-Type: text/event-stream; charset=utf-8`, `Cache-Control: no-cache`, `X-Accel-Buffering: no`.
- `GET /resolve` returns HTTP 405; missing/incorrect content type returns HTTP 422; blank `ticket_number` returns HTTP 400.

**End-to-end smoke test:** Existing ticket `INC0010102` streamed classifier → incident_fetch → incident_decomposer → evaluator → resolution → workflow. Terminal event observed:

```json
{"stage":"workflow","status":"resolved","event":"resolved","terminal":true}
```

**UI guidance for Ferro:** The button should navigate to `/tickets/{Number}/resolve`, where the server-side Blazor page opens the SSE stream and renders progress until a terminal event, then redirects back to detail. There is no run ID and no .NET resolution proxy in this contract.

**Web app configuration check:** `app-agentic-resolution-web` has `ResolutionApi__BaseUrl` set to the deployed resolution API URL, plus `ApiClient__BaseUrl`/`TICKETS_API_URL` set to the tickets API.

---

### 2026-05-06: Resolution API deploy verification

**Verification target:** `ca-resolution-tocqjp4pnegfo`

**Deployed URL:** `https://ca-resolution-tocqjp4pnegfo.graybush-af9ee262.eastus2.azurecontainerapps.io`

**Image/revision verified:**
- Active image: `acragressrcdevtocqjp4pnegfo.azurecr.io/resolution-api:terminal-events-20260506193957`
- Active revision: `ca-resolution-tocqjp4pnegfo--0000003`
- Traffic: 100% to latest ready revision.
- Restarted the active revision after an aborted smoke client left the singleton workflow busy; no image rebuild was required.

**Health:** `GET /health` returned `healthy`.

**Smoke test:**
- Used existing safe demo ticket `INC0010102`.
- `POST /resolve` streamed progress through classifier → request fetch → request decomposer → evaluator → resolution.
- Final deterministic terminal SSE event observed: `{"stage":"workflow","status":"resolved","event":"resolved","terminal":true}`.
- Ticket remained `Resolved` with `agentAction=auto_resolved`; confidence updated to `0.80`.

**Operational note:** Read `/resolve` SSE streams until a terminal event before closing the client. Closing early can leave the singleton Agent Framework workflow busy until the container process is restarted.

---

### 2026-05-06: Resolution API terminal SSE fix

**Issue diagnosed:** Deployed `ca-resolution-tocqjp4pnegfo` was calling `workflow.run_stream(...)`, but Agent Framework `Workflow` exposes `run(..., stream=True)` instead. The stream emitted `classifier started`, then failed with `'Workflow' object has no attribute 'run_stream'`, causing the Resolve UI to finish without the expected terminal workflow event.

**Fix shipped:**
- Replaced `run_stream` usage with Agent Framework streaming events from `workflow.run(ticket_input, stream=True)`.
- Mapped executor lifecycle events to UI stages: classifier, incident/request fetch, incident/request decomposer, evaluator, resolution, escalation.
- Added deterministic terminal workflow statuses: `resolved`, `escalated`, `completed`, or `failed`.
- Wrapped workflow execution in a 240-second timeout via `RESOLUTION_RUN_TIMEOUT_SECONDS`.
- Converted workflow exceptions and timeout failures into SSE `failed` terminal events instead of crashing the HTTP stream.

**Deployment:**
- Built via ACR cloud build.
- Deployed image: `acragressrcdevtocqjp4pnegfo.azurecr.io/resolution-api:terminal-events-20260506193957`.
- Active revision verified: `ca-resolution-tocqjp4pnegfo--0000003`.

**Verification:**
- `GET /health` returned healthy.
- `POST /resolve` with `INC0010102` streamed through classifier → incident fetch → incident decomposer → evaluator → resolution.
- Final SSE terminal event: `{"stage":"workflow","status":"resolved","event":"resolved","terminal":true}`.
- Ticket `INC0010102` updated to `Resolved` with `agentAction=auto_resolved` and confidence `0.82`.

**Decision records:**
- `.squad/decisions/inbox/bishop-resolution-terminal-events.md`
- `.squad/decisions/inbox/bishop-resolution-hang-fix.md`

---

### 2026-05-06: ca-resolution Container App Deployment

**Deployed:** `ca-resolution-tocqjp4pnegfo` to Azure Container Apps (East US 2)

**FQDN:** `https://ca-resolution-tocqjp4pnegfo.graybush-af9ee262.eastus2.azurecontainerapps.io`

**Configuration:**
- Image: `acragressrcdevtocqjp4pnegfo.azurecr.io/resolution-api:latest`
- Ingress: external, port 8000
- Scale: 0–1 replicas (scale-to-zero enabled)
- Identity: System-assigned managed identity (principal: `8cedc719-fa1c-475c-bf82-f05c84ad1d99`)
- ACR pull: system identity
- RBAC: "Cognitive Services OpenAI User" on `oai-agentic-res-src-dev`

**Environment variables:**
- `AZURE_AI_ENDPOINT=https://oai-agentic-res-src-dev.cognitiveservices.azure.com/`
- `MCP_SERVER_URL=https://ca-mcp-tocqjp4pnegfo.graybush-af9ee262.eastus2.azurecontainerapps.io/mcp`
- `TICKETS_API_URL=https://ca-api-tocqjp4pnegfo.graybush-af9ee262.eastus2.azurecontainerapps.io`

**Deployment patterns learned:**
- ACR build (`az acr build`) avoids local Docker requirement; builds in cloud
- `--registry-identity system` enables system-assigned MI for ACR pull (no credentials)
- Pydantic version must be `>=2.11.0` due to `mcp` package dependency
- Cold start from scale-to-zero takes ~60s for Python containers
- Dockerfile build context = `src/python/`, COPY uses `.` not `src/python`
- Deleted unused `ca-agres-tocqjp4pnegfo` container app

---

### 2026-05-06: Python Resolution API — FastAPI wrapper for agent workflow

**Requested by:** Jason Farrell  
**Scope:** Create production-ready Python API service (`ca-resolution`) for Azure Container Apps deployment

**Context:** Architecture pivot establishes direct Blazor → Python Resolution API → MCP → TicketsNow data flow. The .NET API remains separate for ticket CRUD. Blazor calls Python Resolution API directly for workflow orchestration with SSE event streaming for demo visualization.

**Tasks completed:**
1. ✅ Created `src/python/resolution_api/` directory structure
2. ✅ Created `main.py` — FastAPI application with POST /resolve endpoint and SSE streaming
3. ✅ Created `requirements.txt` — FastAPI, uvicorn, pydantic, + agent-framework dependencies
4. ✅ Created `Dockerfile` — Multi-stage build for Azure Container Apps deployment
5. ✅ Created `README.md` — Complete API documentation with local dev and deployment guide
6. ✅ Updated `.env.template` with PORT configuration
7. ✅ Verified imports work correctly

**Implementation details:**

**API Endpoints:**
- `POST /resolve` — Accepts `{ "ticket_number": "INC0010101" }`, returns SSE stream with workflow events
- `GET /health` — Health check for Azure Container Apps probes
- `GET /` — Root health endpoint

**SSE Event Format:**
```json
{"stage": "classifier", "status": "started", "timestamp": "2026-05-06T12:34:56Z"}
{"stage": "classifier", "status": "completed", "result": {"type": "incident"}}
{"stage": "incident_fetch", "status": "started"}
{"stage": "incident_fetch", "status": "completed", "result": {...}}
{"stage": "incident_decomposer", "status": "started"}
{"stage": "incident_decomposer", "status": "completed", "result": {...}}

## 2026-05-06T194800 — Deployment Verification

**Trigger:** Production deployment validation of ca-resolution container app.

**Status:** ✅ Success

**Actions:**
- Verified `ca-resolution` running on `terminal-events` revision
- Confirmed `GET /health` → `{"status":"healthy"}`
- Smoke-tested `POST /resolve` with ticket INC0010102
- Validated SSE stream emitted progress and terminal resolved event
- Verified ticket persisted as Resolved with `agentAction=auto_resolved`

**Result:** Production Resolution API operational, streaming terminal events deterministically, and end-to-end workflow validated.
{"stage": "evaluator", "status": "started"}
{"stage": "evaluator", "status": "completed", "result": {"confidence": 0.85}}
{"stage": "resolution", "status": "started"}
{"stage": "resolution", "status": "completed", "result": {"output": "..."}}
```

**Workflow Stage Tracking:**
- Monitors `workflow.run_stream()` outputs by message type (`IncidentRoute`, `RequestRoute`, `TicketDetails`, `ResolutionAnalysis`, `ResolutionProposal`, final string output)
- Maps message transitions to SSE events (started/completed for each stage)
- Handles incident vs request branching (incident_fetch vs request_fetch, incident_decomposer vs request_decomposer)
- Routes to resolution or escalation based on confidence threshold (0.80)

**Design principles:**
1. **Thin wrapper** — NO duplication of agent logic; imports existing `workflow`, `agents`, `shared` modules
2. **SSE streaming** — Real-time event emission for Blazor UI visualization
3. **Stateless** — No WorkflowRun persistence; run tracking happens in .NET layer if needed
4. **Azure-ready** — Dockerfile with health checks, PORT env var support, DefaultAzureCredential
5. **Environment parity** — Uses same Azure OpenAI endpoint/model as devui_serve.py

**File paths:**
- `src/python/resolution_api/main.py` — FastAPI application
- `src/python/resolution_api/requirements.txt` — Python dependencies
- `src/python/resolution_api/Dockerfile` — Container build instructions
- `src/python/resolution_api/README.md` — Complete API documentation
- `src/python/.env.template` — Environment configuration template (updated with PORT)

**Integration surface:**
- **Blazor UI:** Calls POST /resolve, consumes SSE stream for progress visualization
- **MCP Server:** Agents invoke MCP tools for ticket CRUD operations
- **TicketsNow API:** MCP server proxies REST calls to .NET API
- **Azure OpenAI:** Agents use gpt-4o-mini via DefaultAzureCredential

**Deployment path:**
```bash
# Build
docker build -f src/python/resolution_api/Dockerfile -t acragressrcdevtocqjp4pnegfo.azurecr.io/resolution-api:latest .

# Push
az acr login --name acragressrcdevtocqjp4pnegfo
docker push acragressrcdevtocqjp4pnegfo.azurecr.io/resolution-api:latest

# Deploy to ca-resolution
az containerapp update \
  --name ca-resolution \
  --resource-group rg-agentic-res-src-dev \
  --image acragressrcdevtocqjp4pnegfo.azurecr.io/resolution-api:latest
```

**Known limitations:**
1. No request cancellation — workflow runs to completion even if client disconnects
2. No run persistence — events are streamed but not stored (unlike .NET WorkflowRun table)
3. Single-tenant — no per-request isolation (add scoping for production multi-tenancy)

**Verification:**
- ✅ Import test passed: `python -c "from resolution_api.main import app"`
- ✅ File structure matches Azure Container Apps requirements
- ✅ Dockerfile includes health check and proper CMD for uvicorn

**Next steps:**
- Deploy to Azure Container Apps as `ca-resolution`
- Configure Blazor UI to call Python API directly (remove .NET API dependency for resolution)
- Add MCP_SERVER_URL to container environment for agent tool access
- (Future) Add authentication (Azure AD), metrics (Prometheus), structured logging (App Insights)

**Decision logged:** `.squad/decisions/inbox/bishop-resolution-api.md`

---

## Historical Summary

See `bishop-history-archive-2026-05-04.md` for detailed chronology (2026-04-29 through 2026-05-04):
- Phase 2 kickoff & search index schema finalization (2026-04-29)
- Phase 1 scaffold complete (2026-04-29)
- Foundry Agent wiring & hosted agents migration (2026-04-30)
- Question-driven resolution pipeline design (2026-05-04)
- DecomposerAgent split into IncidentDecomposer + RequestDecomposer (2026-05-04 decision)

### 2026-05-07 — Final Resolver SSE Status Contract Verification
- **Outcome:** Verified SSE terminal status contract for Python resolver
- **Contract:** Terminal event uses stage workflow, terminal flag set to true, status/event values are "resolved", "escalated", or "failed"
- **Validation:** Tested INC0010102 terminal escalated case; pattern matches specification
- **Decision recorded:** `.squad/decisions.md` / "Bishop — Final Resolver Status Contract" (2026-05-07)
- **Status:** No deploy needed; contract ready for production
