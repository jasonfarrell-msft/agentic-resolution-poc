# Project Context

- **Owner:** Jason Farrell
- **Project:** agentic-resolution — Phase 2+ work focuses on Azure AI Search and Foundry Agents (with Agent Framework) consuming the webhook fired on ticket save, then either auto-resolving or escalating/assigning the ticket.
- **Stack:** Azure AI Search, Azure AI Foundry Agents, Microsoft Agent Framework, .NET
- **Phase 1 scope:** standby — no AI work until ticket→DB→webhook path is in place.
- **Created:** 2026-04-29

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
