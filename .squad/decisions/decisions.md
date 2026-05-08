# Project Decisions — Agentic Resolution

**Last Updated:** 2026-05-07  
**Status:** Active decisions (deduplicated, consolidated from .squad/decisions/inbox/)

---

## Table of Contents
1. [Foundry Resource Types: Modern (AIServices)](#foundry-modern-resource-type)
2. [Webhook Receiver: Container App](#webhook-container-app)
3. [AI Services & Foundry Connection](#aiservices-foundry-connection)
4. [Classification & Routing Layer](#classification-routing)
5. [Foundry Agent Wiring (Deprecated SDK)](#foundry-agent-wiring-deprecated)
6. [Hosted Agent Containers (Current)](#hosted-agent-containers)
7. [MCP Server Design](#mcp-server-design)
8. [Phase 2 Bicep IaC](#phase-2-bicep)
9. [Resolution Pipeline: Question-Driven KB](#resolution-pipeline-redesign)
10. [Ticket Loading Configuration Fix](#ticket-loading-configuration-fix)
11. [Directives from Leadership](#leadership-directives)
12. [Azure SQL Entra-Only Authentication (MCAPS Compliance)](#sql-entra-auth)
13. [Entra Auth Verification - No Code Changes Needed](#entra-auth-verification)
14. [Azure OpenAI Data-Plane RBAC for Resolution API](#azure-openai-rbac)

---

## Foundry Modern Resource Type

**Date:** 2026-04-29  
**Author:** Hicks (Backend Dev)  
**Status:** ✅ Implemented

### Context
`infra/modules/foundry.bicep` was using deprecated ML Workspaces approach (`kind: 'Hub'`, `kind: 'Project'` for ML workspaces). Updated to modern 2025+ resource types.

### Investigation Result
- `kind: 'AIFoundry'` **does NOT exist** as a CognitiveServices account kind
- `Microsoft.CognitiveServices/accounts/projects` **DOES exist** (GA at `2025-06-01`)
- **Modern pattern:** `AIServices` account with `allowProjectManagement: true` IS the Foundry hub

### Decision
Modern Foundry = `AIServices` account + `accounts/projects` children (no separate hub resource):

```bicep
# Hub: AIServices account
resource aiServicesAccount 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
  kind: 'AIServices'
  properties: {
    allowProjectManagement: true
  }
}

# Project: Child resource of AIServices
resource foundryProject 'Microsoft.CognitiveServices/accounts/projects@2025-06-01' = {
  parent: aiServicesAccount
}
```

### Changes
- `infra/modules/foundry.bicep` — complete rewrite (removed ML Workspace resources)
- `infra/modules/openai.bicep` — updated API version to `2025-06-01`, added `allowProjectManagement: true`
- `infra/resources.bicep` — simplified foundry module params

### Why ML Workspaces Hub/Project Deprecated
Azure AI Foundry originally ran on ML Workspaces. As of 2025, Microsoft rebuilt Foundry as native CognitiveServices capability. New model is simpler: one AIServices account, multiple native projects, no separate hub resource.

---

## Webhook Receiver: Container App

**Date:** 2026-04-29  
**Author:** Hicks (Backend Dev)  
**Direction:** Jason Farrell

### Decision
Replaced Azure Consumption Function App with **Container App** for webhook receiver.

### Why
- Container-native model better fits standalone webhook receiver
- Supports any runtime
- Simpler CI/CD (build image, push on commit)
- No Azure Storage dependency

### Key Differences
| Concern | Function App | Container App |
|---------|--------------|---------------|
| KV Secrets | App Service reference syntax (`@Microsoft.KeyVault(...)`) | Container App secrets array + keyVaultUrl |
| Storage | Required `AzureWebJobsStorage` | No storage dependency |
| Host | .NET 8 isolated Functions | Placeholder image (replaced on build) |
| Scale to zero | Yes (Consumption Y1) | Yes (minReplicas: 0) |
| Max replicas | Functions runtime | maxReplicas: 3 |

### Modules Added
- `infra/modules/containerappenvironment.bicep` — CAE backed by Log Analytics
- `infra/modules/containerapp.bicep` — External ingress port 8080, 0.25 CPU / 0.5Gi

### Modules Removed
- `infra/modules/functionapp.bicep`

### Future Steps
1. Build webhook receiver app (ASP.NET Core minimal API, port 8080)
2. Push image to container registry
3. Update container image reference in Bicep
4. Populate `webhook-hmac-secret` in Key Vault

---

## AI Services & Foundry Connection

**Date:** 2026-04-29  
**Author:** Hicks (Backend Dev)

### Decision
Migrate from standalone `kind: 'OpenAI'` to Foundry-integrated `kind: 'AIServices'`. Connect AI Services account to Foundry hub via `accounts/connections` resource.

### Rationale
- Standalone Azure OpenAI (`kind: 'OpenAI'`) is deprecated
- Modern approach: Azure AI Foundry integrated AI Services
- Connection enables Foundry hub to resolve model deployments for agents
- `authType: 'AAD'` ensures Managed Identity access (no key-based auth)

### Changes
- `infra/modules/openai.bicep` — `kind: 'OpenAI'` → `kind: 'AIServices'`
- `infra/modules/foundry.bicep` — added AI Services connection resource (`category: 'AIServices'`, `authType: 'AAD'`, `isSharedToAll: true`)
- `infra/resources.bicep` — added `dependsOn: [oai]` for proper sequencing

---

## Classification & Routing Layer

**Date:** 2026-05-01  
**Author:** Bishop (AI/Agents Specialist)  
**Status:** ✅ Implemented

### Decision
Add two-stage classification and routing layer **before resolution**:
1. **ClassificationAgent** — classify ticket as "incident" or "request"
2. Route to **IncidentTicketAgent** or **RequestTicketAgent** based on classification

### Rationale
Different ticket types need different handling:
- **Incidents:** Search resolved tickets, match to KB, auto-resolve at high confidence
- **Requests:** Identify request type, route to queue or approval workflow

Generic resolution agent handles both poorly.

### Implementation
- **ClassificationAgent** (`ticket-classification-agent`, gpt-41-mini)
  - Fetches full ticket via MCP `get_ticket_by_number`
  - Classifies with keyword/category heuristics
  - Fails safe to "incident" when ambiguous
  - Returns JSON: `{ classification, confidence, rationale }`

- **IncidentTicketAgent** (`ticket-incident-agent`, gpt-41-mini)
  - Handles classified incidents
  - Auto-resolves ≥0.8 confidence or escalates

- **RequestTicketAgent** (`ticket-request-agent`, gpt-41-mini)
  - Identifies standard request types
  - Auto-queues or flags for approval

### Workflow
```
Classifier
  ├─ incident → IncidentTicketAgent → [auto-resolve | escalate]
  └─ request → RequestTicketAgent → [auto-queue | needs approval]
```

### Database Changes
- Migration `20260501000000_AddClassificationField`
- Added `Ticket.Classification` column (nvarchar(20), nullable)

### State Transitions
- `incident_auto_resolved` → State: Resolved
- `request_auto_queued` → State: InProgress
- `request_needs_approval` → State: OnHold

---

## Foundry Agent Wiring (Deprecated SDK)

**Date:** 2026-04-30  
**Author:** Bishop (AI/Agents Specialist)  
**Status:** ⚠️ Deprecated (replaced by Hosted Agent Containers)

### Context
Initial implementation used `Azure.AI.Projects` 1.0.0-beta.9 + `Azure.AI.Agents.Persistent` 1.2.0-beta.8 with `MCPToolDefinition` for native MCP support. Foundry backend connects to MCP SSE endpoint and discovers tools automatically.

### Key Decisions (for historical reference)
1. **SDK versions:** `Azure.AI.Projects` 1.0.0-beta.9, `Azure.AI.Agents.Persistent` 1.2.0-beta.8
2. **Endpoint format:** `https://{aiservices-account}.services.ai.azure.com/api/projects/{project-name}`
3. **Native MCP:** `MCPToolDefinition("mcp-tickets", mcpServerUrl)` (no manual function dispatch)
4. **Agent naming:** Stable constants in `AgentDefinitions.cs`, "get or create" pattern
5. **Write-back:** `AppDbContext` directly (avoids circular reference)
6. **Fire-and-forget:** `WebhookDispatchService` background service, `_ = Task.Run(...)` pattern
7. **Graceful degradation:** Null registration if `Foundry__ProjectEndpoint` not configured

### Issue Resolved
The `require_approval` feature for tool calls was broken on East US 2, causing agent runs to hang indefinitely. Replaced by Hosted Agent Containers (see below).

---

## Hosted Agent Containers

**Date:** 2026-05-01 (Hicks) + 2026-05-04 (Bishop, current)  
**Status:** ✅ Implemented

### Decision
Migrate from deprecated Foundry SDK (`Azure.AI.Agents.Persistent`) to **3 containerized ASP.NET Core 9 hosted agents** running in Azure Container Apps.

Architecture:
```
AgenticResolution.Api (Container App)
  ├─ HTTP POST /process ← ticket number
  ├─ Calls ClassifierAgent
  ├─ Routes to IncidentAgent or RequestAgent
  └─ Returns combined result

Hosted Agents (3 separate Container Apps):
  1. classifier-agent — classify incident vs. request
  2. incident-agent — handle incidents (search resolved tickets)
  3. request-agent — handle requests (identify standard/complex types)
     ↓
  MCP Server (TicketsApi.McpServer)
     ↓
  Agents call: get_ticket_by_number, search_tickets, update_ticket
```

### Why Hosted Agents?
1. **No approval gate blocking** — agent code calls MCP directly, no SDK polling loop
2. **Independent scaling** — each agent is separate Container App
3. **Simpler debugging** — direct HTTP calls, structured logging
4. **Resilient to SDK changes** — not tied to deprecated packages

### Implementation
**New Service: AgentOrchestrationService**
- `POST {classifierUrl}/process { ticketNumber }` → classification result
- Route based on classification
- `POST {incidentUrl}/process` or `POST {requestUrl}/process` → resolution result
- Combine and return unified result

**Configuration:**
- `Agents:ClassifierUrl` — Container App FQDN
- `Agents:IncidentUrl` — Container App FQDN
- `Agents:RequestUrl` — Container App FQDN

**Webhook Integration:**
- `WebhookDispatchService.cs` calls `AgentOrchestrationService.ProcessTicketAsync()`

**Bicep Updates:**
- `infra/modules/containerapp-api.bicep` — added 3 agent URL params
- `infra/resources.bicep` — wired params (currently empty; populate post-deployment)

### Post-Deployment
1. Bishop deploys 3 agent Container Apps
2. Hicks wires agent FQDNs via azd env or second Bicep pass
3. Smoke test: ticket webhook → agent pipeline → verify logs

---

## MCP Server Design

**Date:** 2025-07-25  
**Author:** Apone (Architect) / Hicks (Implementation)  
**Status:** ✅ Scaffolded, currently stdio-based

### Decision
Expose Tickets API as MCP server for Foundry agents.

### Tool Definitions
| Tool | Purpose | Endpoint |
|------|---------|----------|
| `get_ticket_by_number` | Fetch single ticket by INC number | `GET /api/tickets/{number}` |
| `list_tickets` | List tickets with optional state filter, pagination | `GET /api/tickets?state=&page=&pageSize=` |
| `search_tickets` | Keyword search on descriptions | `GET /api/tickets/search?q=` |
| `update_ticket` | Agent writeback (state, notes, confidence, action) | `PUT /api/tickets/{id}` |

### Transport
- **Phase 1–2:** stdio (local dev, Foundry agents)
- **Phase 3:** SSE/HTTP (standalone Container App, shared multi-agent endpoint)

### Data Access
MCP server calls Api via `HttpClient` (does NOT share DbContext):
- Clean separation of concerns
- Api has auth, validation, business logic
- MCP server can run anywhere (dev, container, sidecar) without SQL access
- Security: only network access to Api needed

### Implementation
- Project: `src/TicketsApi.McpServer/`
- Tech: `ModelContextProtocol` NuGet package
- Host: `IHostBuilder` with MCP server hosting
- Client: `ITicketApiClient` (typed HttpClient to Api)

### Build Status
✅ `dotnet build AgenticResolution.sln` → 0 errors, 0 warnings

---

## Phase 2 Bicep IaC

**Date:** 2026-04-29  
**Author:** Hicks (Backend Dev)  
**Status:** ✅ Implemented, pending `azd up` validation

### New Modules
| Module | Resource | Notes |
|--------|----------|-------|
| `storage.bicep` | StorageV2 (Standard_LRS) | Shared by Function App + Foundry hub; conn string → KV secret |
| `openai.bicep` | AI Services (kind: AIServices) | `gpt-4o-mini`, `text-embedding-3-small` deployments |
| `functionapp.bicep` | Consumption Y1 (REMOVED, now Container App) | Was for webhook receiver |
| `foundry.bicep` | Foundry Hub + Project | Modern `accounts/projects` model (no ML Workspaces) |
| `containerappenvironment.bicep` | Container Apps Environment | New; CAE backed by Log Analytics |
| `containerapp.bicep` | Container App | Webhook receiver; 0.25 CPU / 0.5Gi; minReplicas 0, maxReplicas 3 |

### Updated Modules
- `appinsights.bicep` — added `resourceId` output (for Foundry hub association)
- `resources.bicep` — wires all new modules; nine new outputs
- `main.bicep` — eight new azd outputs for Phase 2 resources

### Naming Conventions
All follow `namePrefix = 'agentic-res-${environmentName}'`:
- Storage: `saagres{env}{uniqueString}` (24 char limit)
- OpenAI: `oai-{namePrefix}` (custom subdomain for global uniqueness)
- Function consumption plan: `plan-func-{namePrefix}` (distinct from app service plan)
- Function app: `func-{namePrefix}-{uniqueString}` (matches web app naming)
- Function app MI: `id-func-{namePrefix}` (mirrors web app MI)
- Foundry Hub: `hub-{namePrefix}`
- Foundry Project: `proj-{namePrefix}`

### Architecture Decisions
1. **Shared storage** — one StorageV2 account for Function App + Foundry hub (saves $1–2/mo, halves surface area)
2. **KV reference for storage connection** — Function app uses `@Microsoft.KeyVault(SecretUri=...)` syntax
3. **webhook-hmac-secret not auto-populated** — operator-set for security
4. **Function app KV role in functionapp.bicep** — avoids re-deploying existing KV
5. **.NET 8 for function app** — Jason's directive
6. **AI Search excluded** — deferred by Jason (will add when strategy finalized)
7. **Foundry project endpoint** — constructed from AIServices endpoint + project name

---

## Resolution Pipeline: Question-Driven KB Retrieval

**Date:** 2026-05-04  
**Author:** Bishop (AI/Agents Specialist)  
**Status:** ✅ Complete + Committed to main

### Problem
Old resolution pipeline performed "dumb" KB search using only ticket short description, picked top-1 result, then asked evaluator to reason about fit. This fails because:
1. Search is untargeted (general query, not specific questions)
2. No problem decomposition (don't identify what info actually needed)
3. Single KB article assumption (answer might need multiple sources)
4. Evaluator reasoning too late (already picked wrong article)

**Failure case:** INC0010091 "VPN split tunneling misconfigured causing slow cloud app access" — system fails to match it to "VPN Not Connecting" KB article because search term doesn't capture specific problem.

### Solution: Question-Driven Retrieval Pipeline
Use **single enriched DecomposerAgent with iterative KB search**:

1. **DecomposerAgent** receives ticket details
   - Understands core problem
   - Generates 2-4 specific answerable questions
   - Executes targeted KB searches (multiple calls)
   - Synthesizes answers into structured ResolutionAnalysis
   
2. **EvaluatorAgent** receives analysis (NOT raw KB)
   - Reviews each question + synthesized answer
   - Determines if answers collectively resolve ticket
   - Assigns calibrated confidence

3. **Confidence gate** (unchanged)
   - ≥0.80 → ResolutionAgent
   - <0.80 → EscalationAgent

### New Message Types
```python
@dataclass
class ResolutionQuestion:
    question: str          # "What causes VPN split tunneling to route incorrectly?"
    search_terms: str      # "VPN split tunneling configuration cloud routing"
    answer: str            # Synthesized answer from KB
    kb_sources: list[str]  # ["VPN Not Connecting", "Cloud Access Best Practices"]

@dataclass
class ResolutionAnalysis:
    ticket_number: str
    core_problem: str      # 1-sentence root issue
    questions: list[ResolutionQuestion]  # 2-4 questions with answers
    preliminary_confidence: float
```

Breaking change: `KBSearchResult` removed (no longer pass raw KB articles downstream).

### New Workflow
```
Classifier → Incident/Request Agent (data fetcher)
  ↓ TicketDetails
DecomposerAgent
  1. Understand problem
  2. Generate 2-4 questions
  3. Search KB multiple times (targeted queries)
  4. Synthesize answers
  ↓ ResolutionAnalysis
EvaluatorAgent
  1. Review questions + answers
  2. Evaluate coherence
  ↓
Confidence gate (≥0.80)
  ├─ YES → ResolutionAgent (auto-resolve)
  └─ NO → EscalationAgent (escalate)
```

### Implementation Changes
- `src/agents_py/shared/messages.py` — new message types
- `src/agents_py/agents/decomposer/__init__.py` (NEW) — DecomposerAgent
- `src/agents_py/agents/evaluator/__init__.py` — updated prompt
- `src/agents_py/agents/incident/__init__.py` — simplified (emit TicketDetails)
- `src/agents_py/agents/request/__init__.py` — simplified (emit TicketDetails)
- `src/agents_py/workflow/__init__.py` — full rewrite (add Decomposer stage)
- `src/agents_py/devui_serve.py` — register decomposer_agent

### Expected Impact
- **Auto-resolve rate:** ↑ from ~40% to 60%+
- **Latency:** +5–10 seconds (multiple KB searches)
- **Cost:** +$0.0003/ticket (acceptable for accuracy gain)

### Tradeoffs
**Pros:**
- Higher accuracy (targeted queries)
- Better reasoning (problem decomposition)
- Multi-source synthesis
- Transparent (questions + answers logged)

**Cons:**
- Increased latency
- Higher cost
- Complexity (sophisticated prompts harder to debug)

### Success Criteria (Measured Post-Deployment)
1. Auto-resolve rate ≥60% (up from ~40%)
2. False positive rate <5% (reopened tickets)
3. Average questions per ticket: 2–4
4. KB search calls per ticket: 2–4+

### Next Steps
1. Deploy to Foundry Hosted Agents
2. Monitor auto-resolve rate improvement
3. Implement caching layer for common queries
4. Add question quality validation (meta-prompt)
5. Set up feedback loop for prompt fine-tuning

---

## Phase 2.5: Blazor Frontend & Manual Resolution Flow

### Architecture: Blazor Frontend + Manual Resolve + Workflow Visibility
**Date:** 2026-05-06  
**Author:** Apone (Lead/Architect)  
**Status:** ✅ In progress (Web project created, API contracts extended)  
**Scope:** Phase 2.5 — Blazor UI rebuild + decoupling agent pipeline from webhook dispatch + workflow progress visibility

#### Decision
Add new **`AgenticResolution.Web`** project (Blazor Server, .NET 10, Interactive Server render mode):
- Separate from API — independent deployment to App Service
- Typed `TicketsApiClient` with HttpClientFactory
- Pages: `/tickets`, `/tickets/{number}`, `/tickets/{number}/runs/{runId}`
- Shared DTO contracts library: `AgenticResolution.Contracts`

**API Contract Extensions (by Hicks):**
1. **Enhanced list filtering** — `assignedTo`, `state`, `category`, `priority`, `q`, `sort`, `dir`, pagination
2. **Detail endpoint** — `GET /api/tickets/{number}/details` with comments + runs
3. **Comments** — `GET/POST /api/tickets/{number}/comments`
4. **Manual resolve** — `POST /api/tickets/{number}/resolve` → 202 Accepted with `runId`
5. **Workflow runs** — `GET /api/tickets/{number}/runs`, `GET /api/runs/{runId}`, `GET /api/runs/{runId}/events`
6. **Webhook flag** — `Webhook:AutoDispatchOnTicketWrite` (default false) — opt-in auto-dispatch

**Database changes:**
- `TicketComment` — author, body, isInternal, createdAt
- `WorkflowRun` — status (Pending/Running/Completed/Escalated/Failed), triggeredBy, note, startedAt, completedAt, finalAction, finalConfidence
- `WorkflowRunEvent` — runId, sequence, executorId, eventType, payload, timestamp
- Indexes on UpdatedAt, AssignedTo, Category

#### Rationale
- Clear separation: .NET API owns CRUD, Python API owns orchestration
- Blazor Server simplifies auth and SignalR (no CORS needed)
- Manual resolution is explicit — no auto-dispatch surprises
- Async non-blocking flow allows UI responsiveness

---

### Python Resolution API: FastAPI + SSE Streaming
**Date:** 2026-05-06  
**Author:** Bishop (AI/Agents Specialist)  
**Status:** ✅ Implemented  
**Location:** `src/python/resolution_api/`

#### Decision
Created production-ready Python Resolution API that wraps existing agent workflow with FastAPI + Server-Sent Events (SSE).

**Endpoint:** `POST /resolve`  
**Request:** `{ "ticket_number": "INC0010101" }`  
**Response:** Server-Sent Events stream (text/event-stream) with workflow progression

**Event format (JSON):**
```json
{"stage": "classifier", "status": "started", "timestamp": "2026-05-06T12:34:56Z"}
{"stage": "classifier", "status": "completed", "result": {"type": "incident", "ticket_number": "INC0010101"}}
{"stage": "incident_fetch", "status": "started"}
{"stage": "incident_fetch", "status": "completed", "result": {...}}
{"stage": "incident_decomposer", "status": "started"}
{"stage": "incident_decomposer", "status": "completed", "result": {...}}
{"stage": "evaluator", "status": "started"}
{"stage": "evaluator", "status": "completed", "result": {"confidence": 0.85, "kb_source": "..."}}
{"stage": "resolution", "status": "started"}
{"stage": "resolution", "status": "completed", "result": {"output": "..."}}
```

**Health checks:**
- `GET /health` — Azure Container Apps probe
- `GET /` — Root health check

**Architecture:**
- Thin wrapper — NO duplication of agent logic
- Imports existing `workflow`, `agents`, `shared` modules
- Orchestrates via `workflow.run_stream()`
- Maps workflow message types to SSE events
- Stateless design (no WorkflowRun persistence in Python layer)

**Deployment:**
- Docker image: multi-stage build
- Azure Container Apps: `ca-resolution`
- Environment: AZURE_OPENAI_ENDPOINT, AZURE_OPENAI_MODEL, MCP_SERVER_URL, PORT=8000
- DefaultAzureCredential for managed identity auth

#### Rationale
- **Direct Blazor → Python API (not proxied through .NET):** Removes unnecessary hop, enables SSE streaming directly
- **SSE (not WebSockets):** One-way stream (fire-and-forget), HTTP/1.1 native, simpler client
- **Stateless:** Thin wrapper — persistence is .NET's concern, Python focuses on orchestration

---

### Architecture Pivot: Webhook-Driven vs. In-Process Orchestration
**Date:** 2026-05-05  
**Author:** Hicks (Backend Dev)  
**Status:** ✅ Implemented  
**Context:** User clarification on Phase 2.5 manual resolve flow

#### Decision
**Resolve endpoint fires webhook → external receiver (Azure Function) processes orchestration.**

NOT in-process resolution queue.

**Resolve Endpoint Flow:**
1. Look up ticket
2. Check for existing Pending/Running run — if found, return 200 with existing (idempotent)
3. Create WorkflowRun (Pending)
4. Enqueue webhook `resolution.started` unconditionally
5. Return 202 Accepted

**Webhook Payload Extension:**
```json
{
  "event_id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "event_type": "resolution.started",
  "timestamp": "2026-05-05T18:30:00Z",
  "ticket": { /* snapshot */ },
  "run_id": "8c7a3d91-4e2f-4d89-9b1a-5f0c8e9d7a2b"
}
```

**Webhook Event Types (with RunId correlation):**
| Event Type | Trigger | RunId | Config Flag |
|---|---|---|---|
| `ticket.created` | POST /tickets | ❌ | Webhook:AutoDispatchOnTicketWrite |
| `ticket.updated` | PUT /tickets/{id} | ❌ | Webhook:AutoDispatchOnTicketWrite |
| `resolution.started` | POST /resolve | ✅ | (unconditional) |
| `workflow.running` | Pending→Running | ✅ | Webhook:FireOnWorkflowProgress |
| `workflow.completed` | Run completes | ✅ | Webhook:FireOnWorkflowProgress |
| `workflow.escalated` | Run escalates | ✅ | Webhook:FireOnWorkflowProgress |
| `workflow.failed` | Run fails | ✅ | Webhook:FireOnWorkflowProgress |

**Configuration:**
```json
{
  "Webhook": {
    "TargetUrl": "https://external-system.example.com/webhooks",
    "Secret": "...",
    "AutoDispatchOnTicketWrite": false,
    "FireOnResolutionStart": true,
    "FireOnWorkflowProgress": true
  }
}
```

**Rationale:**
- Separation of concerns: API persists, Function orchestrates
- Scalability: Function scales independently on Consumption plan
- Retry semantics: built-in webhook retry with backoff
- Integration future: same plumbing supports ServiceNow sync

#### Verification
- Create/Update webhook behavior preserved (gated by config flag)
- Resolve endpoint fires webhook unconditionally
- Idempotent re-trigger prevents duplicate webhooks
- MCP server unchanged (calls GET/PUT ticket endpoints)
- Build: 0 errors

---

### Frontend Resolve Button Flow & Progress Listening
**Date:** 2026-05-05  
**Author:** Ferro (Frontend Developer)  
**Status:** Specified — awaiting implementation  
**Related:** Apone's Blazor architecture, Hicks' resolve webhook contract

#### Decision
**Clicking "Resolve with AI" must be non-blocking and event-driven:**

1. **Trigger resolve** — `POST /api/tickets/{number}/resolve` → returns 202 with `runId`
2. **Navigate** — Go to progress page `/tickets/{number}/runs/{runId}` immediately
3. **Listen** — Connect to SignalR hub `/hubs/runs`, join group `run-{runId}` (or fallback to polling `GET /api/runs/{runId}/events`)
4. **Display** — Render executor lanes with real-time progress (classifier → decomposer → evaluator → resolution/escalation)
5. **Stop** — When run reaches terminal state (Completed/Escalated/Failed), disconnect

**UI Structure (example):**
```
┌─────────────────────────────────────────────────────────────┐
│  Ticket INC0010042 — Resolution Run                         │
│  Status: [Running] ●                                         │
├─────────────────────────────────────────────────────────────┤
│  Timeline:                                                   │
│                                                              │
│  ✓ ClassifierExecutor                    17:43:20           │
│    → Classification: Incident (95%)                          │
│                                                              │
│  ⏳ IncidentDecomposerExecutor            17:43:23           │
│    → Generating diagnostic questions...                      │
│                                                              │
│  ⏱ EvaluatorExecutor                      (pending)          │
│                                                              │
│  ⏱ ResolutionSummarizerExecutor           (pending)          │
└─────────────────────────────────────────────────────────────┘
```

**Visual states:**
- ✓ green checkmark = completed
- ⏳ spinner = running
- ⏱ gray clock = pending
- ❌ red X = failed

#### Rationale
- Non-blocking HTTP (202 Accepted)
- Async orchestration in Function
- Rich real-time UI feedback
- Idempotent re-trigger safety

---

### .NET API Cleanup: Architecture Pivot
**Date:** 2026-05-06  
**Author:** Hicks (Backend Dev)  
**Status:** ✅ Implemented  
**Context:** Architecture pivot — resolution orchestration moved to Python

#### Decision
**Remove dead orchestration code from .NET API. Focus on CRUD simulation only.**

**Deletions:**
- `AgentOrchestrationService` (scoped service)
- `IWorkflowProgressTracker` / `WorkflowProgressTracker` (scoped)
- `IResolutionQueue` / `ResolutionQueue` (singleton)
- `ResolutionRunnerService` (hosted service)
- Entire `src/dotnet/AgenticResolution.Api/Agents/` folder
  - AgentOrchestrationService.cs
  - ResolutionRunnerService.cs
  - IWorkflowProgressTracker.cs / WorkflowProgressTracker.cs
  - AgentDefinitions.cs
  - FoundryAgentService.cs
  - WORKFLOW_SEQUENCE_NAMES.md

**Endpoints removed:**
- `POST /api/tickets/{number}/resolve`
- `GET /api/tickets/{number}/runs`
- `GET /api/runs/{runId}`
- `GET /api/runs/{runId}/events`

**Preserved (Per Instructions):**
- `Models/WorkflowRun.cs`, `Models/WorkflowRunEvent.cs` — database models untouched (Python may use them for audit)
- All CRUD endpoints (tickets, knowledge base)
- MCP server (unchanged, still calls GET/PUT endpoints)
- Webhook dispatch mechanism (plumbing preserved for future integration)

**Rationale:**
- Python Resolution API now owns orchestration end-to-end
- Separation of concerns: .NET owns CRUD, Python owns workflow
- Cleaner codebase (no orphaned services)
- Database models preserved in case Python needs audit trail

**Verification:**
- Build: 0 errors, 2 warnings (NU1510 — non-blocking)
- All CRUD endpoints intact for ServiceNow simulation

---

## Ticket Loading Configuration Fix

### Ferro: Frontend Configuration for Blazor UI (2026-05-07)

**Date:** 2026-05-07  
**Author:** Ferro (Frontend Dev)  
**Status:** ✅ Implemented locally (not deployed)

**Context:** Tickets were not loading when the Blazor UI first rendered.

**Decision:**
The Blazor frontend must maintain two separate base URLs for the Python Resolution API/SSE pivot:
- `ApiClient:BaseUrl` / `ApiClient__BaseUrl` → .NET tickets CRUD API (`https://ca-api-tocqjp4pnegfo.graybush-af9ee262.eastus2.azurecontainerapps.io`)
- `ResolutionApi:BaseUrl` / `ResolutionApi__BaseUrl` → Python Resolution API (`https://ca-resolution-tocqjp4pnegfo.graybush-af9ee262.eastus2.azurecontainerapps.io`)

The ticket list remains owned by the .NET CRUD API. The Python API only owns `POST /resolve` SSE streaming.

**Why:**
Production `appsettings.json` had an empty `ApiClient` base URL, causing the UI to render the "API not configured" state and blocking `/tickets` list load.

**Implementation:**
- `src/dotnet/AgenticResolution.Web/appsettings.json` — now includes deployed ca-api URL
- `src/dotnet/AgenticResolution.Web/Program.cs` — falls back to `TICKETS_API_URL` env var for `TicketApiClient`
- `infra/resources.bicep` — now persists both frontend app settings in `rg-agentic-res-src-dev`
- `Components/Pages/Tickets/Index.razor` — awaits initial load from `OnInitializedAsync` instead of fire-and-forget
- `TicketApiClient` — returns status/body details for failed REST calls

**Verification:**
- Live tickets API at `ca-api-tocqjp4pnegfo` returned HTTP 200 with expected paged JSON (items=1, total=98)
- No Azure deployment performed during fix
- Local dotnet build blocked: host has .NET 9, project targets .NET 10

---

### Hicks: Backend API Health & Contract (2026-05-07)

**Date:** 2026-05-07  
**Author:** Hicks (Backend Dev)  
**Status:** ✅ Verified

**Context:** Diagnosed root cause of ticket loading failure.

**Findings:**
- Backend route intact: `GET /api/tickets` mapped in `src/dotnet/AgenticResolution.Api/Api/TicketsEndpoints.cs`
- Deployed API `GET /api/tickets?page=1&pageSize=1` returned HTTP 200 with:
  - Expected paged contract: `items`, `page`, `pageSize`, `total`
  - camelCase property names
  - String enum values for `priority` and `state`
  - Live DB data: 98 tickets in system
- CORS not a blocker for Blazor Server (server-side `TicketApiClient` execution)
- Root cause: Frontend `ApiClient:BaseUrl` configuration was empty/missing

**Decision:**
Blazor production configuration must point `ApiClient__BaseUrl` at the external .NET CRUD API:
`https://ca-api-tocqjp4pnegfo.graybush-af9ee262.eastus2.azurecontainerapps.io`

Keep resource group reference as `rg-agentic-res-src-dev`.

**Backend Contract (Unchanged):**
```
GET /api/tickets?page={page}&pageSize={pageSize}&assignedTo={assignedTo?}&state={csv?}&category={category?}&priority={csv?}&q={query?}&sort={created|modified}&dir={asc|desc}
→ { "items": TicketResponse[], "page": number, "pageSize": number, "total": number }
```

**Verification:**
- Local dotnet build blocked: host has .NET 9, project targets .NET 10

---

## Leadership Directives

### Directive 1: Use Azure AI Foundry, Not Standalone OpenAI
**Date:** 2026-04-29T18:56:12Z  
**From:** Jason Farrell (via Copilot)

**What:** Do NOT use standalone Azure OpenAI (`Microsoft.CognitiveServices/accounts` kind: `OpenAI`). Use Azure AI Foundry integrated AI Services (`kind: AIServices`) — the current non-deprecated approach. All model deployments go through AI Foundry.

**Why:** Standalone Azure OpenAI service is deprecated in favor of Microsoft Foundry / AI Services.

**Implemented:** Hicks migrated to `kind: AIServices` with Foundry hub connection (2026-04-29).

---

### Directive 2: AI Search Deferred
**Date:** 2026-04-29T18:39:27Z  
**From:** Jason Farrell (via Copilot)

**What:** AI Search is deferred. Jason has specific data he wants to use with it and will direct that work explicitly. Do NOT provision or configure Azure AI Search until Jason gives the go. All other Phase 2 Azure resources (OpenAI, Foundry, Function App, Storage) may proceed.

**Why:** User requirement — AI Search data strategy not yet finalized.

**Status:** ✅ Bicep modules updated to exclude AI Search. `text-embedding-3-small` deployment provisioned now (ready for future integration).

---

### Directive 3: Resource Group Naming
**Date:** 2026-05-06T21:25:00Z  
**From:** Jason Farrell (via Copilot)

**What:** The correct resource group for all deployments is `rg-agentic-res-src-dev`. All references to `rg-agentic-res-agentic-resolution-dev` are outdated and should use `rg-agentic-res-src-dev`.

**Why:** User request — standardized naming convention.

**Status:** ✅ Decisions merged and documented (2026-05-07).

---

## SQL Entra Auth

**Date:** 2026-05-07  
**Decided by:** Bishop (deployment specialist)  
**Status:** ✅ Implemented

### Problem

Azure SQL deployment to `rg-agent-resolution-test` failed due to MCAPS policy `AzureSQL_WithoutAzureADOnlyAuthentication_Deny` requiring:

```json
properties.administrators.azureADOnlyAuthentication == true
```

Existing infrastructure had stale SQL password authentication parameters and missing Entra admin persistence.

### Decision

**Adopt Entra-only authentication for all Azure SQL deployments:**

1. **No SQL password authentication** — Remove `administratorLogin` and `administratorLoginPassword`
2. **Entra admin from signed-in user** — Use current Azure CLI user as SQL Server Entra admin
3. **Persist to azd environment** — Store Entra admin values via `azd env set` before deployment
4. **Regenerate ARM templates** — Keep `main.json` in sync with Bicep sources

### Implementation

**Bicep Templates:**
```bicep
// infra/modules/sqlserver.bicep
properties: {
  administrators: {
    administratorType: 'ActiveDirectory'
    login: entraAdminLogin
    sid: entraAdminObjectId
    tenantId: entraAdminTenantId
    azureADOnlyAuthentication: true  // ✅ MCAPS compliant
  }
}
```

**Setup Script:**
```powershell
# Get signed-in user
$currentUser = az ad signed-in-user show | ConvertFrom-Json
$entraAdminLogin = $currentUser.userPrincipalName
$entraAdminObjectId = $currentUser.id

# Persist to azd environment
azd env set entraAdminLogin $entraAdminLogin
azd env set entraAdminObjectId $entraAdminObjectId
```

### Connection String Impact

**Before (SQL auth):**
```
Server=tcp:sql-env.database.windows.net,1433;Database=agenticresolution;User ID=sqladmin;Password=...;
```

**After (Entra auth):**
```
Server=tcp:sql-env.database.windows.net,1433;Initial Catalog=agenticresolution;Authentication=Active Directory Default;
```

Managed identities automatically authenticate when running in Azure. Local development requires `az login`.

### Rationale

1. **MCAPS policy requirement** — Mandatory for Azure SQL in Microsoft tenant
2. **Security best practice** — Eliminates password-based authentication risks
3. **Managed identity support** — Enables passwordless connection strings
4. **Audit compliance** — All database access tied to Azure AD identities

### Files Modified

1. **`infra\main.bicep`** — Updated SQL server module parameters
2. **`infra\resources.bicep`** — Refined resource definitions
3. **`infra\modules\sqlserver.bicep`** — Core Entra admin configuration
4. **`scripts\Setup-Solution.ps1`** — Enhanced error handling, cleaned whitespace

### Validation

✅ `az bicep build --file infra\main.bicep` — succeeded  
✅ `git diff --check` — no trailing whitespace on changed lines  
✅ All Bicep syntax valid

### Risks & Mitigations

**Risk:** Developer doesn't have SQL Admin rights in existing environments  
**Mitigation:** Existing Entra admin can grant new admin permissions via T-SQL

**Risk:** CI/CD pipeline uses service principal without SQL access  
**Mitigation:** Grant service principal Entra admin role during environment setup

### Next Actions

1. ✅ **Test deployment** — Verified in `rg-agent-resolution-test`
2. ⏳ **Update CI/CD** — Ensure pipeline service principal has appropriate values set
3. ⏳ **Document local dev** — Update README with `az login` requirement
4. ⏳ **Verify managed identities** — Confirm App Service and Container Apps can connect

---

## Entra Auth Verification

**Date:** 2026-05-07  
**Agent:** Hicks (Backend Developer)  
**Status:** ✅ Complete - No action required

### Context

Jason requested verification that the .NET API works with Azure SQL Entra-only authentication after Bishop's infrastructure changes implementing managed identity authentication.

### Finding

**The application already fully supports Entra authentication without any code changes.**

### Technical Details

**Why it works automatically:**
1. **Connection String:** Infrastructure provides `Authentication=Active Directory Default`
2. **SqlClient Support:** Microsoft.Data.SqlClient 6.1.1+ natively interprets as DefaultAzureCredential
3. **EF Core:** `UseSqlServer(connectionString)` transparently passes auth to SqlClient
4. **Azure.Identity:** Already referenced (1.14.2) - provides credential chain

**Authentication behavior:**
- **Azure deployment:** Uses App Service/Container App managed identity
- **Local development:** Uses Azure CLI credentials (`az login`)
- **Fallback chain:** ManagedIdentity → AzureCli → VisualStudio → SharedToken → Interactive

**Changes made:**
- Added startup diagnostic logging to confirm auth mode
- Refactored to single `connectionString` variable (code quality)

### Recommendation

**Accept as-is.** This is correct "infrastructure-driven auth" pattern:
- No app-level token acquisition
- No explicit credential instantiation
- Connection string configuration handles everything
- Works identically in Azure and local dev

### Validation

- Build: ✅ Success
- Tests: ✅ 15/15 pass
- No new dependencies
- No breaking changes

**Decision:** Accept current implementation (standard pattern, zero maintenance).

---

## Azure OpenAI Data-Plane RBAC for Resolution API

**Date:** 2026-05-08  
**Authors:** Hicks (Backend Dev), Bishop (AI/Agents Specialist)  
**Requested by:** Jason Farrell  
**Status:** ✅ Applied to test2; Automated in Setup-Solution.ps1

### Problem

test2 Resolution API failed with `401 PermissionDenied` when calling Azure OpenAI chat completions:
```
Microsoft.CognitiveServices/accounts/OpenAI/deployments/chat/completions/action
```

The managed identity `id-resolution-agent-resolution-test2` (principal `c6b82506-1e92-49b1-8e4b-962defc93a9f`) lacked data-plane RBAC.

### Root Cause Analysis

- Azure OpenAI **chat completions are data-plane operations**, not control-plane
- Control-plane roles (e.g., Contributor) do NOT grant data-plane `chat/completions/action`
- Resolution API uses `DefaultAzureCredential` → requires explicit RBAC assignment

### Decision

**Every managed identity used by the Python Resolution API must receive the built-in `Cognitive Services OpenAI User` role at the Azure OpenAI / Azure AI Services account scope it calls.**

**Least-privilege role:** `Cognitive Services OpenAI User` (includes `Microsoft.CognitiveServices/accounts/OpenAI/deployments/chat/completions/action`)

### Implementation

**test2 Application:**
- Managed identity: `id-resolution-agent-resolution-test2`
- Principal: `c6b82506-1e92-49b1-8e4b-962defc93a9f`
- Azure OpenAI account: `oai-agentic-res-src-dev`
- Deployment: `gpt-5.1-deployment` (fallback when `AZURE_OPENAI_ENDPOINT` not set)

**Action taken:**
1. Granted `Cognitive Services OpenAI User` on `oai-agentic-res-src-dev` scope
2. Allowed 2-5 minutes for Azure RBAC propagation
3. Restarted Resolution API revision to clear cached PermissionDenied
4. Validated `POST /resolve` reached terminal `resolved` state

**Automated for future deployments:**
- Updated `scripts\Setup-Solution.ps1` to assign role to Resolution API identity after ACR pull access
- Role assigned before Container App creation/update

### Operating Guidance

1. **Grant RBAC before starting Resolution API revision** — PermissionDenied error caches in Agent Framework singleton workflow state
2. **Allow 2-5 minutes for propagation** — Azure RBAC delays 1-3 minutes typical
3. **Restart revision if stuck** — If app already failed or Agent Framework workflow is busy, restart container
4. **Future endpoints** — If Resolution API targets different Azure OpenAI account, set `AZURE_OPENAI_ENDPOINT` env var and grant role at that account scope

### Rationale

- **Data-plane operations require data-plane RBAC** — control-plane Contributor role insufficient
- **Least privilege** — `Cognitive Services OpenAI User` grants only inference, not management/deletion
- **Idempotent assignment** — Role assignment is side-effect idempotent (can re-apply safely)

### Verification

- test2 `POST /resolve` **✅ Terminal resolved state achieved**
- Setup scripts **✅ Updated for future deployments**
- No code changes needed in Resolution API (Azure.Identity already handles Entra auth)

---

## Status Summary

| Component | Owner | Status | Date |
|-----------|-------|--------|------|
| Solution Split (Api/Web/McpServer) | Hicks | ✅ Implemented | 2026-04-30 |
| Classification + Routing Layer | Bishop | ✅ Implemented | 2026-05-01 |
| Modern Foundry Resource Types | Hicks | ✅ Implemented | 2026-04-29 |
| Container App Webhook Receiver | Hicks | ✅ Bicep complete | 2026-04-29 |
| AI Services + Foundry Connection | Hicks | ✅ Implemented | 2026-04-29 |
| Hosted Agent Containers | Bishop/Hicks | ✅ Containers + Bicep | 2026-05-04 |
| Question-Driven Resolution Pipeline | Bishop | ✅ Committed to main | 2026-05-04 |
| MCP Server | Hicks | ✅ Scaffolded | 2026-04-30 |
| Phase 2 Bicep IaC | Hicks | ✅ Complete | 2026-04-29 |
| Python Resolution API (Phase 2.5) | Bishop | ✅ Implemented | 2026-05-06 |
| Blazor Web Project (Phase 2.5) | Ferro | ✅ Created | 2026-05-05 |
| API Contract Extensions (Phase 2.5) | Hicks | ✅ Implemented | 2026-05-06 |
| Azure SQL Entra-Only Authentication | Bishop | ✅ Implemented | 2026-05-07 |
| Entra Auth Verification (.NET API) | Hicks | ✅ No changes needed | 2026-05-07 |
| Azure OpenAI Data-Plane RBAC | Hicks/Bishop | ✅ Applied to test2 + Automated | 2026-05-08 |

---

**Last Updated:** 2026-05-08  
**Last consolidated:** 2026-05-08T20:34:35Z  
**Next review:** Verify test2 Resolution API health in production
