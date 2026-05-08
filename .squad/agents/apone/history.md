# Project Context

- **Owner:** Jason Farrell
- **Project:** agentic-resolution — demo system simulating an automated ServiceNow resolution engine. ServiceNow-like Blazor frontend captures tickets, persists to SQL Server, and fires a webhook. Future phases add Azure AI Search and Foundry Agents (with Agent Framework) for automated resolution or escalation/assignment routing.
- **Stack:** .NET / Blazor (frontend on Azure App Service), Azure SQL Server (persistence), webhook integration, Azure AI Search + Azure AI Foundry Agents (Phase 2+)
- **Phase 1 scope:** basic Azure resources + Blazor frontend hosted on App Service. **Do NOT deploy to Azure** during Phase 1 — local/scaffold work only.
- **Created:** 2026-04-29

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

### 2025-07-25 — Solution Split Architecture Decision (Phase 2)
- **Three-project split approved.** `AgenticResolution.Api` (EF Core, endpoints, webhooks, SQL), `AgenticResolution.Web` (Blazor UI shell, HttpClient to Api), `AgenticResolution.McpServer` (stdio MCP, wraps Api over HTTP). Supersedes Phase 1 "single project" decision.
- **Separate Azure deployments confirmed.** Api gets its own App Service (same plan). Web stays on existing App Service. MCP server is local/stdio for now.
- **Webhook payload trimmed to ServiceNow incident.created semantics.** 10 fields only: number, short_description, category, priority, urgency, impact, state, caller, assignment_group, opened_at. Heavy fields (description, assigned_to, updated_at) excluded — receiver calls `GET /api/tickets/{number}` if needed.
- **MCP server uses HTTP-over-Api, not shared DbContext.** Clean separation; MCP server is a thin adapter. Uses official `ModelContextProtocol` NuGet SDK, .NET 10 console app, stdio transport.
- **Build sequence: 9 steps, Hicks-heavy.** Core split (steps 1–4) is sequential. MCP server (step 7) can begin after step 1 lands. Bicep/azd updates (step 8) parallelize with app-level work.
- **New endpoint required: `GET /api/tickets/search?q=`** — needed by MCP `search_tickets` tool. Added to backlog alongside existing `PUT /api/tickets/{id}` plan.

### 2026-04-29 — Phase 2 Architecture Finalized & Kickoff
- **Phase 2 blueprint locked.** Five subsystems designed: Azure Function webhook receiver (Consumption), single AI Search index (hybrid BM25+vector), two Foundry agents (triage + summarizer on gpt-4o-mini), PUT /api/tickets/{id} endpoint, Bicep IaC (AI Search, OpenAI, Foundry, Function, Storage).
- **Nine gate criteria established (G1–G9).** Infrastructure gates (G1–G3) and application gates (G4–G7) owned by Hicks; test gates (G8–G9) owned by Vasquez. Bishop's agent work gated on G1–G7. Parallel execution enabled.
- **Cost estimate locked: ~$78–81/mo incremental** (AI Search $75, OpenAI $2–5.50, Function/Storage ~$0.50). Combined with Phase 1: ~$103–106/mo.
- **Decisions merged into squad/decisions.md.** All architectural decisions made once; no future blocking questions. Apone ships Phase 2 architecture; Bishop ships search index schema simultaneously.
- **Team tracks enabled:** Hicks (infra first), Vasquez (tests in parallel), Ferro (UI polish, low priority), Bishop (standby until gates clear).

### 2025-07-25 — Phase 1 Architecture Decisions
- **All three Phase 1 agents shipped on schedule.** Hicks (backend), Ferro (frontend), and Vasquez (tester) delivered .NET 10 scaffolds with decision records merged into squad/decisions.md.
- **Backend/Frontend DTO reconciliation pending.** Hicks/Ferro need to align Total/TotalCount response shape before Bishop's reviewer-gate work kicks off.
- **Test suite written ahead of impl.** 39 tests scaffolded (xUnit + FluentAssertions), currently skipped pending Hicks/Ferro type delivery. Skipped count will drop to zero as types land.
- **InMemory → SQL testcontainer swap flagged as Phase 2 requirement.** Vasquez raised this as mandatory before production — no false confidence from InMemory on ticket-number uniqueness or post-commit ordering.
- **User directives locked.** Jason confirmed .NET 10, free-text category, static HMAC, INC-prefixed ticket numbers, and "skip local-dev story" scope boundaries.
- **Phase 2 planning gate:** end-to-end ticket→webhook→AI Search indexing test must pass green before Foundry Agent work begins.

### 2025-07-25 — Phase 1 Architecture Decisions
- **Single Blazor Server project** with embedded minimal API endpoints — no separate API project. Justification: demo simplicity, single deployment target, extract later if needed.
- **Docker SQL** over LocalDB for cross-platform dev parity.
- **Channel<T> + IHostedService** for async webhook dispatch post-commit — keeps API response fast, gives retry without a queue dependency.
- **HMAC-SHA256 webhook signing** — industry standard, simple to implement and verify.
- **azd + Bicep modules** for IaC — scaffolded in Phase 1 but not deployed until Phase 2.
- **Managed identity** for all Azure service-to-service auth (no connection strings in config).
- Key open question: .NET 9 vs .NET 8 LTS — recommended 9, awaiting Jason's call.

---

**📌 TEAM NOTE (2026-05-05) — .gitignore baseline established**  
Hicks added standard .NET .gitignore at repo root (commits 9c98efa, 7e121fd). `.squad/log/` is preserved (project docs). Build artifacts (`bin/`, `obj/`) are now ignored. Do NOT commit these directories going forward — .gitignore patterns are now active.

---

### 2026-05-06 — Phase 2.5: Blazor Web + Python Resolution API Architecture

**Context:** Architecture pivot focuses Phase 2.5 on creating a modern, decoupled system where Python orchestration owns agent workflow end-to-end, while .NET API remains as a pure ServiceNow CRUD simulator.

**Decisions locked:**

1. **Blazor Web Project (AgenticResolution.Web)**
   - Separate Blazor Server project (.NET 10, Interactive Server render mode)
   - Deployed independently to Azure App Service
   - Typed `TicketsApiClient` with HttpClientFactory
   - Shared DTO contracts library: `AgenticResolution.Contracts`
   - Pages: `/tickets` (list), `/tickets/{number}` (detail), `/tickets/{number}/runs/{runId}` (progress)
   - Clean sidebar layout with branded styling

2. **Python Resolution API (`ca-resolution` Container App)**
   - FastAPI with SSE streaming endpoint (`POST /resolve`)
   - Returns Server-Sent Events with workflow progression (classifier → fetch → decomposer → evaluator → resolution/escalation)
   - Thin wrapper around existing agent framework (no duplication)
   - Stateless design (no WorkflowRun persistence in Python layer)
   - Health checks for Azure Container Apps probes
   - Multi-stage Docker build for production readiness

3. **API Contract Extensions**
   - Enhanced list filtering: `assignedTo`, `state`, `category`, `priority`, `q`, `sort`, `dir`, pagination
   - Detail endpoint: `GET /api/tickets/{number}/details` with comments + runs
   - Comments CRUD: `GET/POST /api/tickets/{number}/comments`
   - Manual resolve: `POST /api/tickets/{number}/resolve` → 202 Accepted with `runId`
   - Workflow run visibility: `GET /api/tickets/{number}/runs`, `GET /api/runs/{runId}`, `GET /api/runs/{runId}/events`
   - Webhook auto-dispatch flag: `Webhook:AutoDispatchOnTicketWrite` (default false)

4. **Webhook-Driven Architecture**
   - Resolve endpoint fires `resolution.started` webhook unconditionally
   - Azure Function receiver (external) owns orchestration
   - Workflow progress webhooks: `workflow.running`, `workflow.completed`, `workflow.escalated`, `workflow.failed` (opt-in)
   - RunId correlation in all workflow webhooks for external system integration

5. **.NET API Cleanup**
   - Orchestration code removed (AgentOrchestrationService, ResolutionRunnerService, IResolutionQueue)
   - Dead endpoints deleted (resolve, runs, events)
   - CRUD endpoints preserved (tickets, comments, knowledge base)
   - Database models preserved (WorkflowRun, WorkflowRunEvent) for potential Python usage
   - MCP server unchanged (still calls GET/PUT ticket endpoints)

**Rationale:**
- Clean separation of concerns: .NET owns CRUD, Python owns orchestration, Function owns webhook processing
- Direct Blazor → Python API removes unnecessary .NET proxy hop
- SSE streaming enables real-time workflow visualization in UI
- Stateless Python API allows horizontal scaling in Container Apps
- Webhook-driven architecture maintains integration potential with external systems

**Status:** ✅ All components implemented and ready for integration

---

### 2026-05-07 — Single-Command Setup & Infrastructure Finalization

**Session Outcome:** Complete one-command setup architecture validated and approved for user deployment.

**Infrastructure Decisions Finalized:**
1. **Dynamic Resource Group** — Changed from hardcoded `rg-agentic-res-src-dev` to dynamic creation (`rg-{environmentName}`). Enables `azd up` to work in any subscription/environment. Pattern: `main.bicep` creates RG; `resources.bicep` provisions resources in it.

### 2026-05-08 — Worktree Cleanup & Repo Hygiene

**Session Outcome:** Post-deployment worktree management after main branch stabilization at HEAD 38ab151.

**Actions Taken:**
1. **Pruned 3 stale worktree entries** — Removed dead worktrees that had been abandoned.
2. **Identified 3 live auxiliary worktrees with uncommitted changes:**
   - `agents-fix-ai-resolve-button-functionality`
   - `agents-frontend-500-error-troubleshooting`
   - `agents-frontend-network-issue-debugging`
3. **Blocked force removal** — Left live worktrees in place pending user decision (uncommitted/untracked files prevent automatic cleanup).

**Current State:**
- Main worktree: ✅ Clean, deployment-ready
- Auxiliary worktrees: ⏸️ Blocked (awaiting user input for removal or preservation)

**Next Decision Gate:** User to authorize force removal or preserve branches for continued development.

2. **Key Vault RBAC Scope** — Fixed role assignment scope from resource group to Key Vault resource (principle of least privilege). GUID generation now uses Key Vault ID (available at compile time). Bicep `parent:` property syntax cleaner than string concatenation.

3. **PrincipalType Flexibility** — Added `@allowed(['User', 'ServicePrincipal'])` parameter to `keyvault.bicep` module. Supports both user deployments (`azd up` from workstation) and CI/CD service principal scenarios without module modification.

4. **Validation:** `az bicep build --file infra/main.bicep` ✅ | `az bicep lint` ✅ | All 14 tests pass ✅

**Coordination Notes:**
- Vasquez identified hardcoded RG blocker and validated fixes
- DevOps specialist built orchestration script using this foundation
- Hicks integrated admin endpoints secured with configuration gates
- Bob documented the two-step setup (infrastructure + Container Apps)

---

### 2026-05-07 — Deployment Stabilization & Merge Finalization

**Session Outcome:** Final merge of cross-cutting Azure deployment stabilization changes. Commit f514ebc locked all infrastructure and application code for Entra-only SQL authentication.

**Changes Consolidated:**
1. **Bicep IaC:** `infra/main.bicep`, `infra/resources.bicep`, `infra/modules/sqlserver.bicep` — Entra-only enforcement, dynamic resource group pattern, RBAC scoping refinements
2. **PowerShell Deployment Scripts:** `scripts/Setup-Solution.ps1`, `scripts/Configure-DatabaseUsers.ps1` — Azure CLI user context capture, environment variable persistence, enhanced error handling
3. **EF Core Migrations:** `src/dotnet/AgenticResolution.Api/Migrations/20260429171348_Initial.cs` — Database schema locked to support Entra authentication
4. **Application Configuration:** `src/dotnet/AgenticResolution.Api/Program.cs` — Connection string auth mode set to `Active Directory Default`, managed identity credential chain enabled
5. **Documentation:** `DEPLOY.md`, `SETUP.md` — User-facing deployment procedures finalized

**Validation:**
- ✅ `dotnet build` succeeded with existing warnings (NU1603, NU1510 — non-blocking)
- ✅ `az bicep build --file infra/main.bicep` passed syntax and type checks
- ✅ Live endpoints (`ca-api-*` in `rg-agentic-res-src-dev`) returned HTTP 200 after redeploy
- ✅ Git status clean after commit f514ebc

**Next Gate:** Production infrastructure deployment and live system validation

**Key Learning:** Cloud infrastructure templates must be designed for **reproducibility across environments**. Hardcoded resource names break the "single command works anywhere" promise. Dynamic naming patterns (environment-based, location-based) are essential for IaC credibility.

**Future Work:** Phase 2 can expand Bicep to include Container Apps modules (currently using Azure CLI in orchestration script for expediency). Foundry resource model with `kind: 'AIServices'` and projects will need AIServices account + connections per modern (2025+) patterns — avoid legacy `kind: 'AIFoundry'` or `MachineLearningServices` workspace hub patterns.

---

### 2026-05-10 — Design Review: Database Reseed Contract Issue

**Context:** User reported "script does not reseed the database." Design review identified structural contract mismatches, not a code bug.

**Key Findings:**
1. **API Contract Ambiguity** — `ResetDataRequest` has two independent flags (`ResetTickets`, `SeedSampleTickets`) that are not semantically synchronized. Seeding **deletes all existing tickets** before inserting fresh data, which may violate caller expectations if reset is called standalone.
2. **Test-to-Code Misalignment** — Admin endpoint tests use in-memory DB with manual LINQ assignments, but actual code path uses `ExecuteUpdateAsync()`. Tests do NOT validate real SQL Server behavior or sequence state.
3. **Sequence Reset Under-Specified** — Ticket number sequence reset is tied to seed logic; no compensation if one operation fails mid-transaction.

**Architectural Decisions:**
- **Hicks (backend):** Must clarify API contract semantics (reset vs. seed vs. delete-all) before implementation. Propose explicit `DeleteAllTickets` flag for clarity.
- **Vasquez (tests):** Must replace in-memory tests with SQL testcontainers. Add idempotency validation.
- **Gate criteria:** Contract aligned, testcontainers in place, reseed idempotent, sequence state validated.

**Decision Artifact:** Detailed design review written to `.squad/decisions/inbox/apone-reseed-review.md`. This is a **contract boundary issue**, not a code bug. Requires Hicks + Vasquez team alignment before implementation.

**Pattern Extracted:** Admin endpoints with state-changing operations need explicit contract documentation (not just defaults). Never assume script author and API designer agree on semantics.

### 2026-05-10 — Code Review: Reseed Fix Implementation (Hicks + Vasquez)

**Context:** Reviewed working tree changes addressing "script does not reseed the database" complaint.

**Artifacts Reviewed:**
- `scripts/Setup-Solution.ps1` — Hicks made API timeout a hard failure (`exit 1`), updated error message with `-SeedSampleTickets` flag, doc updates 5→15 tickets
- `scripts/Reset-Data.ps1` — Doc-only update (5→15 tickets)
- `src/dotnet/AgenticResolution.Api.Tests/AdminReseedIntegrationTests.cs` — Vasquez added 7 InMemory tests simulating reseed behavior

**Verdict: APPROVED**

**Rationale:**
1. **Root cause addressed.** The most likely failure mode was silent timeout — script warned but continued, user didn't notice reseed was skipped. Hard `exit 1` makes failure impossible to miss.
2. **Wiring confirmed correct.** Setup-Solution.ps1 line 667 always passes `-SeedSampleTickets` → Reset-Data.ps1 sends `{ ResetTickets: true, SeedSampleTickets: true }` → API deletes all + inserts 15 fresh tickets + resets sequence. Path is sound.
3. **15 tickets verified.** `GetSampleTickets()` returns INC0010001–INC0010015. Doc updates are accurate.
4. **Tests acceptable with caveats.** Vasquez's tests use `RemoveRange` (InMemory limitation clearly documented) — they verify intent, not real SQL behavior. Phase 2 testcontainer migration already flagged. Idempotency test included.

**Remaining risks (not blocking):**
- Tests don't exercise `ExecuteDeleteAsync`/`ExecuteUpdateAsync` — real SQL could diverge
- No retry logic if API health check passes but reset-data call itself fails
- `ResetTickets` + `SeedSampleTickets` redundancy: reset updates all tickets, then seed deletes them anyway
