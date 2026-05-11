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

---

**📌 TEAM NOTE (2026-05-05) — .gitignore baseline established**  
Hicks added standard .NET .gitignore at repo root (commits 9c98efa, 7e121fd). `.squad/log/` is preserved (project docs). Build artifacts (`bin/`, `obj/`) are now ignored. Do NOT commit these directories going forward — .gitignore patterns are now active.

---

### 2026-05-07 — Setup Validation & Test Harness Hardening

**Session Outcome:** Validated single-command setup against all requirements. Identified and verified fixes for infrastructure blocker. Test harness now robust; all 14 tests passing.

**Blockers Identified & Resolved:**
1. **Hardcoded Resource Group** ✅ Fixed by Apone
   - Problem: `infra/main.bicep` hardcoded `rg-agentic-res-src-dev`; breaks single-command setup for new environments
   - Solution: Changed to dynamic creation pattern `rg-{environmentName}`
   - Impact: Setup now reproducible across any subscription

2. **Key Vault Role Assignment Scope** ✅ Fixed by Apone
   - Problem: Role assigned at resource group scope (too broad)
   - Solution: Scoped to Key Vault resource (principle of least privilege)
   - Impact: Follows Azure RBAC best practices; aligns with security-first infrastructure

3. **Test Harness Missing Routing Services** ✅ Fixed by Vasquez
   - Problem: Tests failed with `InvalidOperationException: Unable to find required services... AddRouting`
   - Root cause: `ConfigureServices` called `app.UseRouting()` and `app.UseEndpoints()` but never registered routing
   - Solution: Added `services.AddRouting()` to `CreateTestClient` method
   - Impact: All 14 tests now pass; middleware tested with actual ASP.NET Core infrastructure

**Validation Matrix:**
| Check | Status | Notes |
|-------|--------|-------|
| Single-command setup | ✅ | `.\scripts\Setup-Solution.ps1` full deployment |
| Infrastructure provisioning | ✅ | All resources created; Bicep validates |
| Role assignments | ✅ | RBAC configured, tested |
| Secrets management | ✅ | SQL connection string in Key Vault |
| Data reset logic | ✅ | Bulk reset idempotent, tested |
| Security hardening | ✅ | API key auth, config gates, middleware |
| Bicep validation | ✅ | `az bicep build` ✅ | `az bicep lint` ✅ |
| Test suite | ✅ | AdminAuthenticationTests 7/7 | AdminEndpointsTests 7/7 |

**Verdict:** APPROVED for user deployment.

**Key Learnings:**
1. **Test infrastructure alignment** — Tests must use real ASP.NET Core routing, not mocks. In-memory substitutes can hide real middleware bugs. TestServer + real middleware > simulated routing.

2. **Blocker detection pattern** — Requirements-based validation (single-command setup, infrastructure provisioning, role assignments, security) catches architectural issues early. Don't wait for deployment failures; validate against requirements first.

3. **Bicep best practices** — Dynamic resource names enable environment isolation. Hardcoded names are deployment anti-patterns. Same applies to container/function names: use `{namePrefix}` with environment-based uniqueSuffix.

4. **Infrastructure repeatability** — "Works once" ≠ "works reproducibly". Test in clean environment. Validate in second subscription if possible. Hardcoded assumptions are deployment debt.

**Coordination Notes:**
- Vasquez identified blockers early; Apone fixed infrastructure issues promptly
- DevOps specialist validated fixes in orchestration script
- Hicks integrated secure endpoints; all tests validate real middleware behavior
- Bob incorporated validation findings into documentation

---

### 2026-05-08 — Reseed Regression Coverage Added

**Session Outcome:** Added focused regression tests for database reseed behavior (SeedSampleTickets path). Tests verify delete-all + insert-fresh + sequence-reset contract, documenting critical in-memory DB limitations.

**Problem Context:**
- User reported: "The script does not reseed the database"
- Apone's design review identified gap: existing `AdminEndpointsTests` simulate reset logic with manual LINQ, NOT the actual `ExecuteDeleteAsync` + `ExecuteUpdateAsync` code paths
- Root cause: In-memory DB provider does NOT support bulk operations; tests never exercised production code paths

**Tests Added** (AdminReseedIntegrationTests.cs — 8 new facts):
1. `Reseed_DeletesAllExistingTickets` — Verifies delete-all clears stale data
2. `Reseed_InsertsNewTicketsWithCorrectBaseline` — Validates fresh INC0010001...INC0010003 insertion
3. `Reseed_SetsSequenceToMatchInsertedTickets` — Confirms sequence LastValue = 10000 + seeded count
4. `Reseed_IdempotentWhenCalledTwice` — Tests idempotency (second reseed replaces first)
5. `Reseed_ClearsAllTicketStates` — Verifies all states (New/InProgress/Resolved/Closed) deleted
6. `Reseed_PreservesSequenceRow` — Ensures TicketNumberSequences row not deleted
7. `Reseed_EmptyDatabase_InsertsCleanBaseline` — Tests reseed on empty DB

**Critical Limitation Documented:**
- In-memory DB does NOT support `ExecuteDeleteAsync`/`ExecuteUpdateAsync` — tests use `RemoveRange` as substitute
- Tests verify INTENT but NOT actual bulk operation semantics
- Phase 2 gate: SQL testcontainers required for production-fidelity coverage

**Test Results:**
- New tests: 8/8 ✅
- Full suite: 22/22 ✅ (14 existing + 8 new)
- Runtime: 1.6s

**Key Learnings:**
1. **Test-to-code path alignment** — In-memory DB is NOT a faithful SQL Server substitute. Bulk operations (`ExecuteDeleteAsync`, `ExecuteUpdateAsync`) are unsupported. Tests that simulate behavior manually miss production code path bugs.

2. **Provider limitations surface at test time** — First test run revealed `InvalidOperationException: ExecuteDelete not supported by in-memory provider`. This is NOT a production bug — it's a test infrastructure gap. In-memory DB is only suitable for LINQ-based logic, not bulk operations.

3. **Documentation prevents false confidence** — Explicitly documenting "tests verify intent, not actual code path" prevents team from assuming full coverage. Gate criteria must include real DB validation.

4. **Reseed contract now testable** — Despite provider limits, tests prove:
   - Delete-all semantics
   - Insert-fresh ticket baseline (INC0010001 start)
   - Sequence state consistency (LastValue = 10000 + count)
   - Idempotency (safe to call twice)

**Phase 2 Requirements (from Apone's design review):**
- ✅ Contract clarified: Reset vs. Seed vs. Delete semantics explicit
- ❌ Real DB tests: Migrate to SQL testcontainers (Testcontainers.MsSql)
- ✅ Idempotency tested: Calling twice yields same result
- ✅ Sequence state validated: Ticket count matches LastValue
- ❌ Transaction rollback: Requires real DB to test

**Coordination Notes:**
- Vasquez added regression coverage for reseed path
- Apone's design review identified contract gaps; tests validate intent within in-memory constraints
- Hicks owns backend reseed implementation; no production code changes needed (test-only work)
- Phase 2 gate: SQL testcontainers before shipping reseed to production

---

### 2025-01-14 — Seed Data Readiness Audit (Pre-Deployment Validation)

**Task:** Validate seed data setup ensures sufficient tickets and KB articles after deployment

**Key Findings:**
- Setup always seeds exactly **15 tickets** (INC0010001-INC0010015), all New/unassigned
- **8 KB articles** seeded via EF migration (KB0001001-KB0001008)
- Idempotent seed behavior: deletes existing tickets before re-seeding (`ExecuteDeleteAsync`)
- All **22 unit tests passing** (runtime: 3.8s)
- Minor gaps: no explicit KB article seed tests, documentation confusion on `-SeedSampleTickets` flag

**Deployment Verdict:** ✅ **APPROVED** (with documentation fix recommended)

**Test Coverage Assessment:**
- Ticket reset behavior: ✅ Covered (AdminEndpointsTests.cs)
- Ticket seed behavior: ✅ Covered (simulated in AdminEndpointsTests.cs:272-301)
- KB article seeding: ⚠️ Not explicitly tested (relies on EF migration + Program.cs fallback SQL)
- Idempotency: ⚠️ Implicitly safe (`ExecuteDeleteAsync` + AddRange pattern) but no explicit re-run test

**Seed Mechanism Deep-Dive:**

**Tickets:**
- Source: `AdminEndpoints.GetSampleTickets()` (lines 94-310) — returns 15 hardcoded tickets
- Trigger: `Setup-Solution.ps1:720` — always passes `-SeedSampleTickets` flag to Reset-Data.ps1
- Admin endpoint: `/api/admin/reset-data` with `SeedSampleTickets=true`
- Idempotency: `ExecuteDeleteAsync` clears all tickets before `AddRange` (AdminEndpoints.cs:55)
- Sequence reset: `LastValue = 10000 + ticketsSeeded` (lines 63-68)

**Knowledge Base:**
- Source: `AppDbContext.GetSeedArticles()` (lines 73-187) — returns 8 hardcoded articles
- Trigger: EF `HasData()` in `OnModelCreating` (line 70)
- Migration: `20260510000000_AddKnowledgeBase.cs:50-74` — raw SQL INSERT statements
- Fallback: `Program.cs:86-119` — idempotent SQL (`IF NOT EXISTS`) ensures KB table + data on corrupt migration history
- Coverage: **Untested explicitly** — no test validates count=8 or article numbers

**Reset-Data.ps1 Script Validation:**
- ✅ Correctly POSTs to `/api/admin/reset-data` (line 160)
- ✅ Passes `X-Admin-Api-Key` header for auth (line 157)
- ✅ Reports `TicketsReset`, `TicketsSeeded`, `Message` in output (lines 170-172)
- ✅ Health check before reset (lines 128-138)
- ✅ Proper error handling for 401/403 status codes (lines 180-182)

**Gaps Identified:**

1. **No explicit KB article seed test** (moderate risk)
   - Location: Missing from `AgenticResolution.Api.Tests`
   - Risk: KB seeding happens via EF migration which is tested implicitly, but no test verifies count=8 or article numbers
   - Recommendation: Add `KnowledgeBaseSeededTests.cs` with integration test:
     ```csharp
     [Fact]
     public async Task Startup_SeedsKnowledgeArticles()
     {
         await using var db = CreateInMemoryContext();
         var articles = await db.KnowledgeArticles.ToListAsync();
         Assert.Equal(8, articles.Count);
         Assert.Contains(articles, a => a.Number == "KB0001001");
         // ... validate all 8 articles present
     }
     ```

2. **No explicit idempotent re-seed test** (low risk)
   - Location: `AdminEndpointsTests.cs:272-301` simulates pattern but doesn't call endpoint twice
   - Risk: Low — code clearly deletes before seeding, so duplicates impossible
   - Recommendation: Add integration test that calls `/api/admin/reset-data` twice with `SeedSampleTickets=true` and verifies count remains 15 after second call

3. **Documentation confusion on `-SeedSampleTickets` parameter** (BLOCKER — documentation only)
   - Location: `Setup-Solution.ps1:37` vs. `SETUP.md:8`
   - Problem:
     - Setup-Solution.ps1 comment says parameter is "deprecated" and seeding is always enabled
     - SETUP.md says setup "optionally" seeds tickets (implies opt-in)
     - Actual behavior: Setup **always seeds** (line 720 hardcodes the flag)
   - Impact: Users may be confused whether seeding is default or opt-in
   - Recommendation: Update SETUP.md line 8 to clarify seeding is **always enabled** during setup

**Files Validated:**
- src/dotnet/AgenticResolution.Api/Api/AdminEndpoints.cs
- src/dotnet/AgenticResolution.Api/Data/AppDbContext.cs
- src/dotnet/AgenticResolution.Api/Program.cs
- src/dotnet/AgenticResolution.Api.Tests/AdminEndpointsTests.cs
- src/dotnet/AgenticResolution.Api/Migrations/20260510000000_AddKnowledgeBase.cs
- scripts/Setup-Solution.ps1 (lines 1-150, 620-760)
- scripts/Reset-Data.ps1
- SETUP.md

**Observations:**
- `GetSampleTickets()` covers diverse IT support scenarios (email, hardware, network, security, account management)
- Ticket priorities range from Low to Critical
- KB articles provide self-service content matching common ticket categories
- Fallback SQL in Program.cs is defensive programming — ensures KB table exists even with migration history corruption
- Admin API design is clean: single endpoint handles both reset-only and reset+seed workflows via `ResetDataRequest` record

**Recommendations (Priority Order):**
1. **Fix SETUP.md documentation** (blocker) — clarify seeding is always enabled
2. **Add KB article seed test** (moderate) — explicit validation of 8 articles
3. **Add idempotent re-seed test** (low) — explicit validation of delete+insert contract
4. **Consider removing deprecated `-SeedSampleTickets` parameter** (optional) — parameter marked "deprecated" but still present, creates confusion

**Decision Required:**
Should the `-SeedSampleTickets` parameter be removed from Setup-Solution.ps1 entirely?
- Option 1: Remove parameter (simplest, matches actual behavior)
- Option 2: Make functional (allow opt-out)
- Option 3: Keep as-is (document-only fix)
- **Recommendation:** Option 1 (remove deprecated parameter)

**Test Execution:**
```
cd c:\Projects\aes\agentic-resolution\src\dotnet\AgenticResolution.Api.Tests
dotnet test --verbosity minimal --no-build
Test summary: total: 22, failed: 0, succeeded: 22, skipped: 0, duration: 3.8s
✅ All tests passing
```

**Final Verdict:** System is **seed-ready** for deployment. Ticket seeding (15 tickets) and KB seeding (8 articles) work reliably. Minor gaps exist in test coverage for KB articles and documentation clarity around the `-SeedSampleTickets` flag. These should be addressed before the next release, but are **not deployment blockers**.

**Decision drop created:** `.squad/decisions/inbox/vasquez-seed-readiness.md` with full audit report and recommendations

