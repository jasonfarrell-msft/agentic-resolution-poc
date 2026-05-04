# Decision: Migrate to Foundry Hosted Agents

**Date:** 2026-05-04  
**By:** Bishop (AI/Agents Specialist)  
**Status:** Implemented

## Context

We were using `Azure.AI.Agents.Persistent` (deprecated) with prompt-based agents deployed to Foundry. The SDK's `require_approval` feature for tool calls is broken on East US 2, causing agent runs to hang indefinitely waiting for approval confirmations that never arrive.

The workflow was:
1. API creates an agent run with `require_approval=true`
2. Agent requests MCP tool call (e.g., `get_ticket_by_number`)
3. API polls for `RequiresAction` status → submits approval → polls again
4. **Problem:** Approval never processed; run stuck in `RequiresAction` forever

## Decision

**Migrate from deprecated SDK to Foundry Hosted Agents** — containerized C# code running as separate microservices inside Azure AI Foundry Agent Service.

### Architecture

Instead of one API calling Foundry's prompt agents via SDK, we now have:

```
AgenticResolution.Api (Container App)
   ↓ HTTP POST /invocations
Hosted Agent Container Apps (3 separate services):
   - classifier-agent (Container App)
   - incident-agent (Container App)
   - request-agent (Container App)
   ↓ HTTP call to MCP server
TicketsApi.McpServer (Container App)
   → get_ticket_by_number, search_tickets, update_ticket
```

### Implementation

Created 3 new ASP.NET Core 9 minimal API projects:

1. **`src/Agents.Classifier`**
   - System prompt: `ClassificationAgentPrompt` (from `AgentDefinitions.cs`)
   - Endpoint: `POST /invocations`
   - Logic: Extract ticket number → call MCP `get_ticket_by_number` → classify as "incident" or "request" → return JSON
   - Returns: `{ classification, confidence, rationale }`

2. **`src/Agents.Incident`**
   - System prompt: `IncidentAgentPrompt`
   - Logic: Get ticket → search similar resolved tickets → auto-resolve (≥0.8 confidence) or escalate
   - Returns: `{ action, confidence, notes, matchedTicketNumber }`

3. **`src/Agents.Request`**
   - System prompt: `RequestAgentPrompt`
   - Logic: Get ticket → identify standard/complex request type → auto-queue or flag for approval
   - Returns: `{ action, confidence, notes }`

Each project:
- **No Azure.AI.Projects dependency** — agents call MCP server directly via HTTP (JSON-RPC 2.0)
- **Invocations protocol:** `POST /invocations` accepts `{ "Message": "...", "SessionId": "..." }`
- **Health endpoint:** `GET /health` for Container Apps liveness probes
- **Dockerfile** for Container Apps deployment
- **Rule-based logic placeholder** — in production, these will call Azure OpenAI with system prompts

### Why Hosted Agents?

1. **No approval gate** — agent code calls MCP directly, no SDK polling loop, no broken `require_approval`
2. **Independent scaling** — each agent is a separate Container App, scales independently
3. **Simpler debugging** — direct HTTP calls, structured logging, no opaque SDK state
4. **Resilient to SDK changes** — not tied to deprecated packages or Foundry SDK versioning

### Tooling Notes

- `azd ai agent init` extension exists (v0.1.29-preview) but was too interactive for automation
- Manually scaffolded projects using ASP.NET Core minimal API template
- Avoided `Microsoft.Azure.AI.Agent.Server.*` packages (beta, unnecessary for simple invocations endpoints)

## Next Steps

1. **Add Bicep modules** for 3 new Container Apps (classifier-agent, incident-agent, request-agent)
2. **Update AgenticResolution.Api** to call hosted agent endpoints instead of deprecated `PersistentAgentsClient`
3. **Replace rule-based logic** with Azure OpenAI calls using system prompts
4. **Remove deprecated SDK** once migration is verified

## Trade-offs

**Pros:**
- No approval gate blocking issues
- Clean separation of concerns (each agent is a microservice)
- Direct MCP calls — faster, simpler, more observable

**Cons:**
- 3 additional Container Apps to deploy/manage (vs 1 API calling Foundry)
- Need Bicep IaC for 3 new services
- Agents don't benefit from Foundry's managed orchestration (yet — will add once Foundry Hosted Agents feature matures)

**Mitigation:**
- Container Apps are lightweight and cost-effective
- `azd up` automates multi-service deployment
- System prompts still versioned as code in `AgentDefinitions.cs`
