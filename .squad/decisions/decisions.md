# Project Decisions — Agentic Resolution

**Last Updated:** 2026-05-04  
**Status:** Active decisions (deduplicated, consolidated from .squad/decisions/inbox/)

---

## Table of Contents
1. [Architecture: Solution Split](#architecture-solution-split)
2. [Foundry Resource Types: Modern (AIServices)](#foundry-modern-resource-type)
3. [Webhook Receiver: Container App](#webhook-container-app)
4. [AI Services & Foundry Connection](#aiservices-foundry-connection)
5. [Classification & Routing Layer](#classification-routing)
6. [Foundry Agent Wiring (Deprecated SDK)](#foundry-agent-wiring-deprecated)
7. [Hosted Agent Containers (Current)](#hosted-agent-containers)
8. [MCP Server Design](#mcp-server-design)
9. [Phase 2 Bicep IaC](#phase-2-bicep)
10. [Resolution Pipeline: Question-Driven KB](#resolution-pipeline-redesign)
11. [Directives from Leadership](#leadership-directives)

---

## Architecture: Solution Split

**Date:** 2025-07-25  
**Author:** Apone (Lead/Architect)  
**Status:** ✅ Implemented by Hicks (2026-04-30)

### Decision
Split monolithic Blazor server into three-project layout:
- **AgenticResolution.Api** — EF Core, SQL, webhooks, CRUD endpoints
- **AgenticResolution.Web** — Blazor UI only, calls Api over HTTP
- **AgenticResolution.McpServer** — MCP wrapper around Api, exposes tools to Foundry agents

### Rationale
- Independent scaling (UI vs. data layer)
- Clean security boundary (only Api touches SQL)
- MCP server access requires separate process
- Jason's explicit requirement

### Implementation
| Component | Location | Port (dev) | Azure |
|-----------|----------|-----------|-------|
| Api | `src/AgenticResolution.Api/` | 5001 | App Service (new) |
| Web | `src/AgenticResolution.Web/` | 5000 | App Service (existing) |
| McpServer | `src/TicketsApi.McpServer/` | 5002 | Container App (Phase 2) |

### Payload: ServiceNow-Style Webhook Snapshot
Webhook now sends `TicketWebhookSnapshot` (partial):
- **Included:** Number, ShortDescription, Category, Priority, Urgency, Impact, State, Caller, AssignmentGroup, OpenedAt
- **Excluded:** Id, Description, UpdatedAt (fetch on-demand via API if needed)

### Test Projects
- `AgenticResolution.Api.Tests` — targets Api endpoints via `WebApplicationFactory`
- `AgenticResolution.Web.ComponentTests` — targets Blazor components (unchanged)

### Build Status
✅ `dotnet build AgenticResolution.sln` → 0 errors, 0 warnings

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

---

**Last consolidated:** 2026-05-04T16:16:14Z  
**Next review:** Post-deployment validation of hosted agents
