# Backend Migration to Hosted Agent Containers

**Date:** 2026-05-01  
**Author:** Hicks (Backend Developer)  
**Status:** ✅ Implemented  

## Context

The existing `AgenticResolution.Api` used Foundry's deprecated prompt-based agent approach via `Azure.AI.Agents.Persistent` and `FoundryAgentService`. We're migrating to **3 hosted agent containers** that communicate over HTTP:

- `classifier-agent` (Container App `ca-classifier-agent`) — classifies tickets as incident or request
- `incident-agent` (Container App `ca-incident-agent`) — handles incidents
- `request-agent` (Container App `ca-request-agent`) — handles service requests

## Decision

### New Service: AgentOrchestrationService

Created `src/AgenticResolution.Api/Agents/AgentOrchestrationService.cs` to orchestrate the agent pipeline:

1. **Classify** — POST to `{Agents:ClassifierUrl}/process` with `{ ticketNumber }` → returns `{ classification, confidence, rationale }`
2. **Route** — based on classification, POST to incident-agent or request-agent `/process` → returns `{ action, confidence, notes, matchedTicketNumber }`
3. **Return** — combined `AgentPipelineResult` with all fields

**Contract:**
```csharp
public sealed record AgentPipelineResult(
    string Classification,
    double ClassificationConfidence,
    string Action,
    double ResolutionConfidence,
    string Notes,
    string? MatchedTicketNumber);
```

**Configuration:**
- `Agents:ClassifierUrl` — Container App FQDN for classifier-agent
- `Agents:IncidentUrl` — Container App FQDN for incident-agent
- `Agents:RequestUrl` — Container App FQDN for request-agent

### Service Registration

Updated `Program.cs`:
```csharp
builder.Services.AddHttpClient("agents");
builder.Services.AddScoped<AgentOrchestrationService>();
```

Commented out `FoundryAgentService` registration but kept the service file and `AgentDefinitions.cs` for Bishop's reference.

### Webhook Integration

Updated `WebhookDispatchService.cs` to call the new service:
```csharp
var agentSvc = scope.ServiceProvider.GetRequiredService<AgentOrchestrationService>();
var result = await agentSvc.ProcessTicketAsync(snapshot.Number, CancellationToken.None);
```

Added logging for the pipeline result (classification, action, confidence).

### Infrastructure (Bicep)

**`infra/modules/containerapp-api.bicep`:**
- Added 3 parameters: `classifierAgentUrl`, `incidentAgentUrl`, `requestAgentUrl` (all default to `''`)
- Added 3 environment variables: `Agents__ClassifierUrl`, `Agents__IncidentUrl`, `Agents__RequestUrl`

**`infra/resources.bicep`:**
- Passed the 3 agent URL parameters to the `caApi` module call (all empty for now)

**Post-deployment wiring required:** The Container App FQDNs are not known until after the agent containers are deployed. Options:
1. **azd environment variables** — set `CLASSIFIER_AGENT_URL`, `INCIDENT_AGENT_URL`, `REQUEST_AGENT_URL` after Bishop's deployment
2. **Second-pass Bicep** — add outputs from agent container modules and wire through `resources.bicep` → `containerapp-api.bicep`

## Impact

- ✅ **Backward compatible** — `FoundryAgentService` code preserved, just not wired in DI
- ✅ **Contract-first** — agent endpoint contracts defined via record types
- ✅ **Idempotent** — agents can be called multiple times with same ticket number
- ✅ **Fail-safe routing** — defaults to incident-agent if classification is unclear
- ⚠️ **Configuration required** — agent URLs must be set post-deployment for agents to run

## Trade-offs

**Why not delete FoundryAgentService?**  
Bishop may still be working on the deprecated approach in parallel. Keeping the file but commenting out DI avoids merge conflicts.

**Why empty agent URLs in Bicep?**  
Container App FQDNs are only known after deployment. We can't statically wire them. Bishop will deploy the agent containers first, then we can set the URLs via azd env or a second Bicep pass.

**Why fire-and-forget in webhook handler?**  
The webhook handler must respond quickly to ServiceNow. Agent processing happens async after the webhook is dispatched — exactly the same pattern as the deprecated `FoundryAgentService`.

## Verification

- ✅ `dotnet build src/AgenticResolution.Api/AgenticResolution.Api.csproj` → 0 errors
- ✅ `AgentOrchestrationService` registered in DI
- ✅ `WebhookDispatchService` calls the new service
- ✅ Bicep parameters and environment variables added
- ✅ No breaking changes to existing endpoints or models

## Next Steps

1. **Bishop:** Deploy the 3 agent containers and capture their FQDNs
2. **Hicks:** Wire the agent URLs into `containerapp-api.bicep` via azd env or Bicep outputs
3. **Smoke test:** POST a ticket → verify webhook → verify agent pipeline logs in App Insights
