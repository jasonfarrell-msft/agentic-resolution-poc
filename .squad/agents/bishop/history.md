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

### 2026-05-12: MCP Server Route Conflict and Stale Session Fix

**Requested by:** Jason Farrell  
**Scope:** Fix two bugs causing MCP tool failures in the resolution workflow.

**Bug 1 — Duplicate GET / route (AmbiguousMatchException → HTTP 500):**
- Root cause: A previous session added `app.MapGet("/", ...)` as a SSE keepalive workaround, but `app.MapMcp()` already registers a GET / handler. This caused `AmbiguousMatchException` on all GET / requests → HTTP 500.
- Fix: Removed the `app.MapGet("/", ...)` block entirely from `Program.cs`. `app.MapMcp()` handles GET / correctly for the MCP protocol.

**Bug 2 — MCPStreamableHTTPTool singleton holds stale session ID:**
- Root cause: `create_mcp_tool()` in `mcp_tools.py` was a lazy singleton (cached in `_mcp_tool` global). Agent modules called it at module load time, embedding a stale session ID in each agent. When the MCP container restarted, all session IDs were invalidated, but the Python container kept the old tool → HTTP 404 on all tool calls.
- Fix:
  1. Removed `_mcp_tool` global from `mcp_tools.py` — `create_mcp_tool()` now returns a fresh instance every call.
  2. Converted all agent modules (classifier, incident, request, resolution, escalation, incident_decomposer, request_decomposer) from module-level `agent = Agent(...)` singletons to `create_agent()` factory functions.
  3. Updated `workflow/__init__.py` — each workflow function now calls `create_agent()` inside the function body, creating fresh agent+tool instances per workflow execution.

**Deployment:**
- MCP: `mcp-noroute-20260512131604` → `ca-mcp-agent-resolution-test4` (provisioningState: Succeeded)
- Python resolution: `res-20260512131712` → `ca-res-agent-resolution-test4` (provisioningState: Succeeded)

**Verification:**
- POST /resolve for INC0010019 streamed SSE events through all stages without HTTP 500.
- MCP server logs show no 500 errors (route conflict eliminated); 404s are MCP session protocol responses, not routing failures.

**Files modified:**
- `src/dotnet/TicketsApi.McpServer/Program.cs` — removed duplicate MapGet("/") block
- `src/python/shared/mcp_tools.py` — removed singleton caching from `create_mcp_tool()`
- `src/python/agents/classifier/__init__.py` — converted to `create_agent()` factory
- `src/python/agents/incident/__init__.py` — converted to `create_agent()` factory
- `src/python/agents/request/__init__.py` — converted to `create_agent()` factory
- `src/python/agents/resolution/__init__.py` — converted to `create_agent()` factory
- `src/python/agents/escalation/__init__.py` — converted to `create_agent()` factory
- `src/python/agents/incident_decomposer/__init__.py` — converted to `create_agent()` factory
- `src/python/agents/request_decomposer/__init__.py` — converted to `create_agent()` factory
- `src/python/workflow/__init__.py` — workflow functions call `create_agent()` per execution

**Decision documented:** `.squad/decisions/inbox/bishop-mcp-v2-fix.md`

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



### 2026-05-12: INC0010019 Failure Root Cause - MCP Tool Invocation 404 Errors

**Ticket:** INC0010019 ("OneDrive files show 'locked by another user' preventing edits")  
**Issue:** Ticket repeatedly fails to resolve despite previous fixes (workflow lock leak, MCP URL, OpenAI endpoint, KB search)

**Root cause identified:** The MCP server returns HTTP 404 when agents try to call tools via Model Context Protocol. This causes all ticket detail retrieval to fail.

**Evidence:**
1. MCP logs show `Setting HTTP status code 404` during resolution attempts (01:17:38, 01:22:55, 01:22:56, 01:22:58, 01:23:03 UTC)
2. Agents explicitly report: "encountered an error with the lookup function"
3. Ticket details returned are empty (description: "", category: "", priority: "") despite ticket existing in database
4. Direct API call to `/api/tickets/INC0010019` returns full ticket successfully
5. Resolution workflow executes but with no data, resulting in:
   - Classifier misclassifies as REQUEST instead of INCIDENT (can't retrieve ticket to classify)
   - Request fetch stage returns empty ticket details
   - Decomposer has no description to analyze (preliminary_confidence: 0.0)
   - Evaluator assigns 0.0 confidence
   - Escalation triggers with generic fallback assignment

**Why previous fixes didn't help:**
- ✅ Workflow lock leak fix - Solved timeout/abandonment, but retrieval was already broken
- ✅ MCP URL correction - Fixed URL format, but tool routing still broken
- ✅ Azure OpenAI endpoint - Fixed auth, but agents can't get data to process
- ✅ KB search multi-word query - KB search works, but agents never get ticket data

**Impact:** All tickets fail to resolve (not just 0019) because agents receive empty data from failed MCP tool calls.

**MCP server configuration verified:**
- Health endpoint works: `{"status":"Healthy","timestamp":"..."}`
- Tool definitions exist in TicketTools.cs: get_ticket_by_number, list_tickets, search_tickets, update_ticket
- Program.cs registers tools: `.WithTools<TicketTools>().WithTools<KnowledgeBaseTools>()`
- TICKETS_API_URL environment variable is set correctly

**Suspected causes:**
1. MCP library version mismatch between Agent Framework client and ModelContextProtocol.Server
2. Tool registration not completing during MCP server startup
3. MCP HTTP transport routing issue
4. Agent Framework's MCPStreamableHTTPTool sending requests in unexpected format

**Next steps:**
1. Add MCP server diagnostic logging to instrument tool registration and invocation
2. Test MCP tool endpoints manually with raw JSON-RPC requests
3. Compare MCP protocol versions between Agent Framework and MCP.Server library
4. Consider fallback: Replace MCP tool calls with direct HTTP calls to Tickets API

**Decision documented:** `.squad/decisions/inbox/bishop-0019-diagnosis.md`


### 2026-05-08: test2 Resolution API Azure OpenAI RBAC repair

**Issue diagnosed:** `ca-res-agent-resolution-test2` used user-assigned identity `id-resolution-agent-resolution-test2` (principal `c6b82506-1e92-49b1-8e4b-962defc93a9f`) and failed at classifier startup because the identity lacked the Azure OpenAI data-plane action `Microsoft.CognitiveServices/accounts/OpenAI/deployments/chat/completions/action`.

**Root cause:** The `test2` resource group had no Cognitive Services/Azure OpenAI account. The Resolution API container only had `AZURE_CLIENT_ID` configured, so `shared/client.py` fell back to `https://oai-agentic-res-src-dev.cognitiveservices.azure.com/` and `gpt-5.1-deployment`. That target account requires data-plane RBAC for each managed identity that calls chat completions.

**Least-privilege role:** `Cognitive Services OpenAI User` at the Azure OpenAI/AI Services account scope. This role includes the required chat completions data action without granting contributor-level management permissions.

**Action taken:** Confirmed no existing effective assignment on first inspection, then verified/created the role assignment for the Resolution API principal at `/subscriptions/bb4b2781-6739-4fa1-994e-4ad6ce55c59c/resourceGroups/rg-agentic-res-src-dev/providers/Microsoft.CognitiveServices/accounts/oai-agentic-res-src-dev`.

**Validation:** Initial retry showed the known Agent Framework singleton busy state, so the active `ca-res-agent-resolution-test2` revision was restarted. A retry immediately after assignment still returned PermissionDenied, consistent with Azure RBAC data-plane propagation delay. After a propagation wait and another revision restart, `POST /resolve` for `INC0010102` reached terminal `resolved`; the RBAC error was cleared.

**Operational note:** After assigning Azure OpenAI data-plane RBAC, allow several minutes for propagation and restart the Resolution API revision if it had already acquired a denied token or is stuck in singleton busy state.

**Decision documented:** `.squad/decisions/decisions.md` section "Azure OpenAI Data-Plane RBAC for Resolution API" consolidates findings and operating guidance. Hicks automated role assignment in `scripts\Setup-Solution.ps1` for future deployments.

---

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

---

### 2026-05-10: MSI Database User Configuration Script

**Requested by:** Jason Farrell  
**Scope:** Create reusable script for configuring Azure SQL database users for managed identities, integrate into Setup-Solution.ps1

**Context:** Setup script was using inline SQL with `az sql db query`, which is not available in all Azure CLI versions. Local testing showed the command was not recognized. A reusable, reliable approach was needed.

**Tasks completed:**
1. ✅ Created `scripts/Configure-DatabaseUsers.ps1` — Standalone script for MSI user configuration
2. ✅ Integrated into `Setup-Solution.ps1` — Replaced broken inline approach with script call
3. ✅ Validated against deployed Azure SQL database with both identities
4. ✅ Confirmed idempotency — Safe to run multiple times

**Implementation details:**

**Authentication approach:** Uses Azure CLI to acquire access token, then .NET SqlClient with `AccessToken` property
- **Why not sqlcmd -G:** ODBC driver had authentication issues; `-P` parameter has 128-char limit (tokens are ~2000 chars)
- **Why not az sql db query:** Command not available in all Azure CLI versions
- **Why .NET SqlClient:** Built into PowerShell, handles long tokens properly, reliable Entra auth

**SQL idempotency:** Script checks role membership before attempting `ALTER ROLE`, avoiding errors when users already exist with correct roles.

**Roles configured:**
- API identity: `db_owner` (required for EF migrations on startup)
- Web App identity: `db_datareader`, `db_datawriter`

**Live validation evidence:**
- Tested against `sql-agent-resolution-test.database.windows.net` / `agenticresolution` database
- First run created web app user (API user already existed from prior manual config)
- Second run confirmed idempotency: both users already had correct roles
- Both runs completed successfully with clear status messages

**Deployment pattern learned:** For Azure SQL Entra auth from PowerShell, `az account get-access-token` + .NET `SqlConnection.AccessToken` is the most reliable approach. Avoids sqlcmd/ODBC driver issues and Azure CLI command availability variations.
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

---

### 2026-05-07 — Test Environment Deployment Blocked by MCAPS Policy

**Requested by:** Jason Farrell  
**Target:** Deploy to `rg-agent-resolution-test` using `Setup-Solution.ps1`

**Status:** ❌ **BLOCKED** — Azure Policy violation

**Issue:** The Azure SQL Server deployment to `rg-agent-resolution-test` is blocked by MCAPS governance policy `AzureSQL_WithoutAzureADOnlyAuthentication_Deny`. The policy requires:
- `azureADOnlyAuthentication: true` on all SQL Servers
- Azure AD administrator configuration (no SQL authentication allowed)

**Current implementation:** `infra/modules/sqlserver.bicep` uses SQL authentication with `administratorLogin` and `administratorLoginPassword` parameters. This violates the MCAPS policy requirement for Azure AD-only authentication.

**Deployment outcome:**
- ✅ Resource group created: `rg-agent-resolution-test`
- ✅ Foundation resources deployed: Key Vault (`kv-agentresolutiontest`), App Service Plan (`plan-agent-resolution-test`), Web App (`app-agent-resolution-test-web`)
- ❌ SQL Server deployment failed with policy violation
- ❌ Container Apps and backend services not deployed (dependent on SQL)

**Policy details:**
- **Policy Name:** `AzureSQL_WithoutAzureADOnlyAuthentication_Deny`
- **Policy Set:** `MCAPSGovDenyPolicies`
- **Effect:** Deny
- **Scope:** Management Group `e90bd921-0e00-4e6f-b87c-713670ee27bf`
- **Required:** `Microsoft.Sql/servers/administrators.azureADOnlyAuthentication == true`
- **Override:** Tag `SecurityControl=Ignore` on resource (not recommended for production)

**Impact:** The single-command setup script (`Setup-Solution.ps1`) cannot deploy SQL Server in MCAPS-governed subscriptions without architectural changes to support Azure AD-only authentication.

**Workarounds considered:**
1. **Tag bypass:** Add `SecurityControl: Ignore` tag to SQL Server resource — **NOT RECOMMENDED** for security compliance
2. **Architecture change:** Migrate to Azure AD authentication — **REQUIRES** rework of connection strings, Entity Framework configuration, and setup script password flow
3. **Alternative subscription:** Deploy to non-MCAPS subscription for testing — requires access to different Azure tenant/subscription

**Recommended next step:** Escalate to Jason Farrell for decision:
- Accept the policy limitation and migrate to Azure AD-only authentication (significant rework)
- Request policy exemption for test environments (requires governance approval)
- Use alternative testing subscription without MCAPS policy enforcement

**Resources created (partial):**
- Resource Group: `rg-agent-resolution-test` (East US 2)
- Key Vault: `kv-agentresolutiontest`
- App Service Plan: `plan-agent-resolution-test` (B1 Linux)
- Web App: `app-agent-resolution-test-web`

**Resources NOT created:**
- SQL Server: `sql-agent-resolution-test` ❌ Policy blocked
- SQL Database: `agenticresolution` ❌ Dependent on SQL Server
- Container Apps Environment ❌ Dependent on foundation
- Azure Container Registry ❌ Dependent on foundation
- Container Apps (API, Resolution) ❌ Dependent on foundation

**Decision required:** Blocked on architectural decision for Azure AD authentication vs policy exemption vs alternative subscription.

## 2026-05-07 16:16 - Azure SQL Entra-Only Authentication Implementation

**Context:** Deployment to rg-agent-resolution-test was blocked by MCAPS policy `AzureSQL_WithoutAzureADOnlyAuthentication_Deny`. Jason directed that the solution should use Entra auth with managed identities, setting the current user as admin.

**Changes Implemented:**

1. **infra/modules/sqlserver.bicep**
   - Removed password-based authentication parameters (`administratorLogin`, `administratorLoginPassword`)
   - Added Entra admin parameters: `entraAdminLogin`, `entraAdminObjectId`, `entraAdminTenantId`
   - Configured `administrators.azureADOnlyAuthentication = true`
   - Set current user as ActiveDirectory administrator

2. **infra/resources.bicep**
   - Updated SQL server module call with Entra admin parameters
   - Changed connection string from User ID/Password to `Authentication=Active Directory Default`
   - Connection string stored in Key Vault now uses Entra auth exclusively

3. **infra/main.bicep**
   - Replaced `sqlAdminLogin` and `sqlAdminPassword` parameters with Entra admin parameters
   - Parameters flow from Setup-Solution.ps1 → main.bicep → resources.bicep → sqlserver.bicep

4. **scripts/Setup-Solution.ps1**
   - Removed `SqlAdminPassword` parameter and all password prompts
   - Added discovery of current Azure CLI user via `az ad signed-in-user show`
   - Set environment variables for azd: `ENTRA_ADMIN_LOGIN`, `ENTRA_ADMIN_OBJECT_ID`, `ENTRA_ADMIN_TENANT_ID`
   - Added database user creation step (2.8) after Container Apps deployment:
     * Creates SQL database user for API managed identity with `db_owner` role (required for EF migrations on startup)
     * Creates SQL database user for Web App managed identity with `db_datareader` + `db_datawriter` roles
     * Uses `az sql db query` with `--auth-mode ActiveDirectoryIntegrated` to execute SQL script
     * Graceful error handling with manual fallback instructions
   - Updated connection string construction to use Entra auth (removed password reference)
   - Updated script documentation and help text

5. **SETUP.md**
   - Removed SQL password prerequisite and examples
   - Added explanation of Entra-only authentication behavior
   - Documented current user becomes SQL admin automatically
   - Explained managed identity permissions model
   - Added security features section

6. **DEPLOY.md**
   - Updated "What Gets Created" table to note Entra-only authentication
   - Updated "Initial Setup Flow" to describe Entra admin discovery
   - Added SQL Authentication explanation section

**Key Security Improvements:**
- ✅ MCAPS policy compliant (`azureADOnlyAuthentication = true`)
- ✅ No SQL passwords in code, parameters, or Key Vault
- ✅ Managed identities for all application access
- ✅ Current Azure CLI user as SQL admin (enables setup and management)
- ✅ Least privilege roles where practical (with caveat: API needs db_owner for EF migrations)

**Setup Experience:**
- ✅ Maintained one-command setup (no additional steps required)
- ✅ No password prompt — discovers current user automatically
- ✅ Creates database users for managed identities as part of setup
- ✅ Graceful error handling if SQL script execution fails

**Known Considerations:**
- API identity gets `db_owner` role because the API runs EF migrations on startup
- In production, consider separating migration identity (db_owner) from runtime identity (read/write)
- Database user creation uses `az sql db query` which requires Azure CLI
- Fallback: If automated SQL execution fails, script saves SQL file and provides manual instructions

**Testing Status:**
- ⚠️ Not yet deployed to Azure (per instructions: "Do not deploy to Azure yet unless explicitly asked")
- Changes validated through code review
- Ready for deployment testing

**Blocked:** None. Implementation complete and ready for testing.


### 2026-05-08: Azure SQL Entra-only authentication fix for MCAPS policy compliance

**Issue:** Azure deployment to `rg-agent-resolution-test` failed on Microsoft.Sql/servers because MCAPS policy `AzureSQL_WithoutAzureADOnlyAuthentication_Deny` requires `properties.administrators.azureADOnlyAuthentication == true`.

**Root cause:**
1. `infra\main.json` was stale — contained SQL password parameters (`sqlAdminLogin`, `sqlAdminPassword`) instead of Entra admin parameters
2. `scripts\Setup-Solution.ps1` set environment variables but did not persist them to azd environment for deployment

**Fix:**
1. ✅ **Regenerated main.json from main.bicep** — Removed SQL password auth, added Entra admin parameters (`entraAdminLogin`, `entraAdminObjectId`, `entraAdminTenantId`)
2. ✅ **Updated Setup-Solution.ps1** — Added `azd env set` calls to persist Entra admin values before `azd up`
3. ✅ **Verified SQL configuration** — `azureADOnlyAuthentication: true` present in ARM template
4. ✅ **Validated with bicep build** — No compilation errors

**Behavior guarantees:**
- ✅ MCAPS policy compliant — Azure SQL uses Entra-only authentication
- ✅ No secrets in templates — No SQL passwords in Bicep or ARM templates
- ✅ Current user as admin — Signed-in Azure CLI user automatically becomes SQL Server Entra admin
- ✅ Persistent configuration — Entra admin values stored in azd environment survive across deployments

**Files modified:**
- `infra\main.json` — Regenerated from Bicep, now uses Entra admin parameters only
- `scripts\Setup-Solution.ps1` — Added `azd env set` calls for `entraAdminLogin`, `entraAdminObjectId`, `entraAdminTenantId`

**Unchanged (already correct):**
- `infra\main.bicep` — Already required Entra admin parameters
- `infra\resources.bicep` — Already passed Entra admin parameters to SQL module
- `infra\modules\sqlserver.bicep` — Already configured `azureADOnlyAuthentication: true`

**Next:** Test deployment to verify MCAPS policy no longer blocks Azure SQL creation.


### 2026-05-08: Deployment Readiness Audit for rg-agent-resolution-test

**Requested by:** Jason Farrell  
**Task:** Validate deployment readiness without actually deploying; identify blockers before re-running setup to rg-agent-resolution-test.

**Audit scope:**
- Infrastructure Bicep files (main.bicep, resources.bicep, sqlserver.bicep, keyvault.bicep)
- Deployment scripts (Setup-Solution.ps1, Configure-DatabaseUsers.ps1, Reset-Data.ps1)
- Documentation (SETUP.md, DEPLOY.md, scripts\README.md)
- Search for SQL password/auth remnants
- Validate Entra-only authentication configuration
- Check managed identity database user creation

**Findings:**

✅ **Infrastructure: 10/10 PASS**
- Bicep compiles without errors (`az bicep build` exit 0)
- `azureADOnlyAuthentication: true` confirmed in sqlserver.bicep line 23
- Entra admin params (`entraAdminLogin`, `entraAdminObjectId`, `entraAdminTenantId`) flow correctly through all Bicep files
- Connection string uses `Authentication=Active Directory Default` (no User ID/Password) in resources.bicep line 64
- No `administratorLogin` or `administratorLoginPassword` parameters in any Bicep files
- SQL connection string stored in Key Vault with Entra auth syntax

✅ **Scripts: 13/13 PASS**
- Setup-Solution.ps1 performs all required steps in order:
  1. Prerequisites validation (Azure CLI, azd, .NET SDK, auth)
  2. Discover current Azure user via `az ad signed-in-user show` (lines 142-165)
  3. Persist Entra admin params to azd environment (lines 192-210)
  4. Run `azd up` to provision foundation (SQL, Key Vault, App Service)
  5. Create Container Apps Environment and Azure Container Registry
  6. Build and push API images (`az acr build`)
  7. Create Container Apps with managed identities
  8. Grant roles (AcrPull, Key Vault Secrets User, Azure OpenAI User)
  9. **Configure database users for managed identities** via Configure-DatabaseUsers.ps1 (lines 632-653)
  10. Wait for API health check (lines 687-706)
  11. Reset data and seed sample tickets via Reset-Data.ps1
- Configure-DatabaseUsers.ps1 creates DB users via access token (no password), grants appropriate roles:
  - API identity: `db_owner` (required for EF migrations on startup)
  - Web App identity: `db_datareader`, `db_datawriter`
- Reset-Data.ps1 seeds sample tickets via admin API endpoint (15 tickets covering common IT scenarios)

✅ **Security & Compliance: 8/8 PASS**
- MCAPS policy `AzureSQL_WithoutAzureADOnlyAuthentication_Deny` compliant
- No SQL passwords in Bicep, scripts, or Key Vault
- Managed identities for all app access (API, Web App)
- Current signed-in Azure CLI user becomes SQL Entra admin automatically
- Secrets stored in Key Vault (connection string as secret)
- RBAC-based access control for Key Vault

🟡 **Documentation: BLOCKER — Stale SQL password references**

**Files containing obsolete SQL password content:**
1. `DEPLOY.md` lines 130-146: "SQL Server Password" section with `$env:SQL_ADMIN_PASSWORD` example
2. `SETUP.md` lines 79-83: "SQL Password Requirements" section (entire section obsolete)
3. `scripts\README.md` lines 25-37: `-SqlAdminPassword` parameter documentation (parameter removed)
4. `scripts\README.md` lines 141-156: Troubleshooting "SQL password does not meet requirements" (no longer applicable)

**Impact:** Users following current docs will:
- Waste time setting `SQL_ADMIN_PASSWORD` environment variable (script no longer reads it)
- Report "password not working" issues when authentication is automatic via Entra
- Attempt to troubleshoot password complexity when authentication mode has fundamentally changed

**Why it's a blocker:** Documentation inaccuracy creates confusion and support burden. Users may not trust that Entra auth is working and attempt workarounds.

**Verdict:** Infrastructure is deployment-ready; documentation must be updated before re-running setup to avoid user confusion.

**Recommended fix:**
- Remove stale SQL password sections from DEPLOY.md, SETUP.md, scripts\README.md
- Add prominent note about Entra-only authentication behavior
- Emphasize that current signed-in Azure CLI user becomes SQL admin automatically

**Technical debt note:** API identity gets `db_owner` role because it runs EF migrations on startup. In production, consider separating migration identity (db_owner, used once) from runtime identity (db_datareader + db_datawriter) and running migrations as part of deployment pipeline.

**Files reviewed:**
- ✅ infra\main.bicep
- ✅ infra\resources.bicep
- ✅ infra\modules\sqlserver.bicep
- ✅ infra\modules\keyvault.bicep
- ✅ scripts\Setup-Solution.ps1
- ✅ scripts\Configure-DatabaseUsers.ps1
- ✅ scripts\Reset-Data.ps1
- 🟡 SETUP.md (needs update)
- 🟡 DEPLOY.md (needs update)
- 🟡 scripts\README.md (needs update)

**Decision written to:** `.squad\decisions\inbox\bishop-deployment-readiness.md`

**Next:** Update documentation to remove SQL password references, then re-run deployment to rg-agent-resolution-test.


---

### 2026-05-12: Evaluator "No KB Documentation Found" Root Cause Fix

**Requested by:** Jason Farrell  
**Scope:** Diagnose and fix why the evaluator stage reports "No KB documentation found" for INC0010019 despite KB0001012 being a perfect match.

**Root Cause (two issues, both in decomposers):**

1. **Wrong tool name in prompts:** Both incident_decomposer and equest_decomposer system prompts instructed the agent to call search_knowledge_base — a tool that does not exist. The actual MCP tool is named search_kb. The agent could not find the tool, so KB searches silently failed and all answers were "No KB documentation found."

2. **Incomplete two-step KB retrieval:** search_kb returns only article titles, categories, and tags — NOT the body text. To read the full resolution steps, the agent must then call get_kb_article. Neither decomposer prompt mentioned this second call, so even if the tool name were correct, agents would have no substantive content to synthesize.

**KB search URL confirmation:**  
- /api/kb/search?q=... → 404 (route doesn't exist)  
- /api/kb?q=file → returns 5 articles including KB0001012 ✅  
- KB0001012 confirmed to contain full step-by-step resolution for "file locked by another user"

**Evaluator architecture (correct as-is):**  
The evaluator has no tools by design — it's a pure reasoning agent that receives pre-fetched KB data from the decomposer via ResolutionAnalysis. No change needed to the evaluator itself.

**Fixes applied:**

- src/python/agents/incident_decomposer/__init__.py — STEP 3 updated: search_knowledge_base → search_kb; added explicit instruction to call get_kb_article after each search hit; updated CRITICAL REMINDERS
- src/python/agents/request_decomposer/__init__.py — same changes for request fulfillment path

**Deployment:**  
- Image: cragentresolutiontest4.azurecr.io/res:res-eval-20260512132928  
- Container App: ca-res-agent-resolution-test4 — updated, provisioningState: Succeeded

**Commit:** 1eef739 — ix: wire search_kb tool into evaluator/decomposer to enable KB-based confidence scoring

**Expected outcome:** INC0010019 → decomposer calls search_kb("locked OneDrive") → finds KB0001012 → calls get_kb_article("KB0001012") → synthesizes resolution steps → evaluator scores ≥0.80 → auto-resolved.


---

### 2026-05-12: KB Search Query Length Fix

**Requested by:** Jason Farrell  
**Scope:** Fix incident_decomposer and equest_decomposer calling search_kb 4 times with 8+ word queries that return 0 results due to AND logic.

**Root Cause:**  
Both decomposer system prompts instructed the agent to include descriptive terms like "troubleshooting", "incident", "fix", "setup", "onboarding", "provision" in every search query. The KB API uses SQL LIKE with AND logic — all words must appear in the article. Queries like "Excel session stale troubleshooting recovery incident" or "OneDrive file lock release Microsoft 365 Teams SharePoint" never match any article.

**KB Endpoint Confirmed:**  
- /api/kb/search?q=... → 404 (does not exist)  
- /api/kb?q=file+locked+OneDrive → KB0001012 ✅  
- /api/kb?q=OneDrive+locked → KB0001012 ✅  
Short 2-4 keyword queries reliably surface the correct articles.

**Fixes Applied:**  
- src/python/agents/incident_decomposer/__init__.py — STEP 3 rewritten: removed suggestion to add "troubleshooting/incident/fix/error" to queries; added explicit AND-logic warning; added short query rule (2-4 keywords); added good/bad examples; added retry instruction on 0 results; updated example search_terms in JSON output.
- src/python/agents/request_decomposer/__init__.py — Same changes for fulfillment path: removed "how-to/setup/provision/request/onboarding" padding advice; same 2-4 keyword rule and examples.

**Deployment:**  
- Image: cragentresolutiontest4.azurecr.io/res:res-query-20260512134714  
- Container App: ca-res-agent-resolution-test4 — updated, provisioningState: Succeeded

**Commit:** 755bfe6 — fix: enforce short KB search queries in incident/request decomposer agents

**Expected Outcome:** INC0010019 → decomposer searches "file locked OneDrive" → finds KB0001012 → calls get_kb_article("KB0001012") → synthesizes resolution steps → evaluator scores ≥0.80 → auto-resolved.
