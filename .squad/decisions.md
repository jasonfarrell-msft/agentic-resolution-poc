# Squad Decisions

### 2026-04-29T21:00:00Z: Apone & Bishop — Phase 2 Architecture & Search Index Schema

**By:** Apone (Lead) & Bishop (AI/Agents)

**What (Apone – Architecture):** Phase 2 establishes five integrated subsystems: (1) Azure Function (Consumption, .NET 10) as webhook receiver with HMAC validation; (2) Single AI Search index `tickets-index` with hybrid search (BM25 + vector, semantic reranking, text-embedding-3-small 1536d embeddings); (3) Two Foundry agents (gpt-4o-mini): triage-agent (classify, auto-resolve, escalate) and resolution-summarizer (polish resolutions); (4) PUT /api/tickets/{id} endpoint accepting structured agent results (state, assignedTo, resolutionNotes, agentAction, agentConfidence, matchedTicketNumber) with HMAC validation; (5) Bicep modules for AI Search (Basic), OpenAI (gpt-4o-mini + text-embedding-3-small), Foundry Hub/Project, Function App (Consumption), Storage Account. Incremental cost ~$78–81/mo. Nine gate criteria (G1–G9) lock sequencing: Hicks owns G1–G7 (infra/backend), Vasquez owns G8–G9 (tests), Bishop gated on G1–G7 for agent work.

**What (Bishop – Search Index):** Single index `tickets-index` with 14 fields: keyword fields (id, number, shortDescription, description, category, priority, state, assignedTo, caller, resolutionNotes) + vector field (contentVector, 1536d, cosine HNSW). Semantic config `ticket-semantic` uses shortDescription as title, [description, resolutionNotes] as content, [category, number] as keywords. Hybrid search strategy: embed concatenation of [shortDescription + description + category], query via BM25 + vector similarity, rerank semantically. Top 5 results passed to triage agent. Seed 25 pre-resolved IT scenarios (password resets, VPN, Outlook, etc.) into both SQL and index at gate time (G2/G7).

**Why:** Phase 2 kickoff locked. Architecture avoids premature complexity (no Durable Functions, no multi-index KB corpus, no visual Logic Apps), prioritizes demo reproducibility (`azd up`), enables parallel team tracks (Hicks on infra, Vasquez on tests, Bishop on standby), and establishes unambiguous dependencies via 9 gate criteria. Demo cost remains low (~$103/mo combined). All decisions made once; no blocking questions.

---

### 2026-04-29T17:50:00Z: Vasquez — Phase 2 SQL test infrastructure plan

**By:** Vasquez (Tester)

**What:** Recommendation to swap EF Core InMemory → SQL Server (Testcontainers) in Phase 2 test suite. Rationale: InMemory cannot validate unique constraints (Ticket.Number), rowversion concurrency tokens, transaction isolation (post-commit webhook enqueue), or FK cascades. Testcontainers.MsSql 3.x recommended over ephemeral Azure SQL: ~2–5 sec startup vs 1–3 min Azure provisioning, $0 cost vs $0.50+/run, no CI credentials needed, identical T-SQL semantics to production.

**Why:** Phase 1 tests pass locally on InMemory but may fail under production load/concurrency. Migration effort: 2–3 hours (update TestWebAppFactory, unskip 39 tests, validate CI). Backward compatible; tests that pass on InMemory still pass on SQL.

---

### 2026-04-29T17:48:00Z: Ferro — Priority badge palette (post enum-flip)

**By:** Ferro (Frontend Dev)

**What:** Bootstrap 5.3 `text-bg-*` color scheme for Priority badge post-enum-flip: Critical=1 (red), High=2 (amber), Moderate=3 (cyan), Low=4 (green). Rendered via `$"{(int)Priority} - {Priority}"` so label matches enum value directly (no UI remap). One-liner, no custom CSS or contrast overrides needed.

**Why:** Severity gradient (warm→cool) matches risk perception. Stock Bootstrap utilities ensure accessibility. Avoids gray for Low — green better signals "safe" vs "unknown".

---

### 2026-04-29T17:47:00Z: Hicks — Phase 1 Azure deployment complete

**By:** Hicks (Backend Dev)

**What:** TicketPriority enum flipped to ServiceNow ordering (Critical=1, High=2, Moderate=3, Low=4); migration added (sentinel-value swap, reversible). Azure provision succeeded: RG `rg-agentic-res-agentic-resolution-dev` (East US 2); resources deployed with Bicep refactor for Entra-only SQL auth (MCAPS policy compliance). App Service uses user-assigned MI for SQL auth; Key Vault provisioned (RBAC). Smoke tests pass: GET /tickets (200), POST /api/tickets (201, INC0010001), round-trip read.

**Why:** Phase 1 authorization unblocked deployment. Bicep refactor required: SQL now uses Entra-only, App Service tagged for azd deploy. Estimated cost: ~$20–25/mo (B1 App Service, Basic SQL, monitoring). Webhook config deferred (TargetUrl/Secret are empty; no-op dispatch already handled).

---

### 2026-04-29T17:45:00Z: Jason — Copilot directives — Phase 1 alignment & scope expansion

**By:** Jason Farrell (MSFT) (via Copilot)

**What:** (1) DTO alignment: Ferro aligns to Hicks's `PagedResponse<T>.Total` (rename `TicketListResponse.TotalCount`); (2) Priority enum follows ServiceNow: Critical=1 → Low=4, drop UI remap; (3) Phase 1 Azure deployment AUTHORIZED (supersedes earlier "do not deploy" rule); (4) Phase 2 test swap to SQL Server confirmed as formal commitment (Vasquez's plan accepted).

**Why:** Phase 1 reconciliation. Items 1–2 resolve coordination surfaced by scaffold batch. Item 3 expands Phase 1 scope. Item 4 locks Phase 2 test infrastructure as formal deliverable.

---

## Active Decisions

### Phase 1 Architecture — Agentic Resolution Demo

**Author:** Apone (Lead/Architect)  
**Date:** 2025-07-25  
**Status:** Proposed  
**Scope:** Phase 1 only — local scaffold + IaC, no Azure deployment

#### 1. Solution Layout

**Decision: Single-project Blazor Server with embedded minimal API endpoints.**

```
/src
  AgenticResolution.Web/        # Blazor Server (.NET 9) + minimal API endpoints
    Pages/                      # Razor components (ticket form, list, detail)
    Components/                 # Shared Blazor components
    Endpoints/                  # Minimal API route groups (TicketEndpoints.cs)
    Data/                       # EF Core DbContext, entities, migrations
    Services/                   # WebhookService, TicketService
    wwwroot/
    Program.cs
    appsettings.json
/tests
  AgenticResolution.Tests/      # xUnit integration + unit tests
/infra
  main.bicep                    # azd entry point
  modules/                      # app-service.bicep, sql.bicep, keyvault.bicep, monitoring.bicep
  main.bicepparam
/docs
  architecture.md               # This document (rendered)
AgenticResolution.sln
azure.yaml                      # azd project definition
```

**Justification:** For a demo with one UI and a handful of API endpoints, a separate API project adds deployment complexity (two App Services or a reverse proxy) with zero benefit. Blazor Server already runs on Kestrel — minimal API endpoints sit beside it trivially. If Phase 2 needs a standalone API (e.g., for agent callbacks), we extract then.

#### 2. Azure Resources (Phase 1 Target)

| Resource | SKU / Tier | Name Pattern |
|----------|-----------|--------------|
| Resource Group | — | `rg-agentic-res-{env}` |
| App Service Plan | B1 (Basic) | `plan-agentic-res-{env}` |
| App Service | — | `app-agentic-res-{env}` |
| Azure SQL Logical Server | — | `sql-agentic-res-{env}` |
| Azure SQL Database | Basic 5 DTU | `sqldb-agentic-res-{env}` |
| Application Insights | — | `appi-agentic-res-{env}` |
| Log Analytics Workspace | — | `log-agentic-res-{env}` |
| Key Vault | Standard | `kv-agenticres-{env}` (24-char limit) |

**Naming convention:** `{resource-prefix}-agentic-res-{environment}` where `{env}` ∈ {dev, staging, prod}.

**Webhook Target (Phase 1):** Out of scope. Local dev uses `webhook.site` or stub. Phase 2 introduces actual receiver.

**Managed Identity Strategy:** System-assigned identity with `Key Vault Secrets User` and `SQL DB Contributor` roles. No connection strings in config — all from Key Vault.

#### 3. Data Model — Minimum Viable Ticket/Incident

```sql
CREATE TABLE Tickets (
    Id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    Number          NVARCHAR(15)    NOT NULL UNIQUE,
    ShortDescription NVARCHAR(200)  NOT NULL,
    Description     NVARCHAR(MAX)   NULL,
    Category        NVARCHAR(50)    NOT NULL,
    Priority        INT             NOT NULL DEFAULT 4,
    State           NVARCHAR(20)    NOT NULL DEFAULT 'New',
    AssignedTo      NVARCHAR(100)   NULL,
    Caller          NVARCHAR(100)   NOT NULL,
    CreatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE INDEX IX_Tickets_State ON Tickets (State);
CREATE INDEX IX_Tickets_Priority ON Tickets (Priority);
CREATE INDEX IX_Tickets_CreatedAt ON Tickets (CreatedAt DESC);
```

#### 4. Ticket Save → Webhook Flow

```
Blazor Form → POST /api/tickets → EF Core Save → Azure SQL
                                       ↓
                            Post-commit (SaveChangesAsync)
                                       ↓
                         WebhookService (fire-and-forget via Channel)
                                       ↓
                              HTTP POST + HMAC → Target URL
```

**Dispatch Rules:**
1. Post-commit only — after SaveChangesAsync succeeds
2. Async via Channel<T> — client gets 201 immediately
3. Retry — 3 attempts with exponential backoff (1s, 4s, 16s)
4. Idempotency — unique event_id for deduplication

**Payload:** JSON with event_id, event_type, timestamp, and ticket data. Signed with HMAC-SHA256.

#### 5. Local Dev Story

| Concern | Choice | Rationale |
|---------|--------|-----------|
| SQL Server | Docker (mcr.microsoft.com/mssql/server:2022-latest) | Cross-platform, no LocalDB dependency |
| Secrets | dotnet user-secrets | Connection string, webhook URL, HMAC secret |
| Webhook testing | webhook.site or local stub | Zero infra needed |
| EF Migrations | Code-first via dotnet ef CLI | Scripts in /src/AgenticResolution.Web/Data/Migrations/ |
| Hot reload | dotnet watch | Default Blazor dev experience |

#### 6. Infra-as-Code

**Decision: `azd` with Bicep modules.**

```
/infra
  main.bicep              # Orchestrator
  main.bicepparam         # Parameter file per environment
  modules/
    app-service.bicep     # Plan + App Service + managed identity
    sql.bicep             # Logical server + database + firewall rules
    keyvault.bicep        # Key Vault + access policies via RBAC
    monitoring.bicep      # App Insights + Log Analytics
/azure.yaml               # azd service mapping
```

Modules scaffolded in Phase 1 but not deployed. Local dev only.

#### 7. Out of Scope — Phase 1

- ❌ Azure AI Search
- ❌ Azure AI Foundry / Agent Framework
- ❌ Any AI/ML code or agent logic
- ❌ Actual Azure deployment
- ❌ Authentication / SSO
- ❌ Webhook receiver implementation
- ❌ Multi-tenant or multi-user support
- ❌ CI/CD pipeline (GitHub Actions)
- ❌ Custom domain / TLS

#### 8. Open Questions

1. **Ticket number format** — `INC` + 7-digit sequential or shorter?
2. **Category values** — hard-coded enum or free-text?
3. **Docker requirement** — all machines ready, or add LocalDB fallback?
4. **Webhook secret rotation** — single static secret acceptable?
5. **Target .NET version** — .NET 9 or .NET 8 LTS?

### Hicks — Backend Phase 1 Decisions

**Date:** 2026-04-29  
**Author:** Hicks (Backend)  
**Status:** Accepted  

#### 1. Validation library: DataAnnotations + minimal-API filter

Chose `System.ComponentModel.DataAnnotations` + a generic `ValidationFilter<T>` endpoint filter over FluentValidation.

- **Why:** Phase 1 has one write endpoint and a tiny DTO surface. DataAnnotations is in-box (zero new dependency), works seamlessly with `record` DTOs via `[property: ...]` attribute syntax, and produces RFC 7807 ValidationProblem output via `TypedResults.ValidationProblem`. FluentValidation pays off when rules grow into cross-field logic or are unit-tested in isolation — neither applies yet. Easy to swap later if Phase 2 needs it.

#### 2. Webhook payload shape

JSON, snake_case, signed HMAC-SHA256 over the raw body bytes.

```json
{
  "event_id": "9b3a... (Guid)",
  "event_type": "ticket.created",
  "timestamp": "2026-04-29T17:00:00Z",
  "ticket": {
    "id": "...", "number": "INC0010001",
    "short_description": "...", "description": "...",
    "category": "...", "priority": "Moderate", "state": "New",
    "assigned_to": null, "caller": "...",
    "created_at": "...", "updated_at": "..."
  }
}
```

Headers:
- `X-Resolution-Signature: sha256=<hex>` — HMAC-SHA256 of body using `Webhook:Secret`.
- `X-Resolution-Event-Id` — duplicates `event_id` for receiver-side dedup before parsing.
- `X-Resolution-Event-Type` — duplicates `event_type` for routing.

`event_id` is unique per delivery attempt batch (not per HTTP retry) and is the contract for receiver idempotency.

#### 3. Retry policy

Three retries on top of the initial attempt — total 4 attempts — with a fixed exponential backoff schedule **1s, 4s, 16s** (matches Apone's spec). No jitter in Phase 1 (single dispatcher, no thundering-herd risk). Retries cover transient network errors and any non-2xx response. Permanent failure is logged at Error; payload is dropped (no dead-letter queue in Phase 1).

#### 4. Dispatch transport

`Channel<WebhookEnvelope>` (bounded, capacity 1024, drop-oldest on overflow) read by a single `BackgroundService`. Endpoint enqueues **after** `SaveChangesAsync` succeeds — never inside the request transaction. Caller sees 201 immediately; the webhook is fire-and-forget from the API's point of view.

#### 5. Ticket number generation

DB-backed counter in `TicketNumberSequences` (single row, seeded `LastValue = 10000`). Increment uses an atomic `UPDATE ... OUTPUT INSERTED.LastValue` round-trip to avoid a read-modify-write race under concurrent POSTs. Format: `INC` + 7-digit zero-padded value, so the first ticket is `INC0010001` per Jason's directive.

#### 6. Project layout note

Backend code lives under `src/AgenticResolution.Web/{Api,Data,Models,Webhooks,Migrations}/`, side-by-side with Ferro's UI code under `Components/`, `Pages/`, `Layout/`, `Services/`. `Program.cs` is shared and uses banner-comment regions (`=== Backend (Hicks) ===` / `=== Frontend (Ferro) ===`) to keep merges clean.

#### 7. EF migration naming

The initial migration was generated with the EF Core default timestamp prefix (`{yyyyMMddHHmmss}_Initial.cs`) rather than `0001_Initial`. EF's tooling enforces its own ordering and renaming the file confuses `dotnet ef`. Treat the Apone spec name as logical, not literal.

---

### Ferro — UX choices — Ticket UI (Phase 1)

**Author:** Ferro (Frontend)  
**Date:** 2026-04-29  
**Status:** Recorded — open to revision in Phase 2

These are the meaningful UX defaults baked into the Blazor UI. Flagging so the squad can push back before we calcify them.

#### 1. Layout — sticky top bar + left sidebar

ServiceNow-ish chrome: dark slate top bar (`#1f2d3d`) with the brand on the left, fixed 220px sidebar with two nav entries (Tickets / New Ticket). No secondary modules, no app-switcher, no command palette. We are one app, one table — anything more is theatre.

#### 2. Ticket list — 25 rows per page, server-driven paging

- Page size: **25** rows. Big enough to skim, small enough that priority badges don't blur into wallpaper, and matches the API's default `pageSize`.
- Pagination controls: Previous / Next only. No jump-to-page. Phase-1 volumes don't justify the UI surface.
- Sort: API returns `CreatedAt DESC`. No client-side sort yet — punted to Phase 2 once we know which columns analysts actually sort by.
- Whole row click navigates to detail. Number column is also a real anchor for middle-click / open-in-new-tab.

#### 3. Form layout — single-column-ish, two-column on `md+`

Bootstrap `row g-3` with `col-md-6` for short fields (Caller, Assigned to, Category, Priority) and full width for Short description / Description. Mirrors ServiceNow's incident form density without copying its grid system.

#### 4. Category — free-text with `<datalist>` suggestions

Per Jason's directive (free-text category). I added a `<datalist>` of common values (Hardware, Software, Network, Access, Email, Other) so users get autocomplete without being locked into an enum. New values flow through fine.

#### 5. Priority — dropdown with ServiceNow labels

`1 - Critical / 2 - High / 3 - Moderate / 4 - Low`. **Note:** these are display labels. The wire value is the `TicketPriority` enum (Low=1, Moderate=2, High=3, Critical=4 — Hicks's ordering). UI never exposes the underlying int.

#### 6. Badge color palette

| Priority | Bootstrap class      | Visual     |
|----------|---------------------|------------|
| Critical | `text-bg-danger`    | red        |
| High     | `text-bg-warning`   | amber      |
| Moderate | `text-bg-info`      | cyan       |
| Low      | `text-bg-secondary` | gray       |

| State        | Class               |
|--------------|---------------------|
| New          | `text-bg-primary`   |
| In Progress  | `text-bg-info`      |
| On Hold      | `text-bg-warning`   |
| Resolved     | `text-bg-success`   |
| Closed       | `text-bg-secondary` |
| Cancelled    | `text-bg-dark`      |

#### 7. Validation UX

- Client-side via `DataAnnotationsValidator` + `ValidationSummary` — yellow warning banner above the form.
- Server errors (400/422 with `ValidationProblemResponse`) surface in a red alert at the top of the form, joined as `field: message · field: message`. Per-field error placement is good enough for Phase 1; we can wire `EditContext` field-level binding in Phase 2 if needed.

#### 8. Bootstrap delivery — CDN for now

Linked Bootstrap 5.3.3 via jsDelivr with SRI. No npm/libman pipeline in Phase 1. When we add Azure deployment, we'll vendor it locally so the app works behind WAFs that block CDNs.

#### 9. Things I deliberately did NOT build

- No "Local dev" README section (per Jason).
- No SQL Docker shortcut UI (per Jason).
- No webhook test page — that's Phase 2 receiver work.
- No edit/delete on tickets — list + create + read-only detail only.
- No auth UI — the form just trusts `Caller` as a string.

---

### Vasquez — Test strategy — InMemory now, SQL testcontainer Phase 2

**By:** Vasquez (Tester)  
**Date:** 2026-04-29  

**What:**
1. **EF Core InMemory provider** is the Phase 1 substitute for SQL Server in unit and integration tests, per Jason's "skip local-dev story" directive. `Microsoft.EntityFrameworkCore.InMemory 10.0.0` is referenced from `tests/AgenticResolution.Web.Tests`. No Testcontainers package added.
2. **`TestWebAppFactory`** (in `tests/AgenticResolution.Web.Tests/TestWebAppFactory.cs`) is the single integration entry point. It strips Hicks's production `AppDbContext` registration and substitutes an InMemory store keyed per-factory-instance, and it swaps `IWebhookDispatcher` for `FakeWebhookDispatcher` which records every enqueue.
3. **Webhook post-commit ordering** is asserted via two probes: (a) on the success path, snapshot the DB row at enqueue time inside the fake dispatcher; (b) on the failure path, assert ZERO enqueues when the save throws. This is the strongest assertion InMemory can support.
4. **Test projects** target `net10.0` (matches Jason's directive) and use xUnit + FluentAssertions. bUnit handles Blazor component tests in a separate project to keep the WebApplicationFactory dependency graph clean.
5. **Tests are written ahead of Hicks/Ferro impl** and currently marked `[Fact(Skip = "Pending Hicks impl: <type>")]` or `[Fact(Skip = "Pending Ferro impl: <type>")]`. Skipped count drops to zero as types land. This is intentional — Vasquez gets ahead of the queue.

**Why:**
- Jason said no Docker SQL for Phase 1. InMemory is the only option that doesn't add infra.
- InMemory is *not* a faithful substitute for SQL Server: no real transactions, no unique-constraint enforcement on string indexes by default, no SQL collation behaviour, no rowversion concurrency. Tests that rely on those guarantees will give false confidence.
- Writing tests before implementation locks the contract early — Hicks and Ferro have a target to compile against, and the team gets a working CI signal the moment the first type lands.

**Phase 2 follow-up (REQUIRED before production):**
- Replace InMemory with a SQL Server testcontainer (Testcontainers.MsSql) or an Azure SQL ephemeral DB so ticket-number uniqueness, transactional commit-then-fire ordering, and concurrency-token semantics are validated against the real engine.
- Re-run `WebhookFiringTests.Webhook_Envelope_Is_Enqueued_After_DB_Commit` on real SQL — InMemory cannot prove the post-commit guarantee.
- Add a concurrent-create test for `TicketNumberGenerator` against real SQL (50 parallel writers, assert 50 distinct numbers).

**Coordination ask:**
- **Hicks**: please name the EF Core context `AppDbContext` and the dispatcher abstraction `IWebhookDispatcher` with `Task EnqueueAsync(WebhookEnvelope, CancellationToken)`. If you go with different names, ping me and I'll rename in one pass.
- **Hicks**: please put DTOs under `AgenticResolution.Web.Models.Dtos` (`CreateTicketRequest`, `TicketResponse`).
- **Ferro**: `TicketForm` component should accept `EventCallback<CreateTicketRequest> OnSubmit`, and `PriorityBadge` should accept a `Priority` enum parameter and render `.priority-{level}` CSS classes.
- **Apone**: `tests/EDGE_CASES.md` is the Phase 2 hardening backlog — please review and prioritise before Phase 2 kicks off.

**Files added:**
- `tests/AgenticResolution.Web.Tests/AgenticResolution.Web.Tests.csproj`
- `tests/AgenticResolution.Web.Tests/TestWebAppFactory.cs`
- `tests/AgenticResolution.Web.Tests/FakeWebhookDispatcher.cs`
- `tests/AgenticResolution.Web.Tests/Models/TicketTests.cs`
- `tests/AgenticResolution.Web.Tests/Webhooks/HmacSignatureTests.cs`
- `tests/AgenticResolution.Web.Tests/Webhooks/TicketNumberGeneratorTests.cs`
- `tests/AgenticResolution.Web.Tests/Webhooks/WebhookFiringTests.cs`
- `tests/AgenticResolution.Web.Tests/Api/CreateTicketEndpointTests.cs`
- `tests/AgenticResolution.Web.Tests/Api/GetTicketEndpointTests.cs`
- `tests/AgenticResolution.Web.Tests/Api/ListTicketsEndpointTests.cs`
- `tests/AgenticResolution.Web.ComponentTests/AgenticResolution.Web.ComponentTests.csproj`
- `tests/AgenticResolution.Web.ComponentTests/Components/TicketFormTests.cs`
- `tests/AgenticResolution.Web.ComponentTests/Components/PriorityBadgeTests.cs`
- `tests/EDGE_CASES.md`

---

### User Directives — Phase 1 stack & scope

**By:** Jason Farrell (MSFT) (via Copilot)  
**Date:** 2026-04-29T17:08:00Z  

**What:**
1. **Use .NET 10** for the entire solution (Blazor app, API, tests, libraries).
2. **Skip the local-dev story** in Phase 1 — do not scaffold Docker SQL, do not document `dotnet user-secrets` setup, do not stand up webhook.site stubs. Local-dev concerns are out of scope for Phase 1.
3. Remaining open questions from Apone's Phase 1 architecture proposal are accepted with Apone's defaults:
   - Category: **free-text** (no fixed enum)
   - HMAC secret: **static secret** acceptable for Phase 1
   - Ticket number format: **`INC0010001`** ServiceNow-style

**Why:** User direction during Phase 1 kickoff — captured for team memory so Hicks, Ferro, Vasquez, and Bishop respect these choices in all scaffolding and downstream work.

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction
