# Hicks — Corrected Backend Contract: Manual Resolve Webhook Flow

**By:** Hicks (Backend Dev)  
**Date:** 2026-05-05  
**Status:** Implemented  
**Context:** User clarification on Phase 2.5 manual resolve flow — resolve endpoint should fire webhook to Azure Function receiver, not process in-process.

---

## Problem

The initial implementation of `POST /api/tickets/{number}/resolve` created a WorkflowRun and enqueued to an internal `ResolutionQueue` processed by `ResolutionRunnerService`. This conflated the API with the resolution orchestrator.

**Phase 2 architecture** specifies: API sends webhooks → Azure Function receives → Function processes resolution with agent pipeline. The resolve endpoint was incorrectly processing in-process instead of delegating via webhook.

---

## Solution

Corrected the resolve endpoint to follow the Phase 2 webhook-driven architecture:

### 1. Webhook Payload Extension

Added `resolution.started` event type to `WebhookPayload`:

```csharp
public record WebhookPayload(
    Guid EventId, 
    string EventType, 
    DateTime Timestamp, 
    TicketWebhookSnapshot Ticket, 
    Guid? RunId = null)

public static WebhookPayload ForResolutionStarted(Ticket t, Guid runId) =>
    new(Guid.NewGuid(), "resolution.started", DateTime.UtcNow, 
        TicketWebhookSnapshot.From(t), runId);
```

**Payload shape** (JSON snake_case):
```json
{
  "event_id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "event_type": "resolution.started",
  "timestamp": "2026-05-05T18:30:00Z",
  "ticket": {
    "number": "INC0010042",
    "short_description": "Outlook not syncing",
    "category": "Email",
    "priority": "2",
    "urgency": "2",
    "impact": "2",
    "state": "New",
    "caller": "jane.doe",
    "assignment_group": null,
    "opened_at": "2026-05-05T18:25:00Z"
  },
  "run_id": "8c7a3d91-4e2f-4d89-9b1a-5f0c8e9d7a2b"
}
```

Headers sent by `WebhookDispatchService`:
- `X-Resolution-Signature: sha256=<hmac_hex>` (HMAC-SHA256 of body with `Webhook:Secret`)
- `X-Resolution-Event-Id: <event_id>` (for deduplication)
- `X-Resolution-Event-Type: resolution.started`

### 2. Resolve Endpoint Behavior

**Endpoint:** `POST /api/tickets/{number}/resolve`

**Request body (optional):**
```json
{ "note": "User requested manual resolution" }
```

**Flow:**
1. Look up ticket by number
2. Check for existing Pending/Running WorkflowRun — if found, return HTTP 200 with existing run (idempotent, no duplicate webhook)
3. Create new WorkflowRun with status Pending, TriggeredBy = "manual", optional note
4. **Enqueue webhook unconditionally** via `IWebhookDispatcher` with `ForResolutionStarted(ticket, runId)`
5. Return HTTP 202 Accepted immediately

**Response (HTTP 202 Accepted):**
```json
{
  "runId": "8c7a3d91-4e2f-4d89-9b1a-5f0c8e9d7a2b",
  "ticketNumber": "INC0010042",
  "ticketId": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
  "statusUrl": "/api/runs/8c7a3d91-4e2f-4d89-9b1a-5f0c8e9d7a2b",
  "eventsUrl": "/api/runs/8c7a3d91-4e2f-4d89-9b1a-5f0c8e9d7a2b/events"
}
```

**Response (HTTP 200 OK — idempotent case):**
Same shape as 202, but indicates existing run was returned instead of creating a new one.

**Response (HTTP 404 Not Found):**
Ticket number does not exist.

### 3. Webhook Dispatch to Azure Function

The `WebhookDispatchService` BackgroundService:
1. Reads from the webhook queue
2. Serializes payload to JSON (snake_case)
3. Computes HMAC-SHA256 signature
4. POSTs to `Webhook:TargetUrl` (configured as the Azure Function endpoint)
5. Retries 3 times with backoff (1s, 4s, 16s) on failure
6. Logs success or permanent failure

**Configuration:**
- `Webhook:TargetUrl` — Azure Function HTTPS endpoint (e.g., `https://func-agentic-res-dev.azurewebsites.net/api/ResolutionWebhook`)
- `Webhook:Secret` — Shared HMAC secret (from Key Vault in deployed env)

### 4. Azure Function Receiver Responsibilities

The Azure Function (not implemented in this API project) is responsible for:
1. Validating HMAC signature
2. Deduplicating via `event_id`
3. Looking up the WorkflowRun by `run_id`
4. Setting run status to Running
5. Invoking the agent orchestration pipeline (IncidentDecomposer / RequestDecomposer / Evaluator)
6. Writing WorkflowRunEvent rows to track progress
7. Updating WorkflowRun to Completed/Escalated/Failed with FinalAction and FinalConfidence
8. Optionally calling back to `PUT /api/tickets/{id}` to update ticket state

### 5. Frontend Listen Flow

**Ferro's responsibilities:**
1. POST /api/tickets/{number}/resolve → receives 202 with runId, ticketNumber, ticketId, statusUrl, eventsUrl
2. Start polling `GET /api/runs/{runId}/events` (or subscribe to SignalR hub when available)
3. Render live progress as WorkflowRunEvent rows arrive (executor lanes: ClassifierExecutor, IncidentDecomposerExecutor, EvaluatorExecutor, etc.)
4. Stop listening when run reaches terminal state (Completed/Escalated/Failed)

**Event polling endpoint:**
- `GET /api/runs/{runId}/events` → `WorkflowRunEventResponse[]` ordered by Sequence asc
- Repeated calls return cumulative events; frontend diffs to show new ones
- Future: SignalR hub `/hubs/runs` group `run-{runId}` for push-based updates

---

## Verification of Non-Regression

### Create/Update Webhook Behavior

**Config flag:** `Webhook:AutoDispatchOnTicketWrite` (default `false` in appsettings.json)

**When false (default):**
- `POST /api/tickets` → saves ticket, does NOT enqueue webhook
- `PUT /api/tickets/{id}` → updates ticket, does NOT enqueue webhook
- Manual resolve is the ONLY path that fires webhooks

**When true (opt-in):**
- `POST /api/tickets` → saves ticket, enqueues `ticket.created` webhook
- `PUT /api/tickets/{id}` → updates ticket, enqueues `ticket.updated` webhook
- Useful for external ServiceNow integration in future phases

**Rationale:** Phase 2.5 requirement — resolution is manual and explicit. Auto-dispatch was a Phase 1 demo convenience that conflated ticket write with agent invocation. Flag preserved for future integration scenarios without breaking existing behavior.

---

## Event Types Reference

| Event Type          | Trigger                                  | Payload Includes | Processing |
| ------------------- | ---------------------------------------- | ---------------- | ---------- |
| `ticket.created`    | POST /api/tickets (if flag true)         | Ticket snapshot  | Azure Function indexes to AI Search |
| `ticket.updated`    | PUT /api/tickets/{id} (if flag true)     | Ticket snapshot  | Azure Function updates AI Search index |
| `resolution.started` | POST /api/tickets/{number}/resolve       | Ticket snapshot + runId | Azure Function invokes agent pipeline |

---

## Response Contract Summary

**StartResolveResponse:**
- `runId` (Guid) — WorkflowRun identifier for polling
- `ticketNumber` (string) — Ticket number (e.g., "INC0010042")
- `ticketId` (Guid) — Ticket GUID for database lookups
- `statusUrl` (string) — Relative path to GET run detail
- `eventsUrl` (string) — Relative path to GET/poll run events

**Why include ticket info?** Ferro can display ticket context in the run detail page without a separate API call to fetch the ticket.

---

## Backward Compatibility

**No breaking changes:**
- Existing `GET /api/tickets/*` endpoints unchanged
- Existing `POST /api/tickets` and `PUT /api/tickets/{id}` unchanged
- Webhook behavior changed only if `AutoDispatchOnTicketWrite` is explicitly set to true
- MCP server and other API consumers unaffected

---

## Known Gaps / Follow-Up

1. **Azure Function not yet implemented** — The webhook receiver and agent pipeline orchestration live in the Azure Function project (separate repo or folder). API side is complete; Function side is Bishop's territory.
2. **SignalR hub not yet implemented** — Frontend must poll `/api/runs/{runId}/events`. Future: SignalR hub for push-based updates.
3. **ResolutionRunnerService unused** — The in-process runner (`ResolutionRunnerService`, `IResolutionQueue`) is no longer invoked by the resolve endpoint. Removed from endpoint signature. Can be deleted or repurposed for local dev/testing scenarios.

---

## Rationale

**Why delegate to Azure Function via webhook instead of processing in-process?**

1. **Phase 2 architecture decision** — Agent pipeline runs in the Function, not the API. Separation of concerns: API owns persistence, Function owns orchestration.
2. **Scalability** — Function can scale independently on Consumption plan; API remains lightweight.
3. **Retry semantics** — Webhook dispatch has built-in retry with backoff; Function failures can be retried by Azure Functions host.
4. **Integration future** — Same webhook plumbing supports external ServiceNow integration later.

**Why fire webhook immediately instead of queuing for batch processing?**

Phase 2.5 is a manual resolution demo. User clicks "Resolve with AI" button → expect near-immediate response. Queueing would add latency. If scale becomes an issue, the Function can add internal queuing (e.g., Azure Queue Storage).

---

## Tradeoffs

**Pros:**
- Aligns with Phase 2 architecture (webhook-driven)
- Clean separation: API persists, Function orchestrates
- Idempotent resolve (safe to retry on network failure)
- Response includes ticket context for frontend convenience
- Preserves backward compatibility (create/update non-webhook by default)

**Cons:**
- Resolution flow now depends on external Azure Function availability
- Webhook dispatch can fail (mitigated by retry logic + logging)
- Local dev requires either (a) deployed Function or (b) webhook stub receiver
- In-process runner (`ResolutionRunnerService`) is now orphaned code

---

## Validation

- Build succeeded: 0 errors
- Webhook payload extended with `run_id` field (optional, only present for resolution.started)
- Resolve endpoint fires webhook **unconditionally** for new runs (no config flag gating)
- Existing active run returns 200 without firing duplicate webhook (idempotent)
- Response shape includes ticket number and ID
- Create/update still respect `Webhook:AutoDispatchOnTicketWrite` flag (default false)
- Appsettings.json explicitly sets flag to false
- `IResolutionQueue` removed from endpoint signature

---

**Contract now matches user directive:** Resolve fires webhook unconditionally for new runs and returns immediately with complete context for frontend to start listening for progress events. No config flag required.
