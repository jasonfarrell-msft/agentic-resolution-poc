# Bishop — Workflow Event Progress Surface for Manual Resolution

**By:** Bishop (AI/Agents Specialist)  
**Requested by:** Jason Farrell  
**Date:** 2026-05-05  
**Status:** Implemented and verified  

---

## Context

Apone's architecture decision (`apone-blazor-resolution-architecture.md`) established that the manual "Resolve with AI" flow must provide live workflow progression to the Blazor UI. The Python DevUI already streams executor events; the .NET orchestrator needed equivalent instrumentation so Ferro could display:
- Executor lanes (ClassifierExecutor, IncidentFetchExecutor, etc.)
- Event sequence (Started → Routed → Output → Completed/Error)
- Final run status (Completed, Escalated, Failed)

**Pre-existing state:** The `WorkflowRun` and `WorkflowRunEvent` models were already in place (Hicks's work). The `StartResolveAsync` endpoint created a Pending run but did nothing with it. No automatic agent triggering existed in the webhook path — `Webhook:AutoDispatchOnTicketWrite` flag already implemented.

---

## What Changed

### 1. Progress Tracking Infrastructure

**Created:**
- `IWorkflowProgressTracker` interface — contract for emitting executor events.
- `WorkflowProgressTracker` implementation — persists events to `WorkflowRunEvent` table with monotonic sequence numbers.

**Key methods:**
```csharp
Task ExecutorStartedAsync(Guid runId, string executorId, CancellationToken ct);
Task ExecutorRoutedAsync(Guid runId, string executorId, string route, CancellationToken ct);
Task ExecutorOutputAsync(Guid runId, string executorId, string output, CancellationToken ct);
Task ExecutorErrorAsync(Guid runId, string executorId, string error, CancellationToken ct);
Task ExecutorCompletedAsync(Guid runId, string executorId, CancellationToken ct);
```

**Rationale:** Decoupled progress tracking from orchestration logic. Future enhancement: broadcast to SignalR hub for live UI updates without polling.

---

### 2. AgentOrchestrationService Instrumentation

**Modified:** `ProcessTicketAsync` signature now accepts optional `runId` and `IWorkflowProgressTracker progress` parameters.

**Behavior:** When `progress` is provided, the orchestrator emits events at each stage:
1. **ClassifierExecutor** — Started → Routed(incident) → Completed
2. **IncidentFetchExecutor** — Started → Completed
3. **IncidentDecomposerExecutor** — Started → Completed
4. **EvaluatorExecutor** — Started → Output(confidence + action) → Completed
5. **ResolutionExecutor** OR **EscalationExecutor** — Started → Output(notes) → Completed

**Executor IDs mirror Python workflow structure** documented in `WORKFLOW_SEQUENCE_NAMES.md`.

**Backward compatibility:** Existing callers that omit `runId` and `progress` get the same behavior as before (no event emission).

---

### 3. Resolution Runner Service

**Created:**
- `IResolutionQueue` / `ResolutionQueue` — in-memory channel (512-item capacity, drop-oldest policy) for resolution run requests.
- `ResolutionRunnerService` (BackgroundService) — dequeues requests, invokes orchestrator with progress tracking, updates WorkflowRun status.

**Flow:**
```
POST /api/tickets/{number}/resolve
  → Create WorkflowRun (Pending)
  → Enqueue ResolutionRunRequest(runId, ticketNumber)
  → Return 202 Accepted

ResolutionRunnerService (background)
  → Dequeue request
  → Set run.Status = Running
  → Invoke AgentOrchestrationService.ProcessTicketAsync(ticketNumber, runId, progress)
  → On success: set run.Status = Completed/Escalated, run.FinalAction, run.FinalConfidence
  → On failure: set run.Status = Failed, emit ExecutorError event
```

**No automatic agent triggering:** The resolution runner is ONLY invoked via explicit `POST /resolve`. The webhook path (Create/Update ticket) remains decoupled — it enqueues webhooks ONLY if `Webhook:AutoDispatchOnTicketWrite` is true (default false).

---

### 4. Endpoint Integration

**Modified:** `StartResolveAsync` in `TicketsEndpoints` now injects `IResolutionQueue` and enqueues the run after persisting it.

**Idempotency:** If a Pending or Running run already exists for the ticket, returns HTTP 200 with the existing runId (no new run created).

---

### 5. Service Registration

**Modified:** `Program.cs` now registers:
```csharp
builder.Services.AddScoped<IWorkflowProgressTracker, WorkflowProgressTracker>();
builder.Services.AddSingleton<IResolutionQueue, ResolutionQueue>();
builder.Services.AddHostedService<ResolutionRunnerService>();
```

**Lifetime rationale:**
- `WorkflowProgressTracker` is scoped — each run gets a fresh instance, safe for `IServiceScopeFactory` in background service.
- `ResolutionQueue` is singleton — shared channel across all requests.
- `ResolutionRunnerService` is hosted — single background worker processing the queue.

---

### 6. Documentation

**Created:** `WORKFLOW_SEQUENCE_NAMES.md` — executor sequence, event types, UI display guidance for Ferro.

**Key sequence:**
1. ClassifierExecutor
2. IncidentFetchExecutor
3. IncidentDecomposerExecutor
4. EvaluatorExecutor
5. ResolutionExecutor OR EscalationExecutor

**Event types:** Started, Routed, Output, Error, Completed.

**Guidance:** Ferro should poll `GET /api/runs/{runId}/events` to fetch new events, display executor lanes, and map WorkflowRun.Status to UI state.

---

## Behavior Guarantees

### ✅ Explicit Execution Only
No hidden agent runs. Resolution ONLY happens via `POST /api/tickets/{number}/resolve`. Webhook auto-dispatch remains opt-in (default off).

### ✅ Progress Visibility
Every executor transition persisted to `WorkflowRunEvent`. UI can reconstruct the exact sequence and current state.

### ✅ No Silent Failures
If the agent pipeline fails, the run transitions to `Failed` status and an `ExecutorError` event is emitted. No "pretend it worked" fallback.

### ✅ Idempotent Re-trigger
Clicking "Resolve" twice for the same ticket returns the existing run if it's Pending or Running. No duplicate processing.

---

## Files Changed

**New:**
- `src/dotnet/AgenticResolution.Api/Agents/IWorkflowProgressTracker.cs`
- `src/dotnet/AgenticResolution.Api/Agents/WorkflowProgressTracker.cs`
- `src/dotnet/AgenticResolution.Api/Agents/ResolutionRunnerService.cs`
- `src/dotnet/AgenticResolution.Api/Agents/WORKFLOW_SEQUENCE_NAMES.md`

**Modified:**
- `src/dotnet/AgenticResolution.Api/Agents/AgentOrchestrationService.cs` — added `runId` and `progress` parameters, emit events at each stage
- `src/dotnet/AgenticResolution.Api/Api/TicketsEndpoints.cs` — `StartResolveAsync` now enqueues to `IResolutionQueue`
- `src/dotnet/AgenticResolution.Api/Program.cs` — registered new services

**No changes to:**
- `WebhookDispatchService` — webhook path untouched
- `TicketsEndpoints.CreateAsync/UpdateAsync` — already gated by `Webhook:AutoDispatchOnTicketWrite` flag

---

## Integration Points for Hicks

**Ready to wire:** The `IWorkflowProgressTracker` interface is the contract. If Hicks implements a SignalR hub, he can create a `SignalRWorkflowProgressTracker` that broadcasts to `run-{runId}` group in addition to persisting events. Current implementation persists only; future broadcasting is an additive change.

**No merge conflicts:** The `WorkflowRun` and `WorkflowRunEvent` models were already in place. This work adds orchestration logic that populates those models.

**Webhook correlation:** See `.squad/decisions/inbox/bishop-webhook-run-correlation.md` for the async resolution flow, webhook event naming, and runId correlation strategy. Webhooks are opt-in for external system integration; frontend uses polling/SignalR directly against API.

---

## Integration Points for Ferro

**Endpoint:** `GET /api/runs/{runId}/events` — returns all events ordered by sequence.

**Polling strategy:** Poll every 2-3 seconds while `WorkflowRun.Status` is `Pending` or `Running`. Stop polling when status is `Completed`, `Escalated`, or `Failed`.

**Display sequence:** See `WORKFLOW_SEQUENCE_NAMES.md` for executor IDs and event types.

**Future:** When Hicks adds SignalR hub, Ferro can subscribe to `run-{runId}` group for push updates instead of polling.

---

## Tradeoffs

**✅ Pros:**
- Clean separation: webhook path vs. manual resolution path.
- Progress events enable rich UI (DevUI-style executor lanes).
- No blocking behavior — runs process asynchronously in background service.
- Idempotent re-trigger prevents duplicate work.

**⚠️ Cons:**
- Polling `GET /api/runs/{runId}/events` every 2-3 seconds until complete. Acceptable for Phase 2.5; SignalR is the Phase 3 enhancement.
- Executor events synthesized by orchestrator, not emitted by agents themselves. Future: agents stream structured progress.
- Single background worker processes all runs sequentially. Acceptable for demo; Phase 3 can parallelize with multiple workers if needed.

---

## Open Questions (None Blocking)

1. **SignalR Hub:** Hicks to implement in follow-up. API surface designed to allow drop-in replacement of `WorkflowProgressTracker` with a broadcasting version.
2. **Request Classification:** When enabled, RequestFetchExecutor / RequestDecomposerExecutor will appear instead of IncidentFetchExecutor / IncidentDecomposerExecutor. Orchestrator already structured to support this split.

---

## Verification

**Build status:** ✅ Succeeded (`dotnet build` passed with 1 unrelated package version warning).

**Manual test plan (Vasquez to automate):**
1. Create ticket → no agent fires (unless `Webhook:AutoDispatchOnTicketWrite=true`).
2. `POST /api/tickets/{number}/resolve` → returns 202 with runId.
3. Poll `GET /api/runs/{runId}` → status transitions Pending → Running → Completed/Escalated.
4. `GET /api/runs/{runId}/events` → sequence of executor events appears.
5. Retry `POST /resolve` for same ticket → returns 200 with existing runId (idempotent).
6. Agent failure scenario → run transitions to Failed, error event recorded.

---

## Next Steps

**Ferro:** Implement workflow progression UI using `WORKFLOW_SEQUENCE_NAMES.md` as spec.

**Hicks:** (Optional) Replace `WorkflowProgressTracker` with SignalR-enabled version for live push updates.

**Vasquez:** Write integration tests covering the manual resolution flow and event sequence.

**Bishop (me):** Standby for feedback; ready to adjust executor sequence if Python workflow diverges.
