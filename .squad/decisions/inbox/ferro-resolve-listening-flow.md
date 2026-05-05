# Ferro — Decision: Resolve Button Flow & Progress Listening

**By:** Ferro (Frontend Developer)  
**Requested by:** Jason Farrell  
**Status:** Specified — awaiting AgenticResolution.Web implementation  
**Related:** `apone-blazor-resolution-architecture.md`, `copilot-directive-2026-05-05T134319-resolve-webhook.md`

---

## 1. Problem Statement

The user directive clarifies that clicking "Resolve with AI" must **not** block waiting for agent workflow completion. Instead:
1. The API should enqueue the resolution workflow and return immediately with a run identifier
2. The frontend should navigate to a progress view and start listening for workflow events
3. The user sees real-time progression through classifier → decomposer → evaluator → summarizer stages

Previously, the implicit design might have assumed a synchronous flow or polling after completion. This decision makes the async, event-driven contract explicit.

---

## 2. API Contract (Expected from Hicks)

### 2.1 Trigger Resolve

**Endpoint:** `POST /api/tickets/{number}/resolve`

**Request Body (Optional):**
```json
{
  "note": "Manual trigger by agent Ferro for demo"
}
```

**Response (HTTP 202 Accepted):**
```json
{
  "runId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "statusUrl": "/api/runs/3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "eventsUrl": "/api/runs/3fa85f64-5717-4562-b3fc-2c963f66afa6/events"
}
```

**Idempotency:** If a run for this ticket is already `Pending` or `Running`, return HTTP 200 with the existing run instead of creating a duplicate.

**What happens server-side:**
- Creates a `WorkflowRun` record with `Status=Pending`
- Enqueues a `ResolutionRunRequest` to the `IResolutionRunner` background service
- Webhook dispatch happens **after** this response is sent (async via Channel or similar)
- Returns immediately; does NOT wait for agent workflow to complete

---

### 2.2 Get Run Status

**Endpoint:** `GET /api/runs/{runId}`

**Response:**
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "ticketId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "ticketNumber": "INC0010042",
  "status": "Running",
  "triggeredBy": "user@example.com",
  "note": "Manual trigger by agent Ferro for demo",
  "startedAt": "2026-05-05T17:43:19Z",
  "completedAt": null,
  "finalAction": null,
  "finalConfidence": null,
  "events": [
    {
      "id": "...",
      "sequence": 1,
      "executorId": "ClassifierExecutor",
      "eventType": "Started",
      "payload": null,
      "timestamp": "2026-05-05T17:43:20Z"
    },
    {
      "id": "...",
      "sequence": 2,
      "executorId": "ClassifierExecutor",
      "eventType": "AgentResponse",
      "payload": "{\"classification\":\"Incident\",\"confidence\":0.95}",
      "timestamp": "2026-05-05T17:43:22Z"
    }
  ]
}
```

**Status enum:** `Pending` (0), `Running` (1), `Completed` (2), `Failed` (3), `Escalated` (4)

---

### 2.3 Listen for Events (Real-Time)

**Primary:** SignalR Hub at `/hubs/runs`

**Group:** `run-{runId}`

**Messages:** `ReceiveRunEvent(runId, event)`

**Fallback:** Server-Sent Events at `GET /api/runs/{runId}/events`

**Event Stream Format (SSE):**
```
event: run-event
data: {"id":"...","sequence":3,"executorId":"IncidentDecomposerExecutor","eventType":"Started","payload":null,"timestamp":"2026-05-05T17:43:23Z"}

event: run-event
data: {"id":"...","sequence":4,"executorId":"IncidentDecomposerExecutor","eventType":"Output","payload":"{\"questions\":[...]}","timestamp":"2026-05-05T17:43:25Z"}

event: completed
data: {"runId":"3fa85f64-5717-4562-b3fc-2c963f66afa6","finalStatus":"Completed"}
```

Stream closes when run reaches a terminal state (`Completed`, `Failed`, `Escalated`).

---

## 3. Frontend Client Behavior

### 3.1 Ticket Detail Page (`/tickets/{number}`)

**UI Element:** "Resolve with AI" button

**Click handler:**
```csharp
private async Task OnResolveClickedAsync()
{
    var response = await TicketsApiClient.ResolveAsync(TicketNumber);
    
    if (response.IsSuccess)
    {
        // Navigate to the run progress page immediately
        NavigationManager.NavigateTo($"/tickets/{TicketNumber}/runs/{response.Data.RunId}");
    }
    else
    {
        // Show error toast/banner
        ErrorMessage = response.Error ?? "Failed to start resolution workflow.";
    }
}
```

**No blocking:** The button click does **not** poll or wait. It fires the API call, gets a runId, and navigates.

---

### 3.2 Run Progress Page (`/tickets/{number}/runs/{runId}`)

**Route parameter:** `runId` (Guid)

**On page load:**
1. Fetch initial run snapshot via `GET /api/runs/{runId}`
2. Render current status and all events received so far
3. Connect to SignalR hub `/hubs/runs`, join group `run-{runId}`
4. Subscribe to `ReceiveRunEvent` method
5. On each incoming event, append to the events list and update UI
6. On terminal status (`Completed`, `Failed`, `Escalated`), disconnect from hub and show final state

**Fallback if SignalR unavailable:**
- Poll `GET /api/runs/{runId}` every 2 seconds
- Compare `events.Count` or `lastSequence` to detect new events
- Stop polling on terminal status

**UI Structure:**
```
┌─────────────────────────────────────────────────────────────┐
│  Ticket INC0010042 — Resolution Run                         │
│  Status: [Running] ●                                         │
├─────────────────────────────────────────────────────────────┤
│  Timeline:                                                   │
│                                                              │
│  ✓ ClassifierExecutor                    17:43:20           │
│    → Classification: Incident (95%)                          │
│                                                              │
│  ⏳ IncidentDecomposerExecutor            17:43:23           │
│    → Generating diagnostic questions...                      │
│                                                              │
│  ⏱ EvaluatorExecutor                      (pending)          │
│                                                              │
│  ⏱ ResolutionSummarizerExecutor           (pending)          │
└─────────────────────────────────────────────────────────────┘
```

**Visual states:**
- ✓ green checkmark = executor completed
- ⏳ spinner = executor running (last event from this executor was `Started`)
- ⏱ gray clock = executor not started yet
- ❌ red X = executor failed

**Payload rendering:**
- `EventType=Started` → show "Started" with timestamp
- `EventType=AgentResponse` → parse JSON payload, show key fields (classification, questions, KB article titles, etc.)
- `EventType=Output` → show output text
- `EventType=Error` → show error message in red
- `EventType=Completed` → show final action + confidence

---

### 3.3 DTOs (to be added to `AgenticResolution.Contracts`)

```csharp
namespace AgenticResolution.Contracts;

// Request
public record ResolveTicketRequest(string? Note = null);

// Response
public record ResolveTicketResponse(
    Guid RunId,
    string StatusUrl,
    string EventsUrl
);

// Run detail
public record WorkflowRunResponse(
    Guid Id,
    Guid TicketId,
    string TicketNumber,
    WorkflowRunStatus Status,
    string? TriggeredBy,
    string? Note,
    DateTime StartedAt,
    DateTime? CompletedAt,
    string? FinalAction,
    double? FinalConfidence,
    IReadOnlyList<WorkflowRunEventResponse> Events
);

public record WorkflowRunEventResponse(
    Guid Id,
    int Sequence,
    string? ExecutorId,
    string EventType,
    string? Payload,
    DateTime Timestamp
);

public enum WorkflowRunStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Escalated = 4
}
```

---

### 3.4 API Client Extensions

Add to `Services/TicketsApiClient.cs`:

```csharp
public async Task<ApiResult<ResolveTicketResponse>> ResolveAsync(string ticketNumber, string? note = null)
{
    var request = new ResolveTicketRequest(note);
    var response = await _httpClient.PostAsJsonAsync($"/api/tickets/{ticketNumber}/resolve", request);
    
    if (response.StatusCode == System.Net.HttpStatusCode.Accepted || response.IsSuccessStatusCode)
    {
        var data = await response.Content.ReadFromJsonAsync<ResolveTicketResponse>();
        return ApiResult<ResolveTicketResponse>.Success(data!);
    }
    
    var error = await response.Content.ReadAsStringAsync();
    return ApiResult<ResolveTicketResponse>.Failure(error);
}

public async Task<ApiResult<WorkflowRunResponse>> GetRunAsync(Guid runId)
{
    var response = await _httpClient.GetAsync($"/api/runs/{runId}");
    
    if (response.IsSuccessStatusCode)
    {
        var data = await response.Content.ReadFromJsonAsync<WorkflowRunResponse>();
        return ApiResult<WorkflowRunResponse>.Success(data!);
    }
    
    var error = await response.Content.ReadAsStringAsync();
    return ApiResult<WorkflowRunResponse>.Failure(error);
}
```

---

### 3.5 SignalR Hub Client (Blazor Server)

In `Pages/Tickets/RunProgress.razor.cs`:

```csharp
@implements IAsyncDisposable

private HubConnection? _hubConnection;

protected override async Task OnInitializedAsync()
{
    // Fetch initial snapshot
    var runResult = await TicketsApiClient.GetRunAsync(RunId);
    if (runResult.IsSuccess)
    {
        Run = runResult.Data;
    }
    
    // Connect to SignalR hub
    _hubConnection = new HubConnectionBuilder()
        .WithUrl(NavigationManager.ToAbsoluteUri("/hubs/runs"))
        .WithAutomaticReconnect()
        .Build();
    
    _hubConnection.On<Guid, WorkflowRunEventResponse>("ReceiveRunEvent", OnEventReceived);
    
    await _hubConnection.StartAsync();
    await _hubConnection.InvokeAsync("JoinRun", RunId);
}

private async Task OnEventReceived(Guid runId, WorkflowRunEventResponse newEvent)
{
    if (runId == RunId)
    {
        Run.Events.Add(newEvent);
        
        // Update status if event indicates terminal state
        if (newEvent.EventType == "Completed" || newEvent.EventType == "Failed")
        {
            // Refresh full run to get updated status
            var updatedRun = await TicketsApiClient.GetRunAsync(RunId);
            if (updatedRun.IsSuccess)
            {
                Run = updatedRun.Data;
            }
        }
        
        await InvokeAsync(StateHasChanged);
    }
}

public async ValueTask DisposeAsync()
{
    if (_hubConnection is not null)
    {
        await _hubConnection.InvokeAsync("LeaveRun", RunId);
        await _hubConnection.DisposeAsync();
    }
}
```

---

## 4. Backend Responsibilities (Hicks)

### 4.1 Endpoint Implementation
- `POST /api/tickets/{number}/resolve` creates run, enqueues to Channel, returns 202 immediately
- `GET /api/runs/{runId}` returns full snapshot with all events to date
- `GET /api/runs/{runId}/events` (SSE fallback) streams new events until terminal

### 4.2 SignalR Hub
- Route: `/hubs/runs`
- Methods: `JoinRun(Guid runId)`, `LeaveRun(Guid runId)`, `ReceiveRunEvent(Guid runId, WorkflowRunEventResponse event)`
- Group pattern: `run-{runId}` per client connection
- Broadcasts new events to all clients in group when `IResolutionRunner` emits them

### 4.3 Resolution Runner Service
- `IHostedService` consuming a `Channel<ResolutionRunRequest>`
- Dequeues, sets run `Status=Running`, invokes `AgentOrchestrationService.ProcessTicketAsync(...)`
- Subscribes to orchestrator progress events (via `IProgress<T>` or `IAsyncEnumerable<T>`)
- On each event: writes `WorkflowRunEvent` row + broadcasts to SignalR group
- On terminal state: sets run `CompletedAt`, `FinalAction`, `FinalConfidence`, closes group

### 4.4 Orchestrator Hook (Bishop)
- `AgentOrchestrationService` must expose streaming progress: `IAsyncEnumerable<AgentExecutorEvent> ProcessTicketWithProgressAsync(...)`
- Each executor (ClassifierExecutor, IncidentDecomposerExecutor, etc.) emits:
  - `Started` (executor begins)
  - `AgentResponse` (agent returns structured data)
  - `Output` (text output)
  - `Error` (failure)
  - `Completed` (executor finishes)
- Mirrors Python `workflow/__init__.py` structure

---

## 5. User Experience Flow (End-to-End)

### Step 1: User clicks "Resolve with AI"
- Page: `/tickets/INC0010042`
- Button enabled only if ticket state is `New` or `InProgress`
- Click disables button, shows spinner inline

### Step 2: API call returns runId
- HTTP 202 response with `{ runId, statusUrl, eventsUrl }`
- Button hides, replaced with "Resolution started" message
- Page auto-navigates to `/tickets/INC0010042/runs/{runId}` after 500ms delay

### Step 3: Progress page loads
- Fetches initial run snapshot via `GET /api/runs/{runId}`
- Shows status badge (Pending → yellow, Running → blue, Completed → green, Failed → red, Escalated → orange)
- Renders event timeline with executor lanes
- Connects to SignalR hub in background

### Step 4: Real-time updates arrive
- Each new event appends to timeline
- Executor status icon updates (clock → spinner → checkmark or X)
- If payload contains structured data (e.g., KB articles, questions), render as expandable details
- No page refresh required; all updates via SignalR push

### Step 5: Run completes
- Final event with `EventType=Completed` received
- Status badge changes to green "Completed"
- Final action and confidence displayed at top
- SignalR connection closed
- "View updated ticket" link navigates back to `/tickets/INC0010042`

### Step 6: User returns to ticket detail
- Ticket now shows updated `ResolutionNotes`, `AgentAction`, `State` (likely `Resolved` or `Escalated`)
- Comment timeline includes system comment: "Resolved by AI agent (confidence: 92%)"

---

## 6. Edge Cases & Error Handling

### Case 1: API returns 200 (idempotent — run already exists)
- Frontend treats this the same as 202
- Navigate to existing run's progress page
- Show banner: "Resolution already in progress for this ticket"

### Case 2: API returns 409 Conflict
- Message: "Another resolution is already running for this ticket"
- Show error banner, do not navigate
- Button re-enables after 3 seconds

### Case 3: SignalR connection fails
- Log warning to console
- Fall back to polling `GET /api/runs/{runId}` every 2 seconds
- Show banner: "Live updates unavailable, polling for changes..."

### Case 4: Page refreshed mid-run
- On load, fetch current run snapshot
- If status is `Running`, connect to SignalR and resume listening
- All prior events already visible in snapshot; new events stream in

### Case 5: Run fails (Status=Failed)
- Last event with `EventType=Error` contains error message
- Status badge red, show error message prominently
- "Retry" button calls `POST /resolve` again (creates new run)

### Case 6: Run escalated (Status=Escalated)
- Ticket assigned to human agent
- Status badge orange, message: "Escalated to {AssignedTo}"
- Link to ticket detail to add manual resolution notes

---

## 7. Open Questions (for next squad sync)

1. **Auth on SignalR hub?** Blazor Server uses circuit identity; does the hub require explicit auth, or rely on same circuit?  
   *Default:* hub is open in dev, no auth check required for Phase 2.5.

2. **Max events per run?** If a run generates 1000+ events (e.g., verbose logging), do we cap frontend display?  
   *Default:* render all; optimize if performance issue observed.

3. **Run retention policy?** Can old runs be deleted, or are they audit logs?  
   *Default:* keep all runs; no deletion in Phase 2.5.

4. **Ticket list shows latest run status?** E.g., badge "AI resolving..." next to ticket in list view?  
   *Default:* no, only visible on detail page for Phase 2.5.

---

## 8. Tradeoffs & Risks

### Tradeoffs
- **SignalR vs SSE:** SignalR chosen because Blazor Server already has the circuit; no extra client library needed. SSE remains fallback for non-Blazor clients (e.g., future React UI).
- **Polling fallback cost:** If SignalR unavailable, polling every 2s could generate load. Mitigation: exponential backoff if run is in terminal state for >10s.
- **Event payload size:** Storing full JSON in `Payload` column (nvarchar(max)) is simple but could bloat. Mitigation: compress if >1KB, or extract common fields.

### Risks
- **Orchestrator doesn't stream:** If Bishop's `AgentOrchestrationService` returns only final result, the timeline will be empty until completion. UI degrades to a spinner + final result, losing the "live progress" value. **Mitigation:** Bishop must implement streaming API before Ferro builds progress page.
- **SignalR scale-out:** Phase 2.5 pins API to 1 replica (no Redis backplane). If multiple replicas deployed, events won't broadcast across instances. **Mitigation:** documented in Apone's architecture decision; follow-up work if scaling needed.
- **Race condition:** If webhook fires slower than page load, initial snapshot might be `Pending` with 0 events. User sees blank timeline briefly. **Mitigation:** show "Queued, waiting for agent..." message if `Status=Pending` and no events yet.

---

## 9. Success Criteria

✅ Clicking "Resolve with AI" returns HTTP 202 in <500ms and navigates to progress page  
✅ Progress page shows executor timeline updating in real-time via SignalR  
✅ Page refresh mid-run resumes listening without data loss  
✅ Run completion triggers final status update and closes SignalR connection  
✅ Ticket detail page reflects updated resolution notes after run completes  
✅ Idempotent re-trigger (same ticket, existing run) navigates to existing run instead of error

---

## 10. Implementation Sequence (Ferro owns all frontend tasks)

1. **Wait for Hicks:** `POST /api/tickets/{number}/resolve`, `GET /api/runs/{runId}`, SignalR hub `/hubs/runs` implemented
2. **Wait for Bishop:** `AgentOrchestrationService` streaming API exposed
3. **Once unblocked:**
   - Add `ResolveTicketRequest`, `ResolveTicketResponse`, `WorkflowRunResponse`, `WorkflowRunEventResponse` DTOs to `Services/TicketsApiClient.cs` (or shared `AgenticResolution.Contracts` if Hicks creates it)
   - Extend `TicketsApiClient` with `ResolveAsync` and `GetRunAsync` methods
   - Update ticket detail page (`Pages/Tickets/Detail.razor`) with "Resolve with AI" button + click handler
   - Create new page `Pages/Tickets/RunProgress.razor` with route `/tickets/{number}/runs/{runId}`
   - Implement SignalR hub connection, join/leave group logic, event subscription
   - Render executor timeline with status icons, event details, payload expansion
   - Add status badges, error banners, retry button for failed runs
   - Style with existing `wwwroot/app.css` palette (green=completed, blue=running, red=failed, orange=escalated)

4. **Testing:**
   - Manual smoke test: create ticket → resolve → watch progress page update live
   - Simulate SignalR failure (disable hub in API) → verify polling fallback works
   - Refresh page mid-run → verify resume works
   - Re-trigger resolve on same ticket → verify idempotent 200 handled correctly

---

## Appendix: Executor IDs (from Python workflow)

Expected `ExecutorId` values emitted by orchestrator (Bishop to confirm):

- `ClassifierExecutor` — ticket type classification
- `IncidentDecomposerExecutor` — diagnostic questions for incidents
- `RequestDecomposerExecutor` — process questions for service requests
- `KbSearchExecutor` — retrieve KB articles (hybrid search)
- `EvaluatorExecutor` — match KB articles to ticket
- `ResolutionSummarizerExecutor` — polish final resolution notes

UI should render these as human-readable labels:
- "Classifier" → "Classifying ticket type..."
- "IncidentDecomposer" → "Generating diagnostic questions..."
- "RequestDecomposer" → "Identifying service request steps..."
- "KbSearch" → "Searching knowledge base..."
- "Evaluator" → "Matching solutions to ticket..."
- "ResolutionSummarizer" → "Polishing resolution notes..."

Each executor can emit multiple events (Started → AgentResponse → Output → Completed).
