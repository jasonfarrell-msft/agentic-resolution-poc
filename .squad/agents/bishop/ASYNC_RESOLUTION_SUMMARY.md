# Async Resolution Flow - Implementation Summary

**Date:** 2026-05-05  
**Agent:** Bishop (AI/Agents Specialist)  
**Requested by:** Jason Farrell

---

## The Problem

The original `POST /api/tickets/{number}/resolve` endpoint had a critical bug:
- It created a `WorkflowRun` record with `Pending` status
- It enqueued a webhook event (`resolution.started`)
- **But it never triggered the actual agent orchestration**

Result: The `ResolutionRunnerService` background service was never fed any work. Runs stayed in `Pending` status forever. The frontend would poll for progress but see no events.

---

## The Fix

### 1. Orchestration Trigger (Critical)

**Changed:** `StartResolveAsync` in `TicketsEndpoints.cs`

**Before:**
```csharp
dispatcher.Enqueue(WebhookPayload.ForResolutionStarted(ticket, run.Id));
```

**After:**
```csharp
resolutionQueue.Enqueue(new ResolutionRunRequest(run.Id, ticket.Number));
```

**Impact:** Agent orchestration now actually runs. `ResolutionRunnerService` dequeues the request, invokes `AgentOrchestrationService`, tracks progress, and updates run status.

---

### 2. Webhook Opt-In (New Behavior)

Webhooks are now **opt-in via configuration flags**:

```json
{
  "Webhook": {
    "TargetUrl": "https://external-system.example.com/webhooks",
    "Secret": "...",
    "FireOnResolutionStart": false,
    "FireOnWorkflowProgress": false
  }
}
```

**Default:** Both flags `false`. No webhooks fire unless explicitly enabled.

**Rationale:** Phase 2.5 is demo-focused; external webhook targets not required. Frontend uses direct API polling.

---

### 3. New Webhook Events (for External Systems)

Added four new workflow progress events that carry `run_id` for correlation:

| Event Type           | Fires When                      | Includes RunId | Includes ErrorMessage |
|----------------------|---------------------------------|----------------|-----------------------|
| `workflow.running`   | Run transitions Pending→Running | ✅             | ❌                    |
| `workflow.completed` | Run completes successfully      | ✅             | ❌                    |
| `workflow.escalated` | Run escalates (low confidence)  | ✅             | ❌                    |
| `workflow.failed`    | Orchestration throws exception  | ✅             | ✅                    |

**Implementation:** `ResolutionRunnerService.ProcessRunAsync` now checks `Webhook:FireOnWorkflowProgress` and fires these events at state transitions.

---

## Frontend Contract (Ferro)

**No changes required** if existing polling logic already in place. Confirm this flow:

1. User clicks "Resolve with AI"
2. `POST /api/tickets/{number}/resolve`
3. API returns `202 Accepted` with `{ runId, statusUrl, eventsUrl }`
4. Navigate to `/tickets/{number}/runs/{runId}` or embed progress widget
5. Poll `GET /api/runs/{runId}/events` every 2-3 seconds
6. Display executor lanes: `ClassifierExecutor` → `IncidentFetchExecutor` → `IncidentDecomposerExecutor` → `EvaluatorExecutor` → `ResolutionExecutor`/`EscalationExecutor`
7. Stop polling when `run.status ∈ {Completed, Escalated, Failed}`

**Key point:** Frontend does **not** consume webhooks. Webhooks are for external systems (e.g., ServiceNow integration).

---

## Backend Contract (Hicks)

### Current State (Phase 2.5)
- Polling: Frontend polls `GET /api/runs/{runId}/events`
- Webhooks: Opt-in for external systems via config flags
- No SignalR hub yet

### Future Enhancement (Phase 3)
- SignalR hub at `/hubs/runs`
- Clients join group `run-{runId}`
- `IWorkflowProgressTracker` implementation broadcasts to SignalR + persists to DB
- Blazor Server page receives live updates via `@rendermode InteractiveServer`

**No changes needed now** unless SignalR prioritized in this phase.

---

## Testing Contract (Vasquez)

### Critical Test Cases

1. **Orchestration trigger**
   - POST /resolve → verify `ResolutionRunRequest` enqueued to `IResolutionQueue`
   - Verify `WorkflowRun` transitions `Pending` → `Running` → `Completed`
   - Verify `WorkflowRunEvents` populated with executor sequence

2. **Webhook flags**
   - `FireOnResolutionStart=false` → no `resolution.started` webhook
   - `FireOnResolutionStart=true` → `resolution.started` webhook with `run_id`
   - `FireOnWorkflowProgress=false` → no progress webhooks
   - `FireOnWorkflowProgress=true` → `workflow.*` webhooks with `run_id`

3. **RunId correlation**
   - Parse webhook JSON → verify `run_id` field matches `WorkflowRun.Id`
   - Verify `workflow.failed` includes `error_message`

4. **Idempotency**
   - POST /resolve twice → second returns HTTP 200 with same `runId`
   - Verify single `WorkflowRun` created
   - Verify single orchestration invocation

5. **Failure path**
   - Mock orchestrator exception → verify `WorkflowRun.Status=Failed`
   - Verify `ExecutorError` event recorded
   - Verify `workflow.failed` webhook fires (if enabled)

---

## Configuration Reference

### Recommended Defaults (Phase 2.5)

```json
{
  "Webhook": {
    "TargetUrl": null,
    "Secret": null,
    "AutoDispatchOnTicketWrite": false,
    "FireOnResolutionStart": false,
    "FireOnWorkflowProgress": false
  }
}
```

**Result:** No webhooks fire. Agent orchestration runs normally. Frontend polls API.

### External System Integration

```json
{
  "Webhook": {
    "TargetUrl": "https://servicenow.example.com/api/webhooks/resolution",
    "Secret": "shared-secret-key",
    "AutoDispatchOnTicketWrite": false,
    "FireOnResolutionStart": true,
    "FireOnWorkflowProgress": true
  }
}
```

**Result:** Webhooks fire for resolution lifecycle. External system receives events with `run_id` for correlation.

---

## Files Modified

1. **src/dotnet/AgenticResolution.Api/Api/TicketsEndpoints.cs**  
   - `StartResolveAsync`: Enqueues to `IResolutionQueue` instead of `IWebhookDispatcher`
   - Webhook firing made optional via `Webhook:FireOnResolutionStart` flag

2. **src/dotnet/AgenticResolution.Api/Agents/ResolutionRunnerService.cs**  
   - `ProcessRunAsync`: Fires workflow progress webhooks at state transitions
   - Checks `Webhook:FireOnWorkflowProgress` flag before each webhook

3. **src/dotnet/AgenticResolution.Api/Webhooks/WebhookDispatchService.cs**  
   - Added `ErrorMessage` property to `WebhookPayload` record
   - Added factory methods: `ForWorkflowRunning`, `ForWorkflowCompleted`, `ForWorkflowEscalated`, `ForWorkflowFailed`

---

## Documentation

- **Full contract:** `.squad/decisions/inbox/bishop-webhook-run-correlation.md`
- **Workflow events:** `.squad/decisions/inbox/bishop-workflow-events.md`
- **Executor sequence:** `src/dotnet/AgenticResolution.Api/Agents/WORKFLOW_SEQUENCE_NAMES.md`

---

## Build Status

✅ **Build succeeded** (`dotnet build AgenticResolution.Api.csproj`)  
⚠️ 1 warning: `Azure.AI.Agents.Persistent` version mismatch (unrelated to this change)

---

## Summary for Jason

**Critical fix:** `POST /resolve` now actually starts agent orchestration. It was broken — enqueued webhook but never triggered work.

**New behavior:**
- API returns 202 immediately
- Background service runs agent pipeline asynchronously
- Frontend polls for progress (no webhook dependency)
- Webhooks are opt-in for external system integration

**Team impact:**
- **Ferro:** No changes if polling already implemented; confirm flow above
- **Hicks:** No changes unless SignalR prioritized
- **Vasquez:** Test orchestration trigger, webhook flags, runId correlation

**Configuration:**
- Default: All webhooks disabled
- Production: Enable `FireOnWorkflowProgress` if external system integration needed

**Ready for:** Integration testing, frontend workflow UI, external system integration (optional).
