# Project Context

- **Owner:** Jason Farrell
- **Project:** agentic-resolution — testing the ServiceNow simulation. Critical paths: ticket validation, SQL persistence, webhook firing on save. Future: agent-driven resolution flows.
- **Stack:** xUnit, bUnit, ASP.NET Core integration testing (WebApplicationFactory), SQL Server (LocalDB or testcontainers)
- **Phase 1 scope:** test scaffolding + tests for ticket entry, persistence, and webhook trigger.
- **Created:** 2026-04-29

## Phase 1 Architecture (Apone)

**Solution:** Single-project Blazor Server + embedded minimal API. All tests run against one app (WebApplicationFactory).

**Critical paths to test:**
1. Blazor form → POST /api/tickets validates input (required fields: short description, category, caller)
2. Ticket persists to SQL: Id (GUID), Number auto-gen, State="New", all required fields set
3. WebhookService fires post-SaveChangesAsync success: async Channel dispatch, retries 3x, HMAC signature included
4. Ticket list/detail views render persisted data correctly

**Test infrastructure:**
- WebApplicationFactory with in-memory SQL or testcontainers
- bUnit for component rendering (form validation, success states)
- xUnit for endpoint + service layer (webhook dispatch, retry logic, HMAC signing)

**Questions pending:** Docker/testcontainers strategy confirmed? .NET version locked?

## Learnings

### 2026-04-29 — Phase 1 test scaffold (proactive, ahead of Hicks/Ferro impl)

**Test projects created** (both target net10.0 per Jason's directive):
- `tests/AgenticResolution.Web.Tests` — xUnit + Microsoft.AspNetCore.Mvc.Testing 10.0.0 + EF Core InMemory 10.0.0 + FluentAssertions. References the Web csproj.
- `tests/AgenticResolution.Web.ComponentTests` — bUnit 1.34 + xUnit + FluentAssertions. References the Web csproj.
- No .sln existed yet; Hicks will add both when scaffolding the solution.

**Tests written** (all marked `[Fact(Skip = "Pending Hicks/Ferro impl: <type>")]` until referenced types exist — they are intentional placeholders that will activate as types land):
- Models/TicketTests.cs — 7 facts (defaults, required-field validation, length cap)
- Webhooks/HmacSignatureTests.cs — 4 facts (known-fixture HMAC, payload/secret independence)
- Webhooks/TicketNumberGeneratorTests.cs — 5 facts (INC0010001 start, monotonicity, padding, regex)
- Api/CreateTicketEndpointTests.cs — 5 facts (201 + INC pattern, GET round-trip, 400 on missing fields)
- Api/GetTicketEndpointTests.cs — 3 facts (200 happy, 404 unknown, malformed input)
- Api/ListTicketsEndpointTests.cs — 5 facts (paging, page-2 distinct, state filter, unknown state, invalid pageSize)
- Webhooks/WebhookFiringTests.cs — 4 facts (one fire on success, post-commit ordering, zero on save failure, zero on validation failure)
- Components/TicketFormTests.cs — 4 facts (renders fields, validation, OnSubmit callback, busy-state)
- Components/PriorityBadgeTests.cs — Theory with 5 cases + 1 unknown-priority fact

Total: ~38 test methods scaffolded, all Skip-marked pending impl.

**Shared infra:**
- `TestWebAppFactory : WebApplicationFactory<Program>` — strips production AppDbContext registration, substitutes EF Core InMemory keyed per factory instance, swaps IWebhookDispatcher for FakeWebhookDispatcher.
- `FakeWebhookDispatcher` — concurrent queue records every Enqueue for ordering/count assertions.

**InMemory decision:** Per Jason's "skip local-dev story" directive, Phase 1 tests use EF Core InMemory provider. **This is not a faithful SQL Server substitute** — no real transactions, no unique-constraint enforcement, no rowversion concurrency. Phase 2 MUST add a SQL Server testcontainer (Testcontainers.MsSql) or Azure SQL ephemeral DB to validate ticket-number uniqueness, post-commit webhook ordering, and concurrency tokens against the real engine. Decision drop filed at `.squad/decisions/inbox/vasquez-test-strategy.md`.

**Edge case catalog** (`tests/EDGE_CASES.md`) — 35+ items grouped by Ticket Input, Ticket Numbering, Webhook Firing, Persistence/Concurrency, API Surface, Blazor Components, Observability, Phase 2 Test Infra. Highlights: oversized payloads, unicode/emoji round-trip, concurrent ticket creation race, webhook all-retries-fail terminal behaviour (spec is silent — flagged for Apone), HMAC secret rotation semantics, clock skew on CreatedAt ordering, SQL down on save.

**Coordination drops** to Hicks (DTO namespace `AgenticResolution.Web.Models.Dtos`, context name `AppDbContext`, dispatcher contract `IWebhookDispatcher.EnqueueAsync(WebhookEnvelope, CT)`) and Ferro (TicketForm `OnSubmit` EventCallback, PriorityBadge enum param + `.priority-{level}` classes) captured in the same decision drop.

**2026-04-29 — Phase 2 test-infrastructure swap plan drafted** — Authored `vasquez-phase2-sql-test-plan.md` recommending Testcontainers SQL Server over ephemeral Azure SQL DB; mapped 39 unskipped tests and fixture-upgrade pattern via `IAsyncLifetime`.
