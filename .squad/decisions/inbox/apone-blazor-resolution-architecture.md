# Apone — Architecture Decision: Blazor Frontend, Manual Resolve Flow, Workflow Visibility

**By:** Apone (Lead/Architect)
**Requested by:** Jason Farrell
**Status:** Proposed — pending team acknowledgement before implementation
**Scope:** Phase 2.5 — Blazor UI rebuild + decoupling agent pipeline from webhook dispatch + workflow progress visibility

---

## 1. Solution Topology

Add a new project: **`AgenticResolution.Web`** (Blazor Web App, .NET 10, **Interactive Server** render mode).

- **Why Server, not WebAssembly:** Phase 1 is a demo, no public users, low latency to API in same region, server-side SignalR makes the live "workflow progression" view trivial to wire (no extra auth/CORS dance for the SSE/WebSocket from browser to API).
- **Why a separate project, not embedded in Api:** Aligns with the 2025-07-25 three-project split decision (Api/Web/McpServer). Keeps the API container slim and lets Web deploy independently to App Service.
- **HTTP access:** Typed `ITicketsApiClient` registered with `HttpClientFactory`, base URL from config (`Backend:ApiBaseUrl`). No direct DbContext access from Web.
- **Shared contracts:** Extract DTOs (`TicketResponse`, `PagedResponse<T>`, new `TicketDetailResponse`, `CommentResponse`, `WorkflowRunResponse`, `WorkflowRunEventResponse`) into a small **`AgenticResolution.Contracts`** class library referenced by Api, Web, and McpServer. Prevents drift; eliminates the DTO reconciliation pain noted in the Phase 1 history.

**Pages (Ferro to spec):**
- `/tickets` — list with filters (assignee, status, category, priority, free-text q), sort by Created/Modified asc/desc, paged.
- `/tickets/{number}` — full detail incl. comments timeline, "Add comment" form, **"Resolve with AI"** action button.
- `/tickets/{number}/runs/{runId}` — live workflow progression view (DevUI-style executor lane + streamed events). Default deep-link target after clicking Resolve.
- Colorful UI via a single design-token CSS file + status/priority chips. No heavy component library required.

---

## 2. API Contract Changes (Hicks owns)

### 2.1 List endpoint — extend filtering + ordering

`GET /api/tickets` — current shape only supports `state`, `page`, `pageSize`. Replace with:

| Param        | Type                              | Notes                                                          |
| ------------ | --------------------------------- | -------------------------------------------------------------- |
| `assignedTo` | string                            | Exact match; `unassigned` sentinel matches `NULL`.             |
| `state`      | TicketState (repeatable)          | Accept comma-separated list, e.g. `?state=New,InProgress`.     |
| `category`   | string                            | Exact match.                                                   |
| `priority`   | TicketPriority (repeatable)       | Comma-separated list.                                          |
| `q`          | string                            | Substring on `ShortDescription` or `Description` (existing).   |
| `sort`       | enum `created` \| `modified`      | Default `created`.                                             |
| `dir`        | enum `asc` \| `desc`              | Default `desc`.                                                |
| `page`       | int (≥1)                          | Default 1.                                                     |
| `pageSize`   | int (1–100)                       | Default 25.                                                    |

Sort field whitelist enforced server-side (no caller-supplied SQL). Reuse `PagedResponse<T>`.

### 2.2 Detail endpoint — include comments

Keep `GET /api/tickets/{number}` returning the existing `TicketResponse`, **plus** add `GET /api/tickets/{number}/details` returning a new `TicketDetailResponse { Ticket, Comments[], Runs[] }`. Single round-trip for the detail page; cheap to compose server-side. Existing endpoint stays unchanged for backward compatibility (MCP server depends on it).

### 2.3 Comments

- `GET /api/tickets/{number}/comments` → `IReadOnlyList<CommentResponse>` ordered by `CreatedAt asc`.
- `POST /api/tickets/{number}/comments` body `{ author, body, isInternal }` → `Created<CommentResponse>`.
- No update/delete in Phase 2.5 — tickets are append-only. Validation: `body` 1–4000 chars.

### 2.4 Manual resolve trigger

- `POST /api/tickets/{number}/resolve` → `Accepted<{ runId, statusUrl, eventsUrl }>` (HTTP 202).
- Idempotency: if a run is already `Pending`/`Running` for the ticket, return that run instead of creating a new one (HTTP 200).
- Body optional: `{ note?: string }` written into the run record for traceability.

### 2.5 Workflow run visibility

- `GET /api/tickets/{number}/runs` → list runs (most recent first).
- `GET /api/runs/{runId}` → full run snapshot incl. events.
- `GET /api/runs/{runId}/events` → **Server-Sent Events** stream of new events; closes when run reaches terminal state. Backed by an in-process pub/sub (Channel) keyed by runId, with a SQL backstop for replay-from-zero.
- Optional but recommended: a SignalR hub `/hubs/runs` so the Blazor Server page can join a group `run-{runId}` instead of holding an SSE connection. **Decision:** use SignalR (Blazor Server already speaks it), keep SSE only as a fallback for non-Blazor clients.

---

## 3. Database / Model Changes (Hicks owns; Vasquez writes tests)

### 3.1 New entities

```text
TicketComment
  Id           Guid PK
  TicketId     Guid FK → Ticket.Id  (cascade delete)
  Author       string(100) NOT NULL
  Body         nvarchar(max) NOT NULL
  IsInternal   bit NOT NULL default 0
  CreatedAt    datetime2 default SYSUTCDATETIME()

WorkflowRun
  Id              Guid PK
  TicketId        Guid FK → Ticket.Id  (cascade delete)
  Status          int NOT NULL  (Pending=0, Running=1, Completed=2, Failed=3, Escalated=4)
  TriggeredBy     string(100) NULL    (user/system identifier)
  Note            nvarchar(500) NULL
  StartedAt       datetime2 default SYSUTCDATETIME()
  CompletedAt    datetime2 NULL
  FinalAction     string(100) NULL    (mirrors Ticket.AgentAction at run end)
  FinalConfidence float NULL

WorkflowRunEvent
  Id          Guid PK
  RunId       Guid FK → WorkflowRun.Id  (cascade delete)
  Sequence    int NOT NULL              (per-run monotonic, indexed)
  ExecutorId  string(100) NULL          ("ClassifierExecutor", etc.)
  EventType   string(50) NOT NULL       (Started|Routed|AgentResponse|Output|Error|Completed)
  Payload     nvarchar(max) NULL        (JSON; small)
  Timestamp   datetime2 default SYSUTCDATETIME()
  -- index (RunId, Sequence)
```

### 3.2 Indexes for new filter/sort

Add indexes on `Ticket.UpdatedAt`, `Ticket.AssignedTo`, `Ticket.Category`. Existing `(State)` and `(CreatedAt)` indexes stay. Composite indexes deferred until query plans show a need.

### 3.3 Migrations

Single new EF migration `20260507000000_AddCommentsAndWorkflowRuns`. Snapshot regenerated. No data backfill required.

---

## 4. Removing the Automatic Webhook → Agent Trigger

Two things are conflated today and both must be cut:

**(a) Webhook auto-fire on Create/Update** in `TicketsEndpoints` (`dispatcher.Enqueue(...)` in `CreateAsync` and `UpdateAsync`).
**(b) Implicit agent pipeline run** inside `WebhookDispatchService.ExecuteAsync` (the `RunAgentAsync` fire-and-forget after every webhook event).

**Decision:**

1. **Delete (b) entirely.** The agent pipeline must only run via explicit `POST /resolve`. Move the orchestration call out of `WebhookDispatchService` into a new `IResolutionRunner` hosted service that consumes a `Channel<ResolutionRunRequest>` populated by the resolve endpoint.
2. **Keep webhook plumbing but make it opt-in.** Webhooks are still useful for the ServiceNow integration story. Gate enqueue behind a config flag `Webhook:AutoDispatchOnTicketWrite` (default `false` in Phase 2.5). When false, `CreateAsync`/`UpdateAsync` skip `dispatcher.Enqueue`. The dispatcher and HTTP retry loop stay intact; nothing else regresses.
3. **No deletion of `WebhookDispatchService` code paths** — we preserve the Phase 1 contract for replay later. If the team votes to delete, that's a follow-up decision.

**Resolution runner shape:**

```text
POST /api/tickets/{number}/resolve
  → create WorkflowRun (Pending), enqueue ResolutionRunRequest(runId, ticketNumber)
  → return 202 with runId

ResolutionRunner (BackgroundService)
  → dequeues, sets run Running
  → invokes AgentOrchestrationService.ProcessTicketAsync(...)
  → on each executor event (subscribe to AgentOrchestrationService events
    OR poll via wrapper) writes a WorkflowRunEvent row AND publishes to
    SignalR group "run-{runId}"
  → on terminal state, sets run Completed/Failed/Escalated, closes the group
```

If `AgentOrchestrationService` does not currently expose progress callbacks, **Bishop** owns adding an `IProgress<AgentExecutorEvent>` (or `IAsyncEnumerable<AgentExecutorEvent>`) overload. The Python `workflow/__init__.py` already streams executor events through `WorkflowContext.send_message`/`yield_output`; the .NET orchestrator must mirror that surface so the UI has something to render.

---

## 5. Azure Deployment Implications

**Confirm with reality first.** As of this review, `azure.yaml`, `infra/main.bicep`, `infra/resources.bicep`, `infra/modules/aisearch.bicep`, `infra/modules/openai.bicep`, `infra/modules/containerapp-{api,agent,mcp}.bicep`, `infra/modules/containerappenvironment.bicep`, and `src/dotnet/AgenticResolution.AppHost/Program.cs` are **0-byte stub files**. The "apparently already happening" Container Apps deployment is not actually in this branch's Bicep — only `containerapp.bicep` (a generic webhook target) and `containerregistry.bicep` have content. **Hicks must re-baseline infra before plumbing the new Web service.**

### Target topology

| Component                           | Hosting                                                           | Notes                                                                                  |
| ----------------------------------- | ----------------------------------------------------------------- | -------------------------------------------------------------------------------------- |
| `AgenticResolution.Web` (Blazor)    | **Azure App Service for Linux**, .NET 10, Always On, HTTP/2, **WebSockets ON** | Blazor Server requires WebSockets for the SignalR circuit + the runs hub. |
| `AgenticResolution.Api` (.NET API)  | **Azure Container Apps** (external ingress, port 8080)            | Already containerized via existing Dockerfile. Min replicas 1.                         |
| Resolution runner / agent pipeline  | **Same Container App as the Api** (in-process BackgroundService) | Avoid a second container until we have a reason. Scaling concerns are not Phase 2.5.   |
| `TicketsApi.McpServer`              | Local stdio for now                                               | Unchanged. Not deployed.                                                               |
| Python DevUI / `devui_serve.py`     | **Local-only**, not deployed                                      | Dev tool. The .NET orchestrator is the production runner.                              |
| Azure SQL                           | Existing                                                          | Add migration on deploy via the existing `db.Database.MigrateAsync()` startup hook.    |
| Key Vault, App Insights, Log Analytics | Existing                                                       | Web App gets MI + Key Vault read; Container App API already wired.                     |

### Azd / Bicep checklist (Hicks)

- [ ] Repopulate `azure.yaml` with two services: `web` (host: `appservice`, project: `AgenticResolution.Web`) and `api` (host: `containerapp`, project: `AgenticResolution.Api`).
- [ ] Repopulate `infra/main.bicep` and `infra/resources.bicep` (currently empty).
- [ ] New module `infra/modules/appservice-web.bicep` for the App Service plan + site, with `webSocketsEnabled: true`, MI assigned, app settings: `Backend__ApiBaseUrl`, `ApplicationInsights__ConnectionString`.
- [ ] Fill `infra/modules/containerapp-api.bicep` (currently 0 bytes) with the .NET API container, env vars for `ConnectionStrings__Default` (Key Vault ref), `Cors__AllowedOrigins=https://<web-fqdn>`, `Webhook__AutoDispatchOnTicketWrite=false`.
- [ ] `infra/modules/containerappenvironment.bicep` (0 bytes) — create the env that hosts the API.
- [ ] CORS: API allows the App Service default hostname **and** any custom domain. Today's `Program.cs` reads `Cors:AllowedOrigins` from config — good, no code change needed.
- [ ] SignalR sticky-session note: Blazor Server's circuit lives in the Web App, **not** the API. The hub for `run-{runId}` runs inside the API Container App. Container Apps with multiple replicas will need a backplane (Redis) for SignalR, **or** we pin `minReplicas==maxReplicas==1` for the API in Phase 2.5. **Decision:** pin to 1 replica for the API to avoid a Redis dependency in this phase. Document it.
- [ ] Aspire AppHost (`AgenticResolution.AppHost/Program.cs` is 0 bytes) — either flesh it out as the local dev orchestrator (recommended) or delete the project to reduce surface. **Decision:** keep and flesh out — Aspire is the cleanest local dev story for Web + Api + SQL + DevUI sidecar.

---

## 6. Risks, Sequencing, and Handoffs

### Risks

1. **SignalR scale-out.** Pinned to 1 API replica for now. Document so the next person doesn't bump `maxReplicas` without adding a Redis backplane.
2. **Agent orchestrator progress surface.** Today the .NET `AgentOrchestrationService` returns a single result. Without an event stream, the workflow page will only show start + final. Bishop must add streaming hooks or the UI degrades to a polling spinner.
3. **DevUI vs .NET orchestrator drift.** The Python workflow is the richer reference. Risk that the .NET runner falls behind. Mitigation: keep DevUI runnable locally, and have Bishop diff the two on every workflow change.
4. **CORS / mixed content** between App Service (HTTPS) and Container App (HTTPS) — both are TLS by default, but the Web → Api base URL must be the public HTTPS FQDN, not the internal one. Hicks: assert this in config.
5. **Comment authorship** — no auth in this phase. We accept author as a free-text field. Flag for follow-up before any external demo.
6. **Webhook removal regression.** Some integration tests likely assume the create/update path enqueues a webhook. Vasquez must adjust tests in lockstep with the code change, or split the assertion behind the new config flag.

### Sequencing

```
Step 1 (parallel)
  Hicks  : Contracts library + DB migrations + new endpoints (filter/sort, comments, resolve, runs)
  Hicks  : Re-baseline azure.yaml + infra/*.bicep stubs
  Bishop : Add streaming/progress surface to AgentOrchestrationService
  Ferro  : Component spec + design tokens + page wireframes (no code yet)

Step 2 (after Step 1 contracts merge)
  Ferro    : AgenticResolution.Web project, list + detail + run pages, SignalR client
  Hicks    : ResolutionRunner BackgroundService + SignalR hub + SSE fallback
  Vasquez  : Tests for new endpoints, webhook flag default-off, run lifecycle

Step 3 (after Step 2)
  Hicks    : Webhook auto-dispatch flag flip in Api Program.cs, delete RunAgentAsync auto-trigger
  Vasquez  : End-to-end test: create ticket → no agent fires → POST /resolve → run completes → events visible
  Apone    : Review + reviewer-gate (different agent than author per charter rule)

Step 4 (deployment)
  Hicks    : azd up against a dev sub; smoke-test from Web App against Api Container App
```

### Handoffs

**Ferro (Frontend):**
- Build `AgenticResolution.Web`, Interactive Server.
- Pages: `/tickets`, `/tickets/{number}`, `/tickets/{number}/runs/{runId}`.
- Live run view consumes SignalR hub `/hubs/runs`, group `run-{runId}`, falls back to polling `GET /api/runs/{runId}` if hub unavailable.
- Ship a single `app.css` with status/priority chip styles. Colorful, but readable; respect prefers-reduced-motion.
- Talk to the API only through the typed `ITicketsApiClient` from `AgenticResolution.Contracts`.

**Hicks (Backend):**
- Create `AgenticResolution.Contracts` class library; move DTOs.
- Implement endpoint changes in §2 with sort whitelist enforced.
- EF entities + migration in §3.
- Remove auto-enqueue webhook from `CreateAsync`/`UpdateAsync` behind `Webhook:AutoDispatchOnTicketWrite` flag (default false). Delete the `RunAgentAsync` block in `WebhookDispatchService`.
- Implement `IResolutionRunner` hosted service + SignalR hub + SSE endpoint.
- Re-baseline `azure.yaml`, `infra/main.bicep`, `infra/resources.bicep`, the empty containerapp/openai/aisearch/env modules. New `appservice-web.bicep`. Pin API container to 1 replica with documented reason. Flesh out `AgenticResolution.AppHost/Program.cs` for local dev.

**Bishop (AI/Agents):**
- Expose progress events from `AgentOrchestrationService` (`IAsyncEnumerable<AgentExecutorEvent>` preferred).
- Mirror Python workflow structure where reasonable so the run page renders the same executor lanes (`ClassifierExecutor`, `IncidentDecomposerExecutor`, `EvaluatorExecutor`, etc.).
- No new agents in this phase. Confirm the orchestrator can be invoked by `runId` and report each executor transition.

**Vasquez (Tester):**
- Update existing tests that assume webhook fires on create/update — they must pass with the flag default-off and pass with it on.
- New tests:
  - Filter/sort matrix (assignee, state list, category, priority list, q, sort × dir, paging boundaries).
  - Comment add/list, ordering, length validation.
  - Resolve flow: 202 returns runId, idempotent re-trigger returns same run, run transitions Pending→Running→Completed/Escalated, events row count > 0.
  - Run events SSE/SignalR smoke test (a test SignalR client, not a UI test).

---

## Open Questions (non-blocking — Apone will decide if no answer in 24h)

1. **Auth on `/resolve`** — leave open for the demo, or require a static header secret (consistent with the existing HMAC pattern)? *Default if silent:* leave open in dev, require header in deployed env.
2. **Comment authorship** — accept free-text now, plan a real identity pass later? *Default if silent:* yes.
3. **Run retention** — keep all runs forever, or trim after N per ticket? *Default if silent:* keep all in Phase 2.5; reassess at first cost review.
