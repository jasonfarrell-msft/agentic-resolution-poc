# Skill: Resolution SSE Terminal Contract

**Domain:** Server-sent events for ticket resolution workflows

## Pattern

When a workflow streams per-stage progress, stage-level `status: "completed"` means only that one executor finished. The UI must wait for the terminal workflow event before showing final completion.

## Contract

Treat an SSE event as final only when:

```json
{
  "stage": "workflow",
  "terminal": true
}
```

Then read `status` or `event` for the outcome:

- `resolved`
- `escalated`
- `failed`

## UI rule

Do not infer final state from non-terminal events. Intermediate stages may emit:

```json
{"stage":"evaluator","status":"completed"}
```

That is progress, not workflow completion.

## Ticket detail mapping

The Tickets API now has a native `Escalated` state. After a terminal resolver event, reload `GET /api/tickets/{number}/details` and use `ticket.state` as the source of truth (`Resolved` or `Escalated`). Do not infer the final ticket state from intermediate stage events.
