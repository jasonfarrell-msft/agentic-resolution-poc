# Workflow Executor Sequence Names

This document defines the executor IDs emitted by `AgentOrchestrationService` during manual resolution runs. Ferro should display these names in the workflow progression UI.

## Executor Stages

The agent pipeline follows this fixed sequence (mirroring the Python workflow structure):

### 1. ClassifierExecutor
**Stage:** Classification  
**Purpose:** Determine whether the ticket is an incident or service request.  
**Output:** Route decision (`incident` or `request`).  
**Current behavior:** All tickets routed to `incident` path.

### 2. IncidentFetchExecutor
**Stage:** Fetch Details  
**Purpose:** Retrieve full ticket details from the database via agent.  
**Output:** Ticket metadata (description, category, priority).

### 3. IncidentDecomposerExecutor
**Stage:** Decompose & Search  
**Purpose:** Break the ticket into resolution questions and search the knowledge base.  
**Output:** Core problem statement, questions + answers, preliminary confidence.  
**Note:** In the current implementation, fetch + decompose are combined in a single agent call.

### 4. EvaluatorExecutor
**Stage:** Evaluate Resolution  
**Purpose:** Calibrate confidence and consolidate the resolution.  
**Output:** Final confidence score, resolution text, KB source.

### 5. ResolutionExecutor
**Stage:** Resolution  
**Condition:** Confidence >= threshold (0.80).  
**Purpose:** Mark the ticket resolved and record the agent's resolution.  
**Output:** Ticket updated with state=Resolved.

### 6. EscalationExecutor
**Stage:** Escalation  
**Condition:** Confidence < threshold (0.80).  
**Purpose:** Assign the ticket to a human specialist.  
**Output:** Ticket left in New state with escalation rationale.

---

## Event Types

Each executor emits a sequence of events:

| EventType   | Meaning                                               |
|-------------|-------------------------------------------------------|
| `Started`   | Executor has begun processing.                        |
| `Routed`    | Conditional routing decision made (classifier only).  |
| `Output`    | Intermediate or final output from the executor.       |
| `Error`     | Executor encountered a failure.                       |
| `Completed` | Executor finished successfully.                       |

---

## Example Workflow Run Event Sequence

```
Seq  ExecutorId                  EventType   Payload
---- --------------------------- ----------- ----------------------------
1    ClassifierExecutor          Started     null
2    ClassifierExecutor          Routed      {"route":"incident"}
3    ClassifierExecutor          Completed   null
4    IncidentFetchExecutor       Started     null
5    IncidentFetchExecutor       Completed   null
6    IncidentDecomposerExecutor  Started     null
7    IncidentDecomposerExecutor  Completed   null
8    EvaluatorExecutor           Started     null
9    EvaluatorExecutor           Output      {"output":"Confidence: 0.92, Action: incident_auto_resolved"}
10   EvaluatorExecutor           Completed   null
11   ResolutionExecutor          Started     null
12   ResolutionExecutor          Output      {"output":"Ticket INC0010022 resolved..."}
13   ResolutionExecutor          Completed   null
```

---

## UI Display Guidance

**Ferro** should:
- Display executor lanes in the sequence listed above.
- Show each executor's status: Pending → Running → Completed/Failed.
- For `Output` events, display the payload text in the executor's detail panel.
- For `Error` events, highlight the executor as failed and display the error payload.
- Use the `Routed` event payload to show the classification decision.
- Poll `GET /api/runs/{runId}/events` or subscribe via SignalR (future) to receive new events.

**Status mapping:**
- WorkflowRun.Status = `Pending` → all executors show Pending.
- WorkflowRun.Status = `Running` → highlight the executor with the most recent `Started` event as active.
- WorkflowRun.Status = `Completed` → all executors show Completed.
- WorkflowRun.Status = `Escalated` → EscalationExecutor completed, ResolutionExecutor skipped.
- WorkflowRun.Status = `Failed` → highlight the last executor with an `Error` event as failed.

---

## Future Enhancements

1. **RequestFetchExecutor / RequestDecomposerExecutor:** When request classification is enabled, these executors will appear instead of IncidentFetchExecutor / IncidentDecomposerExecutor.
2. **SignalR Hub:** Real-time event streaming to avoid polling. Executor events broadcast to group `run-{runId}`.
3. **Retry Logic:** If an executor fails, emit a `Retry` event and re-invoke the agent.
4. **Agent Response Parsing:** Currently the orchestrator synthesizes events. Future: agents emit structured progress via `/stream` endpoints.
