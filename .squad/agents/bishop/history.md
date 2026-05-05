# Project Context

- **Owner:** Jason Farrell
- **Project:** agentic-resolution — Phase 2+ work focuses on Azure AI Search and Foundry Agents (with Agent Framework) consuming the webhook fired on ticket save, then either auto-resolving or escalating/assigning the ticket.
- **Stack:** Azure AI Search, Azure AI Foundry Agents, Microsoft Agent Framework, .NET
- **Phase 1 scope:** standby — no AI work until ticket→DB→webhook path is in place.
- **Created:** 2026-04-29

## Core Context

**Current state (2026-05-05):**
- Phase 2 AI pipeline architecture finalized with specialized decomposers (IncidentDecomposer + RequestDecomposer) replacing generic DecomposerAgent — better KB retrieval accuracy via type-specific question generation
- Question-driven resolution pipeline implemented: Classifier → Incident/RequestAgent (fetch) → DecomposerAgent (question-driven KB search) → EvaluatorAgent → Resolution/Escalation
- Hosted agents in Container Apps with `/invocations` (Foundry protocol) and `/health` endpoints; MCP server for ticket operations (get, search, update)
- Azure AI Search index `tickets-index` (14 fields, hybrid BM25+vector+semantic reranking, text-embedding-3-small 1536d) ready for seeding with 25 pre-resolved IT scenarios at gate G7
- No blocking schema or agent design questions remain; Bishop standby on Hicks's Bicep gates (G1–G7)

**Key decisions locked:**
- Single index, no multi-index KB corpus (Phase 3)
- Hybrid search: BM25 + vector similarity + semantic reranking; top 5 results to triage agent
- Incident vs Request dichotomy preserved through decomposition (not collapsed to generic)
- No tool-calling latency — search results pre-fetched by Function before agent eval

---

## Historical Details

## Phase 1 Architecture (Apone)

**Phase 1 baseline (your dependency):**
- Ticket form + POST /api/tickets working
- Tickets persisted to SQL (fields: Number, ShortDescription, Category, Priority, State, Caller, etc.)
- WebhookService fires async post-commit: HMAC-SHA256 signed payload with event_id, event_type, timestamp, ticket data
- Webhook receiver URL configurable via user-secrets (Phase 1) or Key Vault (Phase 2)

**Phase 2 starting point (your work):**
- Webhook receiver: Azure Function or Logic App
- Trigger: AI Search indexing + semantic query of ticket description
- Foundry Agent: consume webhook payload + search results → decision (auto-resolve, escalate, assign)
- Update ticket via PUT /api/tickets/{id} (endpoint to be added Phase 2)
- Feedback loop: agent results logged to Application Insights

**Out of scope Phase 1:** AI Search setup, Foundry agents, any ML code.

**Questions pending:** Webhook secret rotation strategy? Receiver preference (Function vs Logic App)?

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

### 2026-04-29 — Phase 2 Search Index Schema Finalized & Kickoff
- **Hybrid search strategy locked.** Single index `tickets-index` (14 fields) with text-embedding-3-small (1536d vector, cosine HNSW), BM25 full-text, and semantic reranking. Vectorization: concatenate [shortDescription + description + category]. Query strategy: BM25 + vector similarity, top 5 results reranked semantically. No cross-index complexity.
- **Seed data plan: 25 scenarios.** Pre-resolved IT ticket templates (password resets, VPN, Outlook, etc.) seeded into both SQL and index at gate G7 (after Bicep deploy, before agent code). Establishes historical knowledge base for triage agent to reference.
- **Semantic config defined.** `ticket-semantic` uses shortDescription as title field, [description, resolutionNotes] as content, [category, number] as keywords. Ensures semantic ranker surfaces business-relevant matches.
- **Bishop standby on gates.** Waiting for Hicks's G1 (Bicep), G2 (index exists), G3 (models working). Once complete, Bishop will seed index and write agent code. No blocking schema questions remain.
- **Architectural simplicity maintained.** No multi-index design, no KB-article corpus (Phase 3 work), no tool-calling latency in agents — search results pre-fetched by Function. Demo-first approach.

### 2026-04-29 — Phase 1 Scaffold Complete
- **Hicks** scaffolded .NET 10 Blazor Server backend with EF Core + SQL Server, Tickets API (POST/GET/LIST), webhook dispatcher (HMAC-SHA256, 3 retries 1s/4s/16s), and Bicep IaC.
- **Ferro** scaffolded Blazor UI: ticket list (25-row pagination), new-ticket form (free-text category, ServiceNow priority, Bootstrap CDN), read-only detail view.
- **Vasquez** scaffolded 39-test suite (xUnit + FluentAssertions) with InMemory EF provider for Phase 1, flagged SQL testcontainer swap as mandatory Phase 2 follow-up.
- **Jason's directive**: .NET 10 stack, skip local-dev story (no Docker SQL setup docs), INC-prefixed ticket numbers, static HMAC secret, free-text category.
- **Bishop standby continues** — waiting for end-to-end ticket→webhook fire to go green before Phase 2 AI work begins. Hicks/Ferro to reconcile Total/TotalCount DTO alignment before reviewer-gate begins.

### 2026-04-30 — Foundry Agent Wiring Complete

**What was built:**
- `src/AgenticResolution.Api/Agents/AgentRunResult.cs` — simple record for agent run outcomes
- `src/AgenticResolution.Api/Agents/AgentDefinitions.cs` — agent names, model (`gpt-41-mini`), and versioned system prompts as constants
- `src/AgenticResolution.Api/Agents/FoundryAgentService.cs` — full agent orchestration service
- Updated `Program.cs` — registers `AIProjectClient` (singleton) and `FoundryAgentService` (scoped)
- Updated `WebhookDispatchService.cs` — fires resolution agent as background task after each webhook event
- Updated `infra/modules/containerapp-api.bicep` — added `Foundry__ProjectEndpoint` and `TicketsApi__McpServerUrl` env vars

**Key SDK decisions:**
- Used `Azure.AI.Projects` 1.0.0-beta.9 for `AIProjectClient` (new endpoint format: `https://{account}.services.ai.azure.com/api/projects/{project}`)
- Used `Azure.AI.Agents.Persistent` 1.2.0-beta.8 for `PersistentAgentsClient` and `MCPToolDefinition` (not available in 1.1.0 or earlier)
- `PersistentAgentsClient` has no public constructor; obtained via `PersistentAgentsExtensions.GetPersistentAgentsClient(AIProjectClient)` extension method
- `MCPToolDefinition(serverLabel, serverUrl)` is native MCP integration — passes SSE endpoint URL directly to Foundry; no manual tool dispatch needed
- `RunStatus` is a struct with static property comparisons, use `.Equals()` not `==`
- Agent ID cached in `static ConcurrentDictionary<string, string>` shared across scoped service instances
- Write-back via direct `AppDbContext` (avoids circular dependency with McpServer project)
- Graceful degradation: if `Foundry__ProjectEndpoint` not configured, `AIProjectClient` resolves to null and agent runs skip with a warning

### 2026-05-01 — Classification + Routing Layer Added

**What was built:**
- `ClassificationAgent` (`ticket-classification-agent`) — first-stage agent that fetches the full ticket via `get_ticket_by_number`, reads short_description/description/category, and classifies as "incident" or "request" (defaults to incident when ambiguous). Emits `{ "classification", "confidence", "rationale" }` JSON block.
- `IncidentTicketAgent` (`ticket-incident-agent`) — handles incident tickets: searches for similar resolved incidents, auto-resolves at ≥0.8 confidence or escalates with `agent_action="escalate_incident"`.
- `RequestTicketAgent` (`ticket-request-agent`) — handles service requests: identifies standard request types (auto-queued → InProgress) or complex requests needing approval (→ OnHold).
- `RunAgentPipelineAsync` — new orchestration entry point in `FoundryAgentService`: classify → route → write-back.
- `Ticket.Classification` field (`nvarchar(20)`) added with EF Core migration `20260501000000_AddClassificationField`.
- `AgentRunResult.Classification` optional property added.
- `WebhookDispatchService` updated to call `RunAgentPipelineAsync` instead of `RunResolutionAgentAsync`.

**Key decisions:**
- Classification uses `RunAndPollRawTextAsync` (new helper) instead of `RunAndPollAsync` to preserve raw text for classification-specific JSON parsing (`ParseClassificationResponse`). This avoids losing the JSON block when keys differ from the standard summary format.
- `ParseClassificationResponse` normalises result to "incident" | "request" | null — null triggers fallback to `RunResolutionAgentAsync` (safety net).
- `WriteBackAsync` updated with switch expression for state transitions: `incident_auto_resolved` → Resolved, `request_auto_queued` → InProgress, `request_needs_approval` → OnHold, other high-confidence → Resolved, else leave unchanged.
- `TicketWebhookSnapshot` intentionally excludes `Description` — agents fetch full record via `get_ticket_by_number`; user message only passes lightweight identifiers.
- Existing `RunResolutionAgentAsync` and `RunTriageAgentAsync` preserved unchanged as fallback/advisory paths.

### 2026-05-04 — Migrated to Foundry Hosted Agents (3 Separate Containers)

**Context:**
- The deprecated `Azure.AI.Agents.Persistent` package has a broken `require_approval` feature on East US 2.
- Instead of polling/approval gates, we migrated to **Foundry Hosted Agents** — containerized C# Agent Framework code running inside Azure AI Foundry Agent Service as separate microservices.

**What was built:**
- **3 new hosted agent projects:**
  - `src/Agents.Classifier` — ticket classification agent (invocations protocol endpoint)
  - `src/Agents.Incident` — incident resolution agent
  - `src/Agents.Request` — service request agent
- Each project is an ASP.NET Core 9 minimal API with:
  - `POST /invocations` — invocation endpoint called by Foundry Agent Service or AgenticResolution.Api
  - `GET /health` — health check endpoint for Container Apps
  - Direct MCP server calls via HTTP (no approval gate, no SDK polling loop)
  - System prompts copied verbatim from `AgentDefinitions.cs`
  - Dockerfile for Container Apps deployment
- Updated `azure.yaml` with 3 new services: `classifier-agent`, `incident-agent`, `request-agent` (all hosted as `containerapp`)
- Added all 3 projects to `AgenticResolution.sln`

**Key SDK/tooling decisions:**
- `azd ai agent init` extension available (v0.1.29-preview) but too interactive for automation — manual scaffolding used instead
- **No Azure.AI.Projects dependency in hosted agents** — agents call MCP server directly via HTTP (JSON-RPC 2.0 format)
- **Simplified invocations protocol:** Each agent receives `{ "Message": "...", "SessionId": "..." }`, extracts ticket number via regex (`INC\d{7}`), calls MCP, returns JSON result
- **Rule-based logic for now** — classification uses simple keyword matching; in production, these agents will call Azure OpenAI with system prompts
- **No `Microsoft.Azure.AI.Agent.Server.*` packages** — those packages exist (`Microsoft.Azure.AI.Agent.Server.Core`, `Microsoft.Azure.AI.Agent.Server.Invocations`) but are beta and not required for simple invocations endpoints

**Architecture:**
- AgenticResolution.Api → POST to hosted agent Container App endpoints
- Hosted agents → GET ticket data from MCP server → classify/resolve/handle → return structured JSON
- MCP server → `get_ticket_by_number`, `search_tickets`, `update_ticket` tools
- All agents expose `/invocations` (Foundry protocol) and `/health` (Container Apps liveness)

**Next steps:**
- Add Bicep modules for 3 new Container Apps
- Wire AgenticResolution.Api to call hosted agent endpoints instead of deprecated SDK
- Replace rule-based logic with Azure OpenAI calls using system prompts

### 2026-05-04 — Question-Driven Resolution Pipeline Redesign

**Context:**
- The multi-agent resolution pipeline was failing to auto-resolve tickets accurately. Root cause: "dumb" KB search that used only the ticket's short description and picked the top result without understanding what specific information was needed to solve the problem.
- Test case failure: INC0010091 "VPN split tunneling misconfigured causing slow cloud app access" — system couldn't confidently match it to the "VPN Not Connecting" KB article because the search didn't capture the specific problem (split tunneling vs general connection).

**What was built:**
- **DecomposerAgent** (`agents/decomposer/__init__.py`) — New agent that performs question-driven KB retrieval:
  1. Understands the core problem from the ticket description
  2. Generates 2-4 specific answerable questions that would lead to a resolution
  3. Executes multiple targeted KB searches (one per question with specific search terms)
  4. Synthesizes KB search results into clear, specific answers
  5. Assigns preliminary confidence based on answer completeness
- **New message types** (`shared/messages.py`):
  - `TicketDetails` — Output from Incident/Request agents (ticket data only, no KB search)
  - `ResolutionQuestion` — Structured question with search terms, synthesized answer, and KB sources
  - `ResolutionAnalysis` — Full problem decomposition with core problem statement, questions array, and preliminary confidence
  - **REMOVED:** `KBSearchResult` (breaking change — no longer passing raw KB articles downstream)
- **Simplified Incident/Request agents** — Now only fetch ticket details via `get_ticket_by_number`; no KB search (that moved to DecomposerAgent)
- **Updated EvaluatorAgent** — Prompt rewritten to evaluate `ResolutionAnalysis` (questions + synthesized answers) instead of raw KB article content
- **Complete workflow rewrite** (`workflow/__init__.py`):
  - New flow: `Classifier → Incident/RequestAgent (fetch) → DecomposerAgent (question-driven KB search) → EvaluatorAgent → Gate → Resolution/Escalation`
  - Added `decompose_problem` executor with JSON parsing for questions array
  - Updated workflow edges: fetch agents → decomposer → evaluator → gate
- **DevUI update** (`devui_serve.py`) — Added `decomposer_agent` to served entities

**Key architectural decision:**
- **Single enriched agent approach** over separate agents per step. Rationale:
  - Agent Framework's native tool-calling loop allows one agent to iteratively call MCP tools (e.g., `search_knowledge_base` multiple times) in a single conversation
  - Reduces message serialization overhead at workflow boundaries
  - Natural LLM flow: "think → search → search → synthesize → evaluate" happens in one agent trace
  - Simpler workflow graph with fewer edges = easier debugging
- **Question-first retrieval** instead of search-first evaluation. The LLM explicitly articulates what needs to be known BEFORE searching, then searches with targeted queries.

**Prompt engineering highlights:**
- DecomposerAgent prompt includes:
  - Step-by-step workflow (Problem Understanding → Question Generation → Targeted KB Search → Answer Synthesis → Preliminary Confidence)
  - Concrete examples of good vs. bad questions ("What VPN settings control split tunneling?" vs. "How do I fix VPN issues?")
  - Explicit instruction to call `search_knowledge_base` MULTIPLE TIMES (not just once)
  - JSON output schema with questions array (question, search_terms, answer, kb_sources)
- EvaluatorAgent prompt simplified:
  - No longer receives raw KB content — receives structured analysis
  - Focus on answer completeness, solution coherence, and calibrated confidence
  - Stricter threshold guidance (0.90+ = high confidence, <0.50 = inadequate)

**Tradeoffs:**
- ✅ **Higher accuracy:** Targeted KB searches based on specific questions vs. blind top-1 retrieval
- ✅ **Better transparency:** Questions + answers logged, making failures easier to diagnose
- ✅ **Multi-source synthesis:** Can combine information from multiple KB articles (not just one)
- ❌ **Increased latency:** Multiple KB searches (2-4+) instead of single search = +5-10 seconds per ticket
- ❌ **Higher cost:** ~2x LLM token usage per ticket (decomposer does reasoning + multiple searches)
- ❌ **Complexity:** More sophisticated prompts = harder to debug hallucination or off-track reasoning

**Validation:**
- Workflow builds successfully: `python -c "from workflow import workflow; print(workflow)"` → OK
- All imports resolve correctly
- Message types parse correctly (dataclasses with `list` instead of `list[ResolutionQuestion]` for Python 3.8 compatibility)

**Success metrics to track after deployment:**
1. Auto-resolve rate (target: increase from ~40% to 60%+)
2. False positive rate (target: <5% of auto-resolved tickets re-opened)
3. Average questions generated per ticket (should be 2-4)
4. KB search calls per ticket (should be 2-4+, proving targeted retrieval)

**Future work:**
- Caching layer for common KB queries (reduce MCP server load)
- Question quality validation (meta-prompt to validate questions before searching)
- Feedback loop: log {ticket, questions, answers, confidence, actual_resolution_outcome} → fine-tune question generation
- Dynamic question count (let agent decide 1-5 questions based on problem complexity)

### 2026-05-05 — Split DecomposerAgent into IncidentDecomposer + RequestDecomposer

**Context:**
- The single `DecomposerAgent` used a generic question-generation approach that treated incidents and service requests identically. Incidents need failure-mode thinking (root cause, scope, recovery, validation); requests need fulfillment thinking (prerequisites, procedure, approval, verification). A single prompt cannot optimally serve both.

**Two-decomposer pattern:**

- **IncidentDecomposer** (`agents/incident_decomposer/__init__.py`) — diagnosis-oriented:
  - System prompt frames the problem as a failure to be diagnosed
  - Question archetypes: root cause, scope/blast radius, recovery/rollback, validation
  - Search strategy: symptom patterns, component names, error codes, "troubleshooting/incident" KB tags
  - Agent name: `IncidentDecomposer`

- **RequestDecomposer** (`agents/request_decomposer/__init__.py`) — fulfillment-oriented:
  - System prompt frames the problem as a service to be provisioned
  - Question archetypes: prerequisites, provisioning procedure, approval workflow, fulfillment verification
  - Search strategy: service/software/access name, onboarding procedures, approval workflows, "how-to/request-fulfillment" KB tags
  - Agent name: `RequestDecomposer`

**Workflow:**
- `IncidentFetchExecutor` → `IncidentDecomposerExecutor` → `EvaluatorExecutor`
- `RequestFetchExecutor` → `RequestDecomposerExecutor` → `EvaluatorExecutor`
- Both decomposers converge on `EvaluatorExecutor` unchanged — `ResolutionAnalysis` message type is unchanged
- Old `DecomposerExecutor` removed; old `decomposer/` agent directory deleted

**Rationale:**
- Incident questions ("What error code does this produce?") and request questions ("What approval is needed?") are semantically different; one agent prompt cannot be ideal for both
- Type-specific KB search framing surfaces more relevant articles (troubleshooting guides vs how-to guides)
- Cleaner workflow graph: routing intent (incident vs request) is preserved all the way through decomposition
- No message type changes — `ResolutionAnalysis` and `ResolutionQuestion` unchanged; EvaluatorAgent unchanged

**Escalation/handoff:** Both decomposers converge on the Evaluator, which retains full authority over final confidence and routing to Resolution or Escalation agents.


---

**📌 TEAM NOTE (2026-05-05) — .gitignore baseline established**  
Hicks added standard .NET .gitignore at repo root (commits 9c98efa, 7e121fd). `.squad/log/` is preserved (project docs). Build artifacts (`bin/`, `obj/`) are now ignored. Do NOT commit these directories going forward — .gitignore patterns are now active.
