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
