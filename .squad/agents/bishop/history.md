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
