# Decision: Classification + Routing Layer

**By:** Bishop (AI/Agents Specialist)  
**Date:** 2026-05-01  
**Status:** Implemented  

## What

Added a two-stage classification and routing layer that fires before resolution:

1. **ClassificationAgent** (`ticket-classification-agent`, `gpt-41-mini`) — first agent to run. Fetches full ticket via `get_ticket_by_number`, reads short_description/description/category, classifies as "incident" or "request" using keyword and category heuristics. Fails safe to "incident" when ambiguous. Emits: `{ "classification", "confidence", "rationale" }`.

2. **IncidentTicketAgent** (`ticket-incident-agent`, `gpt-41-mini`) — handles classified incidents. Searches resolved tickets, auto-resolves at ≥0.8 confidence (`agent_action="incident_auto_resolved"`) or escalates (`agent_action="escalate_incident"`).

3. **RequestTicketAgent** (`ticket-request-agent`, `gpt-41-mini`) — handles service requests. Identifies standard request types → InProgress (`agent_action="request_auto_queued"`) or complex requests → OnHold (`agent_action="request_needs_approval"`).

4. **`RunAgentPipelineAsync`** replaces `RunResolutionAgentAsync` as the `WebhookDispatchService` entry point. Flow: classify → route → write-back with Classification field.

5. **`Ticket.Classification`** column (`nvarchar(20)`, nullable) added via migration `20260501000000_AddClassificationField`.

6. **`AgentRunResult.Classification`** optional property added (null = unknown).

## Why

Routing by ticket type produces better outcomes: incidents need root-cause matching; service requests need workflow queuing. A single generic resolution agent handles both poorly. Classification-first ensures each ticket is handled by the right specialist agent with appropriate decision logic.

## Key implementation decisions

- **`RunAndPollRawTextAsync`** — new helper returns raw assistant text without parsing. Used exclusively by `RunClassificationAgentAsync` so the custom `{ classification, confidence, rationale }` JSON schema is parsed correctly by `ParseClassificationResponse` (separate from the standard `{ action, confidence, notes, matchedTicketNumber }` schema used by all other agents).
- **Fallback safety net** — if classification returns null/unknown, `RunAgentPipelineAsync` falls back to `RunResolutionAgentAsync`. Original agent is preserved.
- **State transitions** updated in `WriteBackAsync` as a switch expression to handle: `incident_auto_resolved` → Resolved, `request_auto_queued` → InProgress, `request_needs_approval` → OnHold.
- **`TicketWebhookSnapshot` not modified** — snapshot intentionally excludes Description. Agents fetch full ticket via `get_ticket_by_number`. This keeps the webhook payload lean.
- **Prompts as versioned constants** — all three new agent prompts live in `AgentDefinitions.cs` as string constants. No inline prompt strings.
