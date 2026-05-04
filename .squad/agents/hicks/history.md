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

## Learnings

- **Validation:** DataAnnotations + a generic `ValidationFilter<T>` endpoint filter. In-box, no extra dep, swap-out is cheap if Phase 2 needs FluentValidation.
- **Webhook payload:** snake_case JSON `{ event_id, event_type, timestamp, ticket{...} }`. HMAC-SHA256 over the raw body, header `X-Resolution-Signature: sha256=<hex>`. `event_id` is the receiver-side dedup key.
- **Retry policy:** 3 retries on top of initial attempt (4 total) at 1s, 4s, 16s. No jitter (single dispatcher). Permanent failure logs Error and drops; no DLQ in Phase 1.
- **Project layout:** Single Blazor Server project per Apone. My code under `src/AgenticResolution.Web/{Api,Data,Models,Webhooks,Migrations}/`; Ferro under `Components/`, `Pages/`, `Layout/`, `Services/`. `Program.cs` is shared with banner-comment regions for clean merges.
- **Ticket numbers:** DB-backed counter (`TicketNumberSequences`, seeded LastValue=10000) incremented atomically via `UPDATE … OUTPUT INSERTED.LastValue`. First ticket = `INC0010001`.
- **Coordination caveat:** EF migration uses default timestamp prefix, not literal `0001_Initial`. Functionally equivalent; renaming breaks the EF tooling.
- **AI Services vs OpenAI resource kind:** `Microsoft.CognitiveServices/accounts` with `kind: 'OpenAI'` is deprecated. The correct modern resource is `kind: 'AIServices'`, which is Foundry-integrated and supports the same model deployments. AI Foundry hubs connect to the AI Services account via a `Microsoft.MachineLearningServices/workspaces/connections` child resource (`category: 'AIServices'`, `authType: 'AAD'`). Always use `AIServices` going forward; standalone `OpenAI` kind will eventually be retired.
- **Modern Foundry resource type (2025+):** `kind: 'AIFoundry'` does NOT exist as a CognitiveServices kind. The correct modern approach is: (1) `Microsoft.CognitiveServices/accounts` with `kind: 'AIServices'` and `allowProjectManagement: true` — this account IS the Foundry hub; (2) `Microsoft.CognitiveServices/accounts/projects` as child resources for projects. The old `Microsoft.MachineLearningServices/workspaces` with `kind: 'Hub'` / `kind: 'Project'` is the deprecated "Foundry classic" approach and should not be used. No separate ML workspace, no workspace connections needed — the AIServices account and its projects coexist natively. Use API version `2025-06-01` or later for both resources. Project endpoint = AIServices account endpoint; Agents SDK connects to `{endpoint}/projects/{projectName}`. `hubName` is no longer a separate resource name — the AIServices account IS the hub.
- **Container App KV secrets vs App Service KV references:** App Service (and Function Apps) use an inline reference syntax in app settings: `@Microsoft.KeyVault(VaultName=...;SecretName=...)` — the platform resolves these transparently. Container Apps have NO such inline syntax. Instead, declare a `secrets` array at the `configuration` level with `{ name, keyVaultUrl, identity }` (identity = user-assigned MI resource ID), then reference by `secretRef: '<secret-name>'` in the container's env vars. The MI must hold `Key Vault Secrets User` on the vault. Log Analytics also requires `workspace.properties.customerId` (a GUID) for the CAE `appLogsConfiguration`, not the ARM resource ID — add a `customerId` output to the loganalytics module and pass it through.


## 2026-04-29 — Priority enum flip + Phase 1 Azure deploy

### Priority enum (per Jason directive)
- Flipped `TicketPriority` in `Models/Ticket.cs` to ServiceNow numeric ordering: `Critical=1, High=2, Moderate=3, Low=4` (was `Low=1 .. Critical=4`).
- Single source of truth — no duplicate enum in DTOs, so no other C# edits needed.
- Added EF migration `20260429174520_PriorityEnumReorder` with raw SQL that remaps existing rows via sentinel values (91..94) to swap `1↔4` and `2↔3` without collisions. Schema unchanged (column stays `int`). Reversible.
- `dotnet build` clean (0/0). Smoke POST with `priority:3` round-tripped as Moderate.

### Azure deploy (Phase 1)
- **RG:** `rg-agentic-res-agentic-resolution-dev` in **East US 2**, sub TSJasonFarrell-Sub.
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
