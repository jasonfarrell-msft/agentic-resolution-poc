# Bishop — Webhook/RunId Correlation Contract & Async Workflow Pattern

**By:** Bishop (AI/Agents Specialist)  
**Requested by:** Jason Farrell  
**Date:** 2026-05-05  
**Status:** Implemented  
**Related:** `apone-blazor-resolution-architecture.md`, `bishop-workflow-events.md`, `copilot-directive-2026-05-05T134319-resolve-webhook.md`

---

## Context

Jason clarified the contract for manual resolution workflow:
> "Resolve should fire the webhook and return. Once returned successfully, the frontend should start listening for changes so the user can track the resolution."

The original implementation had a critical bug: `POST /api/tickets/{number}/resolve` enqueued a webhook but **never started the agent orchestration**. The `ResolutionRunnerService` was starved of work.

---

## Fixed Architecture

### Resolve Endpoint Contract

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

**Critical fix:** Changed from `dispatcher.Enqueue(WebhookPayload.ForResolutionStarted(...))` to:
```csharp
resolutionQueue.Enqueue(new ResolutionRunRequest(run.Id, ticket.Number));
```

**Result:** API returns immediately (202 Accepted); background orchestration starts asynchronously; frontend polls `GET /api/runs/{runId}/events` for progress.

---

## Webhook Event Naming & RunId Correlation

All webhook payloads now carry `RunId` for correlation:

### Webhook Event Types

| Event Type           | Fires When                               | Config Flag                           | RunId Present |
|----------------------|------------------------------------------|---------------------------------------|---------------|
| `ticket.created`     | Ticket created (POST /tickets)           | `Webhook:AutoDispatchOnTicketWrite`   | ❌            |
| `ticket.updated`     | Ticket updated (PUT /tickets/{id})       | `Webhook:AutoDispatchOnTicketWrite`   | ❌            |
| `resolution.started` | Resolve endpoint called (optional)       | `Webhook:FireOnResolutionStart`       | ✅ RunId      |
| `workflow.running`   | WorkflowRun transitions Pending→Running  | `Webhook:FireOnWorkflowProgress`      | ✅ RunId      |
| `workflow.completed` | WorkflowRun completes (Completed status) | `Webhook:FireOnWorkflowProgress`      | ✅ RunId      |
| `workflow.escalated` | WorkflowRun completes (Escalated status) | `Webhook:FireOnWorkflowProgress`      | ✅ RunId      |
| `workflow.failed`    | WorkflowRun fails (Failed status)        | `Webhook:FireOnWorkflowProgress`      | ✅ RunId + ErrorMessage |

### WebhookPayload Schema

```json
{
  "event_id": "guid",
  "event_type": "workflow.completed",
  "timestamp": "2026-05-05T13:43:19Z",
  "ticket": {
    "number": "INC0010001",
    "short_description": "...",
    "category": "...",
    "priority": "2",
    "urgency": "2",
    "impact": "2",
    "state": "New",
    "caller": "...",
    "assignment_group": null,
    "opened_at": "2026-05-05T13:00:00Z"
  },
  "run_id": "00000000-0000-0000-0000-000000000000",
  "error_message": null  // only present in workflow.failed
}
```

**Correlation strategy:** External systems consuming webhooks can:
1. Receive `resolution.started` with `run_id`
2. Store `run_id` → `ticket.number` mapping
3. Listen for subsequent `workflow.*` events matching that `run_id`
4. Query `GET /api/runs/{run_id}/events` for detailed executor progress

---

## Configuration Flags

All webhook firing is now opt-in via `appsettings.json`:

```json
{
  "Webhook": {
    "TargetUrl": "https://external-system.example.com/webhooks",
    "Secret": "...",
    "AutoDispatchOnTicketWrite": false,       // ticket.created, ticket.updated
    "FireOnResolutionStart": false,           // resolution.started
    "FireOnWorkflowProgress": false           // workflow.running/completed/escalated/failed
  }
}
```

**Default behavior:** All webhooks disabled. Enables phased rollout and testing without external system dependencies.

---

## ResolutionRunnerService Changes

**Before:** Only invoked orchestrator, no webhook integration.  
**After:** Fires progress webhooks at workflow state transitions (if enabled).

**New behavior:**
1. **On Pending→Running transition:** Fire `workflow.running` event
2. **On completion (Completed/Escalated status):** Fire `workflow.completed` or `workflow.escalated`
3. **On orchestration failure:** Fire `workflow.failed` with error message

**Implementation:** Injected `IWebhookDispatcher` and `IConfiguration` into `ProcessRunAsync` scope; checked `Webhook:FireOnWorkflowProgress` flag before each enqueue.

---

## Frontend Contract (for Ferro & Hicks)

### Blazor Resolve Flow

```
User clicks "Resolve with AI" on ticket detail page
  ↓
POST /api/tickets/{number}/resolve
  ↓
API returns 202 with runId
  ↓
Navigate to /tickets/{number}/runs/{runId} (or embed progress on detail page)
  ↓
Start polling GET /api/runs/{runId}/events (every 2-3 seconds)
  ↓
Display executor lanes: ClassifierExecutor → IncidentFetchExecutor → ...
  ↓
Stop polling when run.status ∈ {Completed, Escalated, Failed}
```

**No webhook involvement in frontend.** Webhooks are for external system integration (e.g., ServiceNow sync). Frontend uses polling (Phase 2.5) or SignalR (Phase 3).

---

## Files Changed

### Modified
- `src/dotnet/AgenticResolution.Api/Api/TicketsEndpoints.cs`  
  **Change:** `StartResolveAsync` now enqueues to `IResolutionQueue` (not `IWebhookDispatcher`); webhook firing made optional via config flag.

- `src/dotnet/AgenticResolution.Api/Agents/ResolutionRunnerService.cs`  
  **Change:** `ProcessRunAsync` now fires workflow progress webhooks (running/completed/escalated/failed) if `Webhook:FireOnWorkflowProgress=true`.

- `src/dotnet/AgenticResolution.Api/Webhooks/WebhookDispatchService.cs`  
  **Change:** Added `ErrorMessage` property to `WebhookPayload` record; added factory methods `ForWorkflowRunning`, `ForWorkflowCompleted`, `ForWorkflowEscalated`, `ForWorkflowFailed`.

### Unchanged (verified)
- `AgentOrchestrationService` — progress tracking already implemented
- `WorkflowProgressTracker` — event persistence already implemented
- `WORKFLOW_SEQUENCE_NAMES.md` — executor sequence documentation already accurate

---

## Behavior Guarantees

### ✅ Asynchronous Execution
`POST /resolve` returns 202 immediately. Agent orchestration runs in background. No blocking HTTP calls.

### ✅ Webhook RunId Correlation
All workflow webhooks carry `run_id`. External systems can track resolution lifecycle by correlating events on this GUID.

### ✅ Opt-In Webhooks
All webhook firing controlled by config flags. Default: all disabled. No surprise external traffic.

### ✅ No Frontend Dependency on Webhooks
Frontend polls `GET /api/runs/{runId}/events` directly. Webhooks are parallel signal for external systems, not required for UI.

### ✅ Idempotent Re-Trigger
Duplicate `POST /resolve` returns HTTP 200 with existing runId if Pending/Running run exists. No duplicate orchestration.

---

## Testing Contract for Vasquez

### Test Scenarios

1. **Manual resolution triggers orchestration:**
   - POST /resolve → verify ResolutionRunRequest enqueued
   - Verify WorkflowRun transitions Pending → Running → Completed
   - Verify WorkflowRunEvents populated

2. **Webhook firing respects config flags:**
   - `FireOnResolutionStart=false` → no `resolution.started` event
   - `FireOnResolutionStart=true` → `resolution.started` with runId
   - `FireOnWorkflowProgress=false` → no progress events
   - `FireOnWorkflowProgress=true` → `workflow.running/completed/escalated/failed` with runId

3. **RunId correlation in webhook payload:**
   - Parse webhook JSON → verify `run_id` field present and matches WorkflowRun.Id
   - Verify `workflow.failed` includes `error_message`

4. **Idempotency:**
   - POST /resolve twice → second call returns HTTP 200 with same runId
   - Verify single WorkflowRun created, single orchestration invocation

5. **Failure path:**
   - Force orchestrator exception → verify WorkflowRun.Status=Failed
   - Verify ExecutorError event recorded
   - Verify `workflow.failed` webhook fires (if enabled)

---

## Integration Points for Hicks

### SignalR Hub (Phase 3 Enhancement)

Current: Frontend polls `GET /api/runs/{runId}/events`.  
Future: SignalR hub `/hubs/runs`, group `run-{runId}`, broadcasts:
- `ExecutorStarted(executorId)`
- `ExecutorOutput(executorId, payload)`
- `ExecutorCompleted(executorId)`
- `ExecutorError(executorId, error)`
- `RunStatusChanged(status)`

**Implementation path:** Create `IWorkflowProgressBroadcaster : IWorkflowProgressTracker` that wraps persistence + SignalR. Blazor Server page joins group on mount, receives live updates via `@rendermode InteractiveServer`.

---

## Open Questions (None Blocking)

1. **Webhook retry on 5xx?** Current: 3 attempts with exponential backoff (1s, 4s, 16s). Acceptable for Phase 2.5.
2. **Webhook DLQ?** Current: log error and drop after retry exhaustion. Phase 3: persist failed webhooks to retry table.
3. **RunId in ticket.updated webhook?** Current: no. Future: if ticket update triggered by agent resolution, include runId for correlation.

---

## Decision Rationale

**Why enqueue to ResolutionQueue instead of webhook dispatcher?**
- ResolutionRunnerService is the actual agent orchestrator.
- Webhooks are for external notification, not orchestration trigger.
- Separation of concerns: orchestration queue vs. notification queue.

**Why make webhooks opt-in?**
- Phase 2.5 is demo-focused; external systems not required.
- Allows testing without webhook target.
- Reduces noise during development.

**Why include RunId in all workflow webhooks?**
- External systems need correlation key for workflow lifecycle tracking.
- Frontend doesn't need webhooks (polls API directly).
- RunId is stable, unique, and already persisted in WorkflowRun table.

---

## Summary for Jason

**What changed:**
1. ✅ `POST /resolve` now starts agent orchestration (was broken, enqueued webhook but didn't start work)
2. ✅ Webhook firing is opt-in via config flags (default: all disabled)
3. ✅ All workflow webhooks carry `run_id` for correlation
4. ✅ New webhook events: `workflow.running`, `workflow.completed`, `workflow.escalated`, `workflow.failed`
5. ✅ Build succeeds (verified with `dotnet build`)

**Frontend contract:**
- POST /resolve → 202 with runId
- Poll GET /api/runs/{runId}/events
- No webhook dependency

**External system contract:**
- Subscribe to webhook target URL
- Receive `workflow.*` events with `run_id`
- Query API for detailed progress via `GET /api/runs/{run_id}/events`

**Configuration surface:**
- `Webhook:FireOnResolutionStart` (default: false)
- `Webhook:FireOnWorkflowProgress` (default: false)

**Ready for:** Ferro (UI polling), Vasquez (integration tests), Hicks (SignalR enhancement in Phase 3).
