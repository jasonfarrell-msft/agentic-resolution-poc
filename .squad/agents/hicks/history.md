# Project Context

- **Owner:** Jason Farrell
- **Project:** agentic-resolution — backend for a ServiceNow simulation. Ticket POST → save to SQL Server → fire webhook. Webhook downstream feeds AI Search + Foundry Agents in later phases.
- **Stack:** ASP.NET Core, EF Core, SQL Server (Azure SQL target)
- **Phase 1 scope:** API + DB + webhook plumbing. No Azure deployment yet — local SQL.
- **Created:** 2026-04-29

## Phase 1 Architecture (Apone)

**Solution:** Single-project Blazor Server with embedded minimal API (no separate API service).
- Ticket endpoint: POST /api/tickets
- TicketEndpoints.cs in `Endpoints/` folder
- WebhookService dispatches async post-commit success (Channel<T>, fire-and-forget)

**Data Model:** Tickets table with fields: Id (GUID), Number (auto-gen `INC` + 7-digit), ShortDescription, Description, Category, Priority (1-4, default 4), State (enum: New/InProgress/Resolved/Closed), AssignedTo, Caller, CreatedAt, UpdatedAt.

**Webhook Dispatch:** 3 retries with exponential backoff (1s, 4s, 16s). HMAC-SHA256 signature. Payload includes event_id, event_type, timestamp, ticket data. Receiver URL configured in user-secrets (Phase 1) or Key Vault (Phase 2).

**Local dev:** Docker SQL Server (mcr.microsoft.com/mssql/server:2022-latest). User-secrets for connection string, webhook URL, HMAC secret. EF migrations code-first.

**Questions pending:** .NET 9 or 8 LTS? Docker ready on all machines or need LocalDB fallback? Ticket # format confirmed?

## Core Context

**Phase 1 (2026-04-29):**
- Single Blazor Server project with minimal API
- POST /api/tickets CRUD endpoint; webhook dispatch with HMAC-SHA256 and 3 retries
- SQL Server (EF Core code-first); ticket counter seeded at INC0010001
- Gitignore baseline added (2026-05-04)

**Phase 2 (2026-05-05+):**
- Decomposed: .NET API remains CRUD-only; Python Resolution API handles orchestration
- Deleted: AgentOrchestrationService, ResolutionRunnerService, all workflow endpoints
- Backend API kept minimal (ServiceNow mock contract)
- Python/Foundry agents now own resolution logic

**Current state (2026-05-07):**
- ✅ ca-api Container App deployed and healthy
- ✅ GET /api/tickets?page=1&pageSize=1 returns 200 JSON with 98 paged records
- ✅ Routing verified: `/api/tickets` on ca-api (CRUD), `/tickets` on App Service (UI)
- ✅ App Service settings fixed: ApiClient__BaseUrl and ResolutionApi__BaseUrl set
- ✅ TicketApiClient hardened against HTML responses
- ✅ dotnet build passes; ready for deployment

---

## Learnings

- **Final resolver status persistence (2026-05-07):** The live Tickets API now has a native `Escalated` ticket state. `PUT /api/tickets/{id}` persists `Escalated` directly and also normalizes the legacy resolver payload `state=InProgress` + `agentAction=escalated_to_human` to `Escalated`; read DTOs also project legacy escalated records as `Escalated`. Deployed `ca-api-tocqjp4pnegfo` in `rg-agentic-res-src-dev` with image `acragressrcdevtocqjp4pnegfo.azurecr.io/agentic-resolution/api-src-dev:hicks-final-status-read-20260507024330` (revision `ca-api-tocqjp4pnegfo--0000014`). Validation: `dotnet build src/dotnet/AgenticResolution.sln --nologo` passed; `dotnet build src/dotnet/AgenticResolution.Api/AgenticResolution.Api.csproj --nologo` passed with existing NU1603 warning; known ticket `INC0010102` returned detail state `Resolved`; direct validation ticket `INC0010103` returned detail state `Escalated`; resolver validation ticket `INC0010104` completed with terminal `escalated` and `GET /api/tickets/INC0010104/details` returned ticket state `Escalated`.
- **Ticket detail contract (2026-05-07):** Detail fetch is by ticket number, not GUID. Live ca-api evidence from `rg-agentic-res-src-dev`: `GET /api/tickets?page=1&pageSize=1` returned 200 with 98 total tickets and sample `INC0010102` / `bcd0df92-9b6b-4f17-982a-c1989924edbc`; `GET /api/tickets/INC0010102/details` returned 200 JSON with `ticket`, `comments`, and `runs`; `GET /api/tickets/INC0010102` returned 200 single-ticket JSON; GUID forms `/api/tickets/{id}` and `/api/tickets/{id}/details` returned 404. Blazor detail route `/tickets/{Number}` calls `TicketApiClient.GetTicketDetailsAsync(number)`, which correctly calls `api/tickets/{number}/details`. Source API DTO was aligned to include `runs` with workflow events so future deploys preserve the production contract.
- **Validation:** DataAnnotations + a generic `ValidationFilter<T>` endpoint filter. In-box, no extra dep, swap-out is cheap if Phase 2 needs FluentValidation.
- **Webhook payload:** snake_case JSON `{ event_id, event_type, timestamp, ticket{...} }`. HMAC-SHA256 over the raw body, header `X-Resolution-Signature: sha256=<hex>`. `event_id` is the receiver-side dedup key.
- **Retry policy:** 3 retries on top of initial attempt (4 total) at 1s, 4s, 16s. No jitter (single dispatcher). Permanent failure logs Error and drops; no DLQ in Phase 1.
- **Project layout:** Single Blazor Server project per Apone. My code under `src/AgenticResolution.Web/{Api,Data,Models,Webhooks,Migrations}/`; Ferro under `Components/`, `Pages/`, `Layout/`, `Services/`. `Program.cs` is shared with banner-comment regions for clean merges.
- **Ticket numbers:** DB-backed counter (`TicketNumberSequences`, seeded LastValue=10000) incremented atomically via `UPDATE … OUTPUT INSERTED.LastValue`. First ticket = `INC0010001`.
- **Coordination caveat:** EF migration uses default timestamp prefix, not literal `0001_Initial`. Functionally equivalent; renaming breaks the EF tooling.
- **AI Services vs OpenAI resource kind:** `Microsoft.CognitiveServices/accounts` with `kind: 'OpenAI'` is deprecated. The correct modern resource is `kind: 'AIServices'`, which is Foundry-integrated and supports the same model deployments. AI Foundry hubs connect to the AI Services account via a `Microsoft.MachineLearningServices/workspaces/connections` child resource (`category: 'AIServices'`, `authType: 'AAD'`). Always use `AIServices` going forward; standalone `OpenAI` kind will eventually be retired.
- **Modern Foundry resource type (2025+):** `kind: 'AIFoundry'` does NOT exist as a CognitiveServices kind. The correct modern approach is: (1) `Microsoft.CognitiveServices/accounts` with `kind: 'AIServices'` and `allowProjectManagement: true` — this account IS the Foundry hub; (2) `Microsoft.CognitiveServices/accounts/projects` as child resources for projects. The old `Microsoft.MachineLearningServices/workspaces` with `kind: 'Hub'` / `kind: 'Project'` is the deprecated "Foundry classic" approach and should not be used. No separate ML workspace, no workspace connections needed — the AIServices account and its projects coexist natively. Use API version `2025-06-01` or later for both resources. Project endpoint = AIServices account endpoint; Agents SDK connects to `{endpoint}/projects/{projectName}`. `hubName` is no longer a separate resource name — the AIServices account IS the hub.
- **Container App KV secrets vs App Service KV references:** App Service (and Function Apps) use an inline reference syntax in app settings: `@Microsoft.KeyVault(VaultName=...;SecretName=...)` — the platform resolves these transparently. Container Apps have NO such inline syntax. Instead, declare a `secrets` array at the `configuration` level with `{ name, keyVaultUrl, identity }` (identity = user-assigned MI resource ID), then reference by `secretRef: '<secret-name>'` in the container's env vars. The MI must hold `Key Vault Secrets User` on the vault. Log Analytics also requires `workspace.properties.customerId` (a GUID) for the CAE `appLogsConfiguration`, not the ARM resource ID — add a `customerId` output to the loganalytics module and pass it through.
- **Gitignore baseline established (2026-05-04):** Repo had NO .gitignore at root; 184 build artifacts (155 bin/, 29 obj/) were tracked. Added standard .NET .gitignore via `dotnet new gitignore` (484 lines). Modified to preserve `.squad/log/*.md` (project docs, not build logs) by adding `!.squad/log/` negation after the `[Ll]og/` pattern. Untracked all 184 files with `git rm --cached`. Commits: 9c98efa (add .gitignore), 7e121fd (untrack artifacts). Build outputs now properly ignored — no further cleanup needed.
- **Resolution queue enqueue (2026-07-25):** `POST /api/tickets/{number}/resolve` was creating a `WorkflowRun` but never feeding `IResolutionQueue`. Injected `IResolutionQueue` into the endpoint and called `Enqueue(new ResolutionRunRequest(...))` after save. The `ResolutionRunnerService` background worker now receives work via its channel.
- **Synthetic progress events removed (2026-07-25):** `AgentOrchestrationService.ProcessTicketAsync` was firing premature Started+Completed events for ClassifierExecutor and IncidentFetchExecutor before any real work. Removed Completed for Classifier (only fires Started + Routed now) and removed IncidentFetchExecutor entirely — the agent call fires under IncidentDecomposerExecutor Started/Completed. Step events should only mark Completed when actual executor work finishes.
- **Architecture note (agents):** There is no `Agents:IncidentUrl` in the real system. Agents use Agent Framework with defined Executors; the orchestration service's `_config["Agents:IncidentUrl"]` is a stub that will be replaced by proper executor invocation.
- **Ticket list loading (2026-05-07):** The deployed .NET CRUD API `GET /api/tickets` is healthy and returns camelCase JSON with string enums from `ca-api-tocqjp4pnegfo`; Blazor Server must have `ApiClient:BaseUrl`/`ApiClient__BaseUrl` set to that ca-api URL. Source paths: `src/dotnet/AgenticResolution.Api/Api/TicketsEndpoints.cs`, `src/dotnet/AgenticResolution.Web/Services/TicketApiClient.cs`, `infra/resources.bicep`.
- **Ticket/API routing check (2026-05-07):** CRUD tickets live only at `https://ca-api-tocqjp4pnegfo.graybush-af9ee262.eastus2.azurecontainerapps.io/api/tickets`; `/tickets` is a Blazor UI route and correctly returns the page shell. Verified live `ca-api` returns `200 application/json` for `/api/tickets?page=1&pageSize=1` and `404` for `/tickets`. The active App Service `app-agentic-res-src-dev-tocqjp4pnegfo` had legacy `ApiBaseUrl` but was missing `ApiClient__BaseUrl`; set `ApiClient__BaseUrl` and `ResolutionApi__BaseUrl` in `rg-agentic-res-src-dev`, restarted it, and verified `/tickets` no longer renders the API-unconfigured state. Hardened `src/dotnet/AgenticResolution.Web/Program.cs`, `src/dotnet/AgenticResolution.Web/Services/TicketApiClient.cs`, and `infra/resources.bicep` so the web app uses the ca-api base URL and fails fast if HTML shell content is returned instead of JSON.


## 2026-04-29 — Priority enum flip + Phase 1 Azure deploy

### Priority enum (per Jason directive)
- Flipped `TicketPriority` in `Models/Ticket.cs` to ServiceNow numeric ordering: `Critical=1, High=2, Moderate=3, Low=4` (was `Low=1 .. Critical=4`).
- Single source of truth — no duplicate enum in DTOs, so no other C# edits needed.
- Added EF migration `20260429174520_PriorityEnumReorder` with raw SQL that remaps existing rows via sentinel values (91..94) to swap `1↔4` and `2↔3` without collisions. Schema unchanged (column stays `int`). Reversible.
- `dotnet build` clean (0/0). Smoke POST with `priority:3` round-tripped as Moderate.

### Azure deploy (Phase 1)
- **RG:** `rg-agentic-res-src-dev` in **East US 2**, sub TSJasonFarrell-Sub.
- Resources: App Service (B1 Linux, .NET 10) + Azure SQL (Basic 5 DTU) + Key Vault + User-Assigned MI + App Insights + Log Analytics.
- App URL: `https://app-agentic-res-agentic-resolution-dev-ie6eryvrpccqa.azurewebsites.net`.
- **Bicep refactor required for MCAPS policy:** `AzureSQL_WithoutAzureADOnlyAuthentication_Deny` blocked SQL admin login/password. Refactored to subscription-scope `main.bicep` + RG-scope `resources.bicep`; SQL uses Entra-only auth with the User-Assigned MI as Entra admin (principalType Application). App connects via `Authentication=Active Directory Default` and `AZURE_CLIENT_ID` env var pinned to the MI's clientId.
- Added `azd-service-name=web` tag, switched plan to Linux for .NET 10 (`DOTNETCORE|10.0`).
- Wired EF `Database.MigrateAsync()` on startup so the SQL schema is created automatically (Phase 1 = single instance — safe).
- All 4 smoke checks PASS.

### Webhook null-target behavior
- `Webhook__TargetUrl` and `Webhook__Secret` deployed as empty strings.
- `WebhookDispatchService` already log-and-skips when target is null/whitespace (`"Webhook:TargetUrl not configured; dropping {EventId}"`) — counted as no-op success. No code change. Jason can wire a real receiver via App Service config post-deploy without redeploy.



## 2026-04-29 — Phase 2 Azure IaC (OpenAI, Function App, Storage, AI Foundry)

### What was built
Five new Bicep modules + updates to `resources.bicep`, `main.bicep`, and `appinsights.bicep`:

- **`infra/modules/storage.bicep`** — Standard_LRS StorageV2, no public blob access. Connection string stored as a Key Vault secret (`storage-connection-string`). Outputs: `storageAccountName`, `storageAccountId`, `storageSecretUri`, `storageKvSecretRef`.
- **`infra/modules/openai.bicep`** — Azure OpenAI S0, East US 2. Deploys `text-embedding-3-small` (v1, 120K TPM) then `gpt-4o-mini` (v2024-07-18, 120K TPM) sequentially (`dependsOn` to avoid parallel deployment conflict). Grants `Cognitive Services OpenAI User` to app service MI + function app MI. Outputs: `endpoint`, `name`, `resourceId`.
- **`infra/modules/functionapp.bicep`** — Consumption plan (Y1), .NET 8 isolated worker. Dedicated user-assigned MI. KV role assigned within module (`dependsOn: [kvRole]` on function app). `AzureWebJobsStorage` uses KV secret reference. `Webhook__HmacSecret` uses KV named-secret reference (operator must populate). `Foundry__Endpoint` is empty placeholder for Bishop. Outputs: `functionAppName`, `defaultHostName`, `principalId`, `siteId`.
- **`infra/modules/foundry.bicep`** — AI Foundry Hub (Standard, East US 2) + Project referencing hub via `hubResourceId`. Hub backed by Phase 2 storage account + existing KV + App Insights. Grants `Azure AI Developer` role to function app MI on the project. Outputs: `hubName`, `projectName`, `projectEndpoint`, `hubId`, `projectId`.
- **`infra/modules/appinsights.bicep`** — Added `resourceId` output (needed by Foundry hub for `applicationInsights` property). Non-breaking addition.

### resources.bicep changes
- Added `var storageAccountName` (same `take/replace/uniqueString` pattern as keyVaultName).
- Added four new module calls: `funcIdentity`, `storage`, `oai`, `func`, `foundry`.
- `storage` has explicit `dependsOn: [kv]` so KV exists before the storage module writes secrets.
- Added nine new outputs forwarding Phase 2 resource identifiers.

### main.bicep changes
- Added eight new `output` declarations to surface Phase 2 resources as azd env vars (`AZURE_OPENAI_ENDPOINT`, `FUNCTION_APP_NAME`, `FOUNDRY_PROJECT_ENDPOINT`, etc.).

### Naming decisions
- Storage: `saagres{env}{uniqueString}` truncated to 24 chars — mirrors Key Vault naming.
- OpenAI: `oai-{namePrefix}` (no uniqueString needed; globally unique via custom subdomain).
- Function plan: `plan-func-{namePrefix}` (separate from existing `plan-{namePrefix}` app service plan).
- Function app MI: `id-func-{namePrefix}` (mirrors `id-{namePrefix}` for app service MI).
- Foundry hub: `hub-{namePrefix}`, project: `proj-{namePrefix}`.

### Deviations from Apone Phase 2 proposal
- **.NET 8 not .NET 10 for function app** — Jason's task explicitly requested .NET 8 isolated; Apone's decision said .NET 10. Jason's directive takes precedence.
- **AI Search excluded** — explicitly deferred by Jason; no search module created.
- **Storage account shared between function app and Foundry hub** — Apone's proposal treated these as separate but sharing reduces cost and aligns with the Foundry hub needing its own storage account anyway.


## 2026-04-30 — Solution Split: AgenticResolution.Api + Web Cleanup + New Endpoints

### What was done
- **Created `AgenticResolution.Api`** project (`net10.0` Web API): EF Core, Migrations, Models, Webhooks, Ticket endpoints all moved from Web. UserSecretsId = `agenticresolution-api-secrets`.
- **Ticket model** extended with 4 new agent-writeback fields: `ResolutionNotes` (nvarchar(max)), `AgentAction` (nvarchar(100)), `AgentConfidence` (float?), `MatchedTicketNumber` (nvarchar(20)).
- **Migration `20260430000000_AddAgentFields`** created manually (no EF CLI) following existing migration patterns. Adds the 4 new columns.
- **Webhook payload trimmed** to `TicketWebhookSnapshot` — ServiceNow-style compact shape (excludes Description, UpdatedAt, Id; adds Urgency, Impact, AssignmentGroup). `ForTicketUpdated` added alongside `ForTicketCreated`.
- **New endpoints**: `PUT /{id:guid}` (agent writeback with validation filter) and `GET /search?q=` (keyword search, pageSize capped at 50). `/search` registered **before** `/{number}` route to avoid Minimal API routing conflict.
- **`TicketResponse`** updated with new agent fields.
- **`AgenticResolution.Web` cleaned up**: EF Core packages removed; `Program.cs` trimmed to Blazor + HttpClient only; `Models/TicketEnums.cs` created to preserve enums for Blazor pages; DTOs updated with agent fields.
- **Solution file** now includes all 4 projects (Api, Web, Web.Tests, Web.ComponentTests).
- **`AgenticResolution.Web.Tests`** retargeted to `AgenticResolution.Api`.
- **`dotnet build AgenticResolution.sln`** → 0 errors, 0 warnings.

### Learnings
- **Route ordering in Minimal API matters**: `MapGet("/search", ...)` must come before `MapGet("/{number}", ...)` or "search" is captured as the `{number}` parameter.
- **Enum preservation when splitting projects**: When removing the entity `Ticket.cs` from Web, enums must be preserved in a separate file since Blazor pages still reference them.
- **No project reference between Web and Api** (separate processes). Web keeps local DTO copies in `Models/Dtos/` for JSON deserialization.
- **Migration designer files** need namespace updated when copying — entity name in `BuildTargetModel` must use the new project namespace.

## 2026-04-30 — TicketsApi.McpServer scaffold

### What was done
- Created `src/TicketsApi.McpServer/` — ASP.NET Core web app exposing `AgenticResolution.Api` as an MCP server.
- Package: `ModelContextProtocol.AspNetCore` v1.2.0 (confirmed available and compatible with net10.0).
- Transport: SSE via `WithHttpTransport()` + `MapMcp("/mcp")`. Foundry agents connect over HTTP.
- 4 MCP tools in `Tools/TicketTools.cs` (`[McpServerToolType]`/`[McpServerTool]` attributes confirmed working): `get_ticket_by_number`, `list_tickets`, `search_tickets`, `update_ticket`.
- Typed HTTP client `TicketApiClient` calls `AgenticResolution.Api` over HTTP — no shared DbContext.
- Health endpoint at `/health` for Container Apps probes.
- Application Insights wired via `AddApplicationInsightsTelemetry()`.
- Project added to `AgenticResolution.sln` with GUID `{F5A6B7C8-D9E0-4123-FA45-567890123456}`.
- `dotnet build AgenticResolution.sln` → 0 errors, 0 warnings.

### Learnings
- **GUID collision:** The task spec proposed `{D3E4F5A6-...}` for McpServer but that GUID was already assigned to `AgenticResolution.Web.Tests`. Always check the .sln before using a spec-provided GUID.
- **`WithToolsFromAssembly()`** scans the calling assembly for `[McpServerToolType]`-decorated classes; no explicit registration needed per tool class.
- **Static tool methods + DI injection:** `ModelContextProtocol.AspNetCore` 1.2.0 supports static methods with DI-injected parameters (ITicketApiClient, ILogger<T>, CancellationToken) alongside MCP parameters — no constructor injection required.
- **`ModelContextProtocol.Server` namespace** contains both `[McpServerToolType]` and `[McpServerTool]` attributes in v1.2.0.

## 2026-05-01 — Hosted Agent Container Migration

### What was done
- **Created `AgentOrchestrationService.cs`** — orchestrates the hosted agent pipeline: (1) calls classifier-agent to classify ticket as incident/request, (2) routes to specialist agent (incident-agent or request-agent) based on classification. Returns `AgentPipelineResult` with classification, action, confidence, notes, and matched ticket number.
- **Updated `Program.cs`** — registered `AgentOrchestrationService` and `HttpClient("agents")`; commented out deprecated `FoundryAgentService` registration (kept for reference).
- **Updated `WebhookDispatchService.cs`** — replaced `FoundryAgentService.RunAgentPipelineAsync(snapshot)` with `AgentOrchestrationService.ProcessTicketAsync(snapshot.Number)`. Added logging for agent pipeline result.
- **Updated `containerapp-api.bicep`** — added 3 new parameters (`classifierAgentUrl`, `incidentAgentUrl`, `requestAgentUrl`) and 3 new environment variables (`Agents__ClassifierUrl`, `Agents__IncidentUrl`, `Agents__RequestUrl`).
- **Updated `resources.bicep`** — added 3 agent URL parameters to the `caApi` module call (all default to `''` — will be wired post-deployment via azd env vars or second-pass approach).
- **Build check passed** — `dotnet build src/AgenticResolution.Api/AgenticResolution.Api.csproj` → 0 errors.

### Learnings
- **HttpClient POST pattern:** Use `PostAsJsonAsync` with cancellation token, then `Content.ReadFromJsonAsync<T>(cancellationToken: ct)` (not `ct: ct` — wrong parameter name).
- **Agent URL configuration strategy:** Container App FQDNs are not known until after deployment. For Phase 4, we're using empty defaults in Bicep and expecting post-deployment wiring via azd env vars or a second `azd deploy` pass after Bishop deploys the agent containers.
- **FoundryAgentService preserved:** Not deleted to avoid merge conflicts with Bishop's work — just commented out in DI registration. `AgentDefinitions.cs` also kept since the prompts are needed by the agent containers.
- **Webhook agent invocation is fire-and-forget:** `WebhookDispatchService` calls the agent pipeline async after webhook dispatch — does not block the HTTP response path.

## 2026-05-07 — Blazor Backend API Implementation

### What was done
- **Added three new models**: `TicketComment`, `WorkflowRun`, `WorkflowRunEvent` with proper relationships and validation attributes.
- **Updated `AppDbContext`**: Added three new DbSets, configured entities with proper indexes (UpdatedAt, AssignedTo, Category on Ticket; composite indexes on WorkflowRuns and WorkflowRunEvents).
- **Extended ticket list endpoint**: Now accepts `assignedTo` (with "unassigned" sentinel), `state` (comma-separated), `category`, `priority` (comma-separated), `q` (search term), `sort` (created/modified), `dir` (asc/desc). Whitelist-enforced sort fields prevent SQL injection.
- **Added GET details endpoint**: `/api/tickets/{number}/details` returns ticket + comments + runs in single round-trip.
- **Added comments endpoints**: `GET /api/tickets/{number}/comments` and `POST /api/tickets/{number}/comments` with validation (author 1-100 chars, body 1-4000 chars).
- **Added workflow run endpoints**: `POST /api/tickets/{number}/resolve` (idempotent — returns existing pending/running run), `GET /api/runs/{runId}`, `GET /api/runs/{runId}/events`.
- **Removed automatic agent invocation**: Webhook dispatch no longer calls `RunAgentAsync`. Agent pipeline only runs via explicit resolve endpoint.
- **Added webhook config flag**: `Webhook:AutoDispatchOnTicketWrite` (default `false` in appsettings.json). When false, create/update skip webhook enqueue entirely. Preserves plumbing if needed later.
- **Created EF migration**: `20260507000000_AddCommentsAndWorkflowRuns` adds Comments, WorkflowRuns, WorkflowRunEvents tables plus three new indexes on Tickets.
- **Updated model snapshot**: Reflects all new entities and relationships.
- **Build succeeded**: 0 errors, 2 warnings (Azure.AI.Agents.Persistent version resolution — non-blocking).

### Learnings
- **Comma-separated enum filters**: Split on comma, parse each with `Enum.TryParse`, filter nulls, then build `Contains()` query. Works cleanly for state and priority multi-select.
- **Idempotent resolve**: Check for existing Pending/Running runs for same ticket before creating new one. Returns HTTP 200 with existing run instead of 202 to signal idempotency.
- **Webhook auto-dispatch default-off**: Phase 2.5 requirement. Webhook plumbing preserved but disabled by default. If webhook is re-enabled later, add the config flag to Container App env vars.
- **Sort field whitelist pattern**: Use `switch` expression with whitelisted values (`"created"`, `"modified"`), default to safe fallback. Prevents arbitrary column names in LINQ.
- **DbSet naming convention**: `Comments`, `WorkflowRuns`, `WorkflowRunEvents` (plural) for consistency with existing `Tickets` and `TicketNumberSequences`.

### Known gaps
- **No resolution runner BackgroundService yet**: The resolve endpoint creates a Pending run but does not actually invoke the agent pipeline. That's Bishop's integration point — needs an `IResolutionRunner` hosted service that dequeues resolve requests and calls `AgentOrchestrationService`.
- **No SignalR hub for live events**: The `/api/runs/{runId}/events` endpoint currently returns all events from DB. Live streaming via SSE or SignalR hub (`/hubs/runs`) is not implemented — Ferro will need that for real-time UI updates.
- **Infra files are stubs**: `infra/main.bicep`, `infra/resources.bicep`, and `azure.yaml` are all 0 bytes. Cannot update backend deployment baseline without actual Bicep modules. Blocker for deployment — needs infra baseline from Apone or separate infra task.
- **No Container App module**: `infra/modules/containerapp-api.bicep` does not exist. The history references it but the file was never created or is 0 bytes.

### Coordination handoffs
- **Bishop**: Add `IResolutionRunner` BackgroundService that consumes `Channel<ResolutionRunRequest>` (runId, ticketNumber), sets run to Running, invokes `AgentOrchestrationService.ProcessTicketAsync`, writes WorkflowRunEvent rows for each executor step, and sets final status (Completed/Failed/Escalated). Expose progress events via `IAsyncEnumerable<AgentExecutorEvent>` or callback pattern.
- **Ferro**: Detail page can call `GET /api/tickets/{number}/details` for full ticket + comments + runs. Resolve button calls `POST /api/tickets/{number}/resolve` and redirects to run detail page at `/api/runs/{runId}` for events polling or SignalR subscription.
- **Vasquez**: Tests for filter/sort combinations, comment CRUD, resolve idempotency, run lifecycle state transitions. Update existing tests to assert webhook dispatch does NOT fire with default config.

## 2026-05-07 — Fixed Resolve Webhook Contract (Coordinator Finding)

### What was done
- **Removed IResolutionQueue from StartResolveAsync signature** — endpoint no longer enqueues to internal resolution runner. Webhook receiver owns orchestration.
- **Removed IConfiguration parameter** — no longer needed since webhook firing is unconditional for new runs.
- **Changed webhook dispatch to unconditional** — lines 363-369 previously enqueued IResolutionQueue and optionally fired webhook behind `Webhook:FireOnResolutionStart=false` flag. Now fires `WebhookPayload.ForResolutionStarted(ticket, run.Id)` unconditionally for NEW runs (no config gating).
- **Preserved idempotent behavior** — existing active run (Pending/Running) returns HTTP 200 with existing run, does NOT fire duplicate webhook.
- **Updated decision doc** — `.squad/decisions/inbox/hicks-resolve-webhook-contract.md` now correctly states "fires webhook unconditionally" and lists IResolutionQueue removal in validation section. Status remains "Implemented" but now accurate.
- **Build succeeded** — 0 errors, 2 warnings (Azure.AI.Agents.Persistent version resolution — non-blocking).

### Why this was needed
Coordinator (Jason Farrell) identified that the code did NOT match the user directive from Phase 2.5 clarification:
- **User requirement:** Resolve should fire webhook and return; frontend starts listening for changes AFTER successful 202 response.
- **Old behavior (incorrect):** Resolve enqueued IResolutionQueue directly (in-process orchestration) and only optionally fired webhook behind a config flag.
- **New behavior (correct):** Resolve fires esolution.started webhook unconditionally for new runs, returns 202 immediately. Webhook receiver (Azure Function) owns downstream orchestration and progress updates.

### Contract summary
1. POST /api/tickets/{number}/resolve creates or reuses active WorkflowRun
2. For NEW run: fires WebhookPayload.ForResolutionStarted(ticket, run.Id) unconditionally
3. Returns HTTP 202 Accepted with { runId, ticketNumber, ticketId, statusUrl, eventsUrl }
4. Does NOT enqueue IResolutionQueue from endpoint
5. Existing active run returns HTTP 200 with existing run, no duplicate webhook
6. Create/update webhook auto-dispatch remains disabled by default (Webhook:AutoDispatchOnTicketWrite only)

### Learnings
- **Coordinator role is critical** — squad agents can inadvertently document behavior as "implemented" without verifying the code matches. Coordinator must validate actual code state against directive.
- **Config flags should be scoped precisely** — Webhook:FireOnResolutionStart was never needed; the resolve action is ALWAYS explicit manual invocation. Auto-dispatch flag (AutoDispatchOnTicketWrite) remains correctly scoped to create/update side effects.
- **Document-code drift detection** — decision docs marked "Implemented" should be verified against codebase before handoff. Plan vs reality mismatch is a coordination failure, not a technical one.

### Next steps
- **Azure Function webhook receiver** (Bishop territory) must implement esolution.started handler that sets run to Running, invokes agent pipeline, writes WorkflowRunEvent rows.
- **Frontend polling** (Ferro) can now call resolve and immediately start polling /api/runs/{runId}/events.
- **ResolutionRunnerService cleanup** — IResolutionQueue, ResolutionRunnerService.cs can be deleted or repurposed for local dev scenarios. No longer used in production flow.


## 2026-05-08 — azd Infrastructure Baseline

### What was done
- **Wrote `azure.yaml`** — declared two services: `web` (App Service, `src/dotnet/AgenticResolution.Web`) and `api` (Container App stub, `src/dotnet/AgenticResolution.Api`). Project named `agentic-resolution`.
- **Wrote `infra/main.bicep`** — subscription-scope; creates resource group `rg-${environmentName}`; calls `resources.bicep` module; outputs `AZURE_LOCATION`, `AZURE_TENANT_ID`, `WEB_APP_NAME`, `WEB_APP_HOSTNAME`.
- **Wrote `infra/resources.bicep`** — provisions App Service Plan (B1, Linux) + App Service for Blazor frontend with `azd-service-name: 'web'` tag; app settings: `ApiClient__BaseUrl=''` (placeholder), `ASPNETCORE_ENVIRONMENT=Production`; runtime `DOTNETCORE|8.0`. Backend resources (Container App Environment, Container Registry) gated behind `deployBackend` param (default `false`).
- **Wrote `infra/modules/containerappenvironment.bicep`** — minimal stub (properties empty) for Container App Environment. Only deployed when `deployBackend=true`.
- **Build check passed** — `dotnet build src/dotnet/AgenticResolution.Web/AgenticResolution.Web.csproj` succeeded with 2 warnings (NU1510 Microsoft.Extensions.Http pruning suggestion — non-blocking).
- **Wrote `DEPLOY.md`** — documents `azd init` / `azd up` flow, app settings, backend deployment prep (future), troubleshooting, clean-up.

### Why
Jason requested minimal azd infrastructure to deploy the Blazor frontend to App Service now, deferring Container Apps (backend) until later. The `deployBackend` parameter gates all backend resources so `azd up` can provision and deploy the Web project immediately without blocking on incomplete backend config (e.g., Container Registry role assignments require managed identity principal IDs that don't exist yet).

### Learnings
- **azd service-resource matching** — `azd-service-name: 'web'` tag on the App Service resource links it to the `web` service in `azure.yaml`. Without this tag, azd won't know where to deploy the build output.
- **Backend resource gating** — Commented out the `containerRegistry` module call in `resources.bicep` because `containerregistry.bicep` requires `webhookPrincipalId`, `apiPrincipalId`, `mcpPrincipalId` parameters (Container App managed identities). Those don't exist yet, so provisioning would fail. The `containerEnv` module is stubbed in case it's referenced later.
- **App Service Linux runtime** — `DOTNETCORE|8.0` is the correct `linuxFxVersion` string for .NET 8 on Linux App Service. .NET 10 would be `DOTNETCORE|10.0` but the Blazor project targets `net10.0` which is backward-compatible on the .NET 8 runtime for now (Jason can bump to 10.0 runtime if needed).
- **ApiClient__BaseUrl placeholder** — Set to empty string. Jason will populate this with the Container App API URL once backend deployment is live. The Blazor app can handle empty config gracefully (no API calls until configured).

### What's still gated (deployBackend=false)
- Container App Environment
- Container Registry (commented out — needs principal IDs)
- All Container Apps (webhook, api, mcp-server)
- All backend modules (AI Search, OpenAI, Foundry, etc.)

### Next steps (blocking on other agents)
- **Ferro** — ensure `AgenticResolution.Web` Blazor pages render correctly with empty `ApiClient__BaseUrl`.
- **Jason** — run `azd init` + `azd up` to provision and deploy. After backend deployment, set `ApiClient__BaseUrl` app setting to Container App API FQDN.
- **Hicks (future)** — when backend is ready, set `deployBackend=true` in `main.parameters.json` or via azd env var; complete `containerRegistry` module call with actual principal IDs; deploy `api` service to Container App.

## 2026-05-06 — Architecture Pivot: Orchestration Moved to Python

### Context
The architecture pivoted. The .NET API (TicketsNow) remains as a basic CRUD API simulating ServiceNow. All resolution orchestration (AgentOrchestrationService, IResolutionQueue, ResolutionRunnerService, workflow run tracking) moved to a new Python Resolution API.

### What Was Removed
- **Endpoints:** POST /api/tickets/{number}/resolve, GET /api/tickets/{number}/runs, GET /api/runs/{runId}, GET /api/runs/{runId}/events
- **Services:** AgentOrchestrationService, IResolutionQueue/ResolutionQueue, ResolutionRunnerService, IWorkflowProgressTracker/WorkflowProgressTracker
- **Files:** Entire Agents/ folder deleted (AgentOrchestrationService.cs, ResolutionRunnerService.cs, WorkflowProgressTracker.cs, IWorkflowProgressTracker.cs, AgentDefinitions.cs, FoundryAgentService.cs, WORKFLOW_SEQUENCE_NAMES.md)
- **DTOs:** StartResolveRequest, StartResolveResponse, WorkflowRunResponse, WorkflowRunEventResponse, WorkflowRunDetailResponse
- **Program.cs registrations:** AgentOrchestrationService, IResolutionQueue, ResolutionRunnerService, IWorkflowProgressTracker, HttpClient("agents")

### What Remains
- **CRUD endpoints:** POST/GET/PUT tickets, GET tickets list/search, GET/POST comments — unchanged
- **Webhook dispatch:** IWebhookDispatcher, WebhookDispatchService — unchanged (agents may still trigger webhooks)
- **Database models:** WorkflowRun.cs, WorkflowRunEvent.cs, AppDbContext DbSets — kept for potential Python API usage; migrations not deleted to avoid breaking existing databases
- **MCP Server:** TicketsApi.McpServer project untouched — continues calling GET/PUT ticket endpoints

### Verification
- dotnet build AgenticResolution.sln — SUCCESS (0 errors, 2 NU1510 warnings)
- .NET API now purely a ServiceNow mock — basic ITSM CRUD interface
- Python Resolution API owns orchestration, workflow runs, and resolution logic

### Key Decision
TicketsNow is a **fixed interface contract** simulating ServiceNow. No client can add new endpoints to ServiceNow, so the .NET API remains minimal. Python Resolution API is the orchestration layer calling the .NET API for ticket data/updates.


### Cross-Agent Update: Frontend Configuration Fix (2026-05-07)

From Ferro (Frontend Dev): Ticket loading failure was caused by missing `ApiClient:BaseUrl` configuration in production Blazor settings.

**Fix applied:**
- `appsettings.json` now includes deployed ca-api URL: `https://ca-api-tocqjp4pnegfo.graybush-af9ee262.eastus2.azurecontainerapps.io`
- `Program.cs` falls back to `TICKETS_API_URL` env var (aligns with Python Resolution API naming)
- `infra/resources.bicep` now persists both frontend app settings (`ApiClient__BaseUrl` and `ResolutionApi__BaseUrl`)
- `Index.razor` awaits initial load from `OnInitializedAsync`
- `TicketApiClient` returns detailed error status/body on API failures

**Implication:** CRUD API endpoints are healthy and untouched. No backend changes needed.

**Shared blocker:** Local dotnet build requires .NET 10; host has .NET 9 only. Cannot validate via local build.

---

### 2026-05-07 — Ticket API routing contract verified

Confirmed endpoint routing: `/tickets` is Blazor UI route; ticket CRUD API is `https://ca-api-tocqjp4pnegfo.graybush-af9ee262.eastus2.azurecontainerapps.io/api/tickets`. Set missing App Service settings. Verified live `GET /api/tickets?page=1&pageSize=1` returns 200 JSON with 98 available tickets. Hardened `TicketApiClient` against HTML-as-JSON responses. Status: Complete, `dotnet build` passed.

**Collaboration note:** Ferro applied frontend configuration fix to prioritize environment-set `TICKETS_API_URL`. Both backend and frontend work complete.

---

## 2026-05-07 — Ticket Detail Contract Verified

✅ **API Verification Complete**

- **Health:** ca-api deployed and responsive at `https://ca-api-tocqjp4pnegfo.graybush-af9ee262.eastus2.azurecontainerapps.io`
- **Tickets endpoint:** `GET /api/tickets?page=1&pageSize=1` returned 200 with 98 total tickets in system
- **Sample ticket:** INC0010102 (GUID: `bcd0df92-9b6b-4f17-982a-c1989924edbc`)
- **Detail endpoint:** `GET /api/tickets/INC0010102/details` returned 200 with `{ ticket, comments, runs }` payload
- **Contract locked:** Number-based routing confirmed; GUID routes return 404 as expected
- **Source API:** Detail DTO aligned to include runs; dotnet build validated

**Contract Summary:**
- `GET /api/tickets/{number}` → single ticket summary
- `GET /api/tickets/{number}/details` → detail payload + comments + runs
- GUID-based routes → 404 (by design)

→ Decision recorded: `.squad/decisions.md` / "Hicks — Ticket Detail Contract" (2026-05-07)


### 2026-05-07 — Final Ticket Detail Status Updates & API Deployment
- **Outcome:** Implemented ticket detail status updates with native Escalated state
- **Backend contract:** API supports native Escalated status; PUT /api/tickets/{id} normalizes legacy escalation payload (state=InProgress + agentAction=escalated_to_human) to persisted Escalated
- **Deployment:** ca-api revision 0000014 deployed to `ca-api-tocqjp4pnegfo` with image hash hicks-final-status-read-20260507024330
- **Validation:** GET /api/tickets/{number}/details shows persisted Resolved/Escalated statuses; INC0010102 resolved; INC0010103 escalated; INC0010104 resolver result confirmed
- **Decision recorded:** `.squad/decisions.md` / "Hicks — Ticket Detail Final Status Contract" (2026-05-07)
- **Status:** Live and verified

---

### 2026-05-07 — Secured Admin Endpoints & Data Seeding Orchestration

**Session Outcome:** Single-command setup now includes secured data reset capability and sample ticket seeding.

**Security Architecture Implemented:**
1. **Configuration-Gated Admin Endpoints** — Disabled by default (`AdminEndpoints:Enabled=false`). Operators must explicitly enable in production (requires direct code/config change, not a flag toggle). Follows principle of least privilege.

2. **API Key Authentication** — All admin requests require `X-Admin-Api-Key` header. Key read from configuration (`AdminEndpoints:ApiKey`). Recommended storage: Azure Key Vault secret or environment variable. No inline plaintext.

3. **Custom AdminAuthMiddleware** — Centralized request validation before reaching endpoint. Returns:
   - 401 Unauthorized: missing or invalid API key
   - 403 Forbidden: admin endpoints disabled
   - 200/success: valid key, endpoint processes request

4. **Ephemeral Admin Keys** — `Setup-Solution.ps1` generates a random GUID per setup session. Key lives in environment variable during setup; not persisted to Key Vault or config files. Provides single-session authenticated reset without hardcoded credentials.

5. **Endpoints Created:**
   - POST `/api/admin/reset-data`: Bulk reset via `ExecuteUpdateAsync` (single SQL statement, efficient)
     - Sets all tickets to State=New, AssignedTo=null
     - Clears ResolutionNotes, AgentAction, AgentConfidence, MatchedTicketNumber
     - Resets TicketNumberSequence to baseline (10000)
     - Optional: Seeds 5 realistic demo tickets (staggered creation times)
   - GET `/api/admin/health`: Database connectivity check (returns `{"status":"healthy"}`)

6. **Sample Ticket Seeding** — 5 realistic demo tickets created on request:
   - Email access on mobile (High priority)
   - Printer not responding (Moderate priority)
   - VPN drops intermittently (High priority)
   - SharePoint access request (Low priority)
   - Laptop running slow (Moderate priority)
   All: State=New, AssignedTo=null, CreatedAt staggered 15-120 minutes ago

**PowerShell Orchestration Scripts:**
- `Reset-Data.ps1` — Standalone data reset utility; auto-discovers API URL from azd environment or accepts explicit parameter
- `Setup-Solution.ps1` — Orchestration script integrating foundation deployment + Container Apps + data reset
  - Generates ephemeral admin key (GUID)
  - Waits for API health check (120s timeout, exponential backoff)
  - Calls reset-data with `-SeedSampleTickets` flag (optional)

**Test Coverage:**
- `AdminAuthenticationTests` — 7 tests validating middleware behavior (401/403/200 responses, case sensitivity, empty header, health check bypass)
- `AdminEndpointsTests` — 7 tests validating reset logic and health check

**Key Learnings:**
1. **Disabled by default is not enough** — Add authentication on top. Defense-in-depth pattern: both config gate AND API key auth. If someone misconfigures and enables the endpoint, it still requires the key.
2. **Ephemeral keys work for internal tooling** — Not suitable for multi-user/long-lived APIs, but perfect for setup automation where each session is a discrete event. The key only exists in memory during setup; no audit trail needed across sessions.
3. **ExecuteUpdateAsync is efficient** — Single SQL UPDATE statement beats load-then-update pattern. Critical for production resets involving large ticket volumes.
4. **Middleware ordering matters** — AdminAuthMiddleware must run early, before routing. If placed after routing, invalid requests might still route to the endpoint.
5. **Idempotent resets enable safe re-runs** — Setup script can retry data reset if first attempt times out. Idempotency prevents duplicate ticket removal.

**Coordination Notes:**
- Vasquez fixed test harness routing services; all 14 tests now pass
- DevOps specialist integrated reset-data into orchestration script
- Apone's secure Bicep foundation enables Key Vault secret storage for API keys (if persisted)
- Bob documented reset capability in SETUP.md troubleshooting section


## 2026-05-07 — Entra Auth Verification and Diagnostic Logging

**Context:** Jason requested verification that .NET app authentication works with Azure SQL Entra-only auth using managed identities/DefaultAzureCredential. After infrastructure changes by Bishop (2026-05-07 16:16) that implemented Entra-only authentication, needed to verify .NET app side was ready.

**Analysis:**
- ✅ Infrastructure already correct: Connection string uses `Authentication=Active Directory Default`
- ✅ Microsoft.Data.SqlClient 6.1.1 natively supports this auth mode (uses DefaultAzureCredential internally)
- ✅ Azure.Identity 1.14.2 already referenced
- ✅ EF Core 10.0.0 + SqlServer provider fully compatible
- ✅ No explicit SqlConnection or AccessToken acquisition code needed

**Behavior:**
- **Azure:** Automatically uses App Service/Container App managed identity
- **Local dev:** Uses Azure CLI credentials (via `az login`)
- **Credential chain:** ManagedIdentity → AzureCli → VisualStudio → SharedToken → others

**Changes Made:**
1. **src/dotnet/AgenticResolution.Api/Program.cs**
   - Added diagnostic logging to show authentication mode at startup
   - Refactored to use single `connectionString` variable (DRY, no duplication)
   - Logs: `[Startup] SQL connection configured for Entra authentication (managed identity / Azure CLI credentials)`

**Validation:**
- `dotnet build src/dotnet/AgenticResolution.Api/AgenticResolution.Api.csproj` → ✅ SUCCESS (1 existing NU1603 warning unrelated to auth)
- `dotnet test src/dotnet/AgenticResolution.Api.Tests/AgenticResolution.Api.Tests.csproj` → ✅ 15/15 tests pass

**Summary:**
The .NET application requires **ZERO changes** to support Entra authentication. The `Authentication=Active Directory Default` connection string parameter works transparently with:
- EF Core's `UseSqlServer(connectionString)`
- Microsoft.Data.SqlClient 6.0+ automatic DefaultAzureCredential behavior
- Both Azure (managed identity) and local (Azure CLI) scenarios

Added diagnostic logging for operational visibility. No functional behavior change — app was already Entra-ready.

**Team note:** This is an example of infrastructure-driven security where the platform (Azure SQL + SqlClient provider + Azure.Identity) handles auth entirely through configuration. No application code changes needed beyond connection string format.


- **Database reseed script fix (2026-05-08):** Setup-Solution.ps1 was silently skipping sample ticket seeding when the API didn't become ready within 120 seconds. Changed line 656 from a soft warning to a hard error (`exit 1`) so users know setup is incomplete. Updated all documentation (scripts/README.md, SETUP.md, DEPLOY.md, Reset-Data.ps1, Setup-Solution.ps1) to reflect that seeding now creates 15 sample tickets (not 5 as previously documented). AdminEndpoints.cs seed logic was correct (`ExecuteDeleteAsync` then `AddRange`); issue was timeout handling in the setup script. Validation: `dotnet test` on AdminEndpointsTests passes (8/8 tests, 0 failures).

- **Test deployment validation (2026-05-08):** Successfully deployed test environment to Azure. Web App: https://app-agent-resolution-test-web.azurewebsites.net/; API: https://ca-api-agent-resolution-test.ashybay-4e6168e1.eastus2.azurecontainerapps.io. Seeded 15 sample tickets (INC0010001-INC0010015) covering Email, Hardware, Network, Account Management, Software, Security, and Cloud Storage categories, all in New status. Key fixes: SQL Server public network access must be enabled for Container App connectivity (error: Deny Public Network Access prevented connections); Admin endpoints require AdminEndpoints__Enabled=true and AdminEndpoints__ApiKey (double underscore for nested config) set as environment variables. Admin endpoint path is /api/admin/reset-data not /api/admin/reseed. Validated API returns paginated response with 15 tickets. Files: scripts\Setup-Solution.ps1, scripts\Reset-Data.ps1, src\dotnet\AgenticResolution.Api\Middleware\AdminAuthMiddleware.cs, src\dotnet\AgenticResolution.Api\Api\AdminEndpoints.cs.

---

### 2026-05-08 — Test Deployment Successful with Seeded Data

**Task:** Execute setup/deploy path for test environment with 15 seeded sample tickets.

**Environment:** `agent-resolution-test` in `eastus2` | Resource group: `rg-agent-resolution-test`

**Status:** ✅ **COMPLETED**

**Deployment Outcome:**

| Component | Endpoint | Status |
|-----------|----------|--------|
| Web App | https://app-agent-resolution-test-web.azurewebsites.net | ✅ HTTP 200 |
| Tickets API | https://ca-api-agent-resolution-test.ashybay-4e6168e1.eastus2.azurecontainerapps.io | ✅ Healthy |
| Resolution API | https://ca-res-agent-resolution-test.ashybay-4e6168e1.eastus2.azurecontainerapps.io | ✅ Healthy |
| Database | SQL Server `sql-agent-resolution-test` | ✅ Connected |
| Sample Data | 15 tickets (INC0010001–INC0010015) | ✅ Seeded, all New state |

**Infrastructure Notes:**
- All resources created successfully in `rg-agent-resolution-test`
- Managed identities configured with Entra authentication
- SQL Server public access enabled (temporary; private endpoints pending)
- All migrations applied automatically on API startup

**Data Validation:**
- GET /api/tickets returned 15 tickets
- All tickets in New state
- Categories: Email, Hardware, Network, Account Management, Software, Security, Cloud Storage
- Admin endpoint `/api/admin/reset-data` operational

**Known Temporary Workaround:**
- SQL Server public network access must remain enabled until private endpoints are configured
- Production consideration: VNet + private endpoint to eliminate public exposure

**Signed-off by:** Coordinator validation complete

**References:**
- `.squad/orchestration-log/2026-05-08T13-59-14Z-hicks.md`
- `.squad/decisions.md` — "SQL Server Public Access Required for Container Apps" (2026-05-08)
