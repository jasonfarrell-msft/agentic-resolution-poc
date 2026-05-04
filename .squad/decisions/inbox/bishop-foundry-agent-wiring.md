# Decision: Foundry Agent Wiring

**Author:** Bishop  
**Date:** 2026-04-30  
**Status:** Implemented

## Context

The system now has a working webhook pipeline (ticket → DB → HMAC-signed webhook). The task was to wire two Foundry agents that use the MCP server as their tool source, triggered by the webhook.

## Decisions

### 1. SDK: Azure.AI.Projects 1.0.0-beta.9 + Azure.AI.Agents.Persistent 1.2.0-beta.8

- `Azure.AI.Projects` 1.0.0-beta.9 is the current stable beta for `AIProjectClient`.
- `Azure.AI.Agents.Persistent` 1.2.0-beta.8 is required for `MCPToolDefinition` (not present in 1.1.0 or below).
- The packages are separate; `Azure.AI.Projects` does NOT auto-install `Azure.AI.Agents.Persistent` as a dependency — must be added explicitly.
- `PersistentAgentsClient` has no public constructor; must be obtained via the `GetPersistentAgentsClient()` extension method on `AIProjectClient` from the `Azure.AI.Agents.Persistent` package.

### 2. AIProjectClient endpoint format

- The new (beta.9) endpoint format is `https://{aiservices-account}.services.ai.azure.com/api/projects/{project-name}`.
- The `.azure/agentic-resolution-dev/.env` `FOUNDRY_PROJECT_ENDPOINT` value is in the old cognitiveservices format and cannot be used directly with `AIProjectClient` beta.9.
- The correct endpoint is: `https://oai-agentic-res-agentic-resolution-dev.services.ai.azure.com/api/projects/proj-agentic-res-agentic-resoluti`
- Config key: `Foundry__ProjectEndpoint` (falls back to `Foundry__Endpoint`).

### 3. Native MCP tool integration

- Used `MCPToolDefinition("mcp-tickets", mcpServerUrl)` — native MCP support in the agents SDK.
- The Foundry backend connects to the MCP SSE endpoint and discovers tools automatically.
- No manual function dispatch loop is needed (unlike `FunctionToolDefinition` which requires explicit tool call handling).

### 4. Agent naming + "get or create" pattern

- Agent names are stable constants in `AgentDefinitions.cs`.
- On every agent run, the service scans existing agents by name to avoid creating duplicates.
- Agent IDs are cached in a `static ConcurrentDictionary` to avoid repeated list calls.

### 5. Write-back via AppDbContext (not ITicketApiClient)

- `ITicketApiClient` is defined in `TicketsApi.McpServer`, which would create a circular project reference.
- `FoundryAgentService` uses `AppDbContext` directly for write-back.
- Write-back sets `AgentAction`, `AgentConfidence`, `ResolutionNotes`, `MatchedTicketNumber`, and advances state to `Resolved` if confidence ≥ 0.8.

### 6. Fire-and-forget agent invocation

- `WebhookDispatchService` is a `BackgroundService` (singleton lifetime).
- `FoundryAgentService` is scoped, so it's resolved via `IServiceScopeFactory` in a new scope per agent run.
- The agent run is fired with `_ = Task.Run(...)` (not awaited), so webhook dispatch is not blocked.
- Errors are caught and logged at `LogError` level.

### 7. Graceful degradation

- If `Foundry__ProjectEndpoint` is not configured, `AIProjectClient` registers as `null!` and agent service calls are skipped (no exception thrown on startup).
- This allows the API to run in environments without AI configured (dev, test).

## Alternatives Rejected

- **FunctionToolDefinition + manual dispatch**: More complex, requires a tool-call polling loop. Rejected in favour of native MCPToolDefinition.
- **ITicketApiClient for write-back**: Would cause circular project reference (McpServer ↔ Api). Rejected.
- **Singleton FoundryAgentService**: Task spec said Scoped. Static cache handles the cross-instance agent ID sharing.
