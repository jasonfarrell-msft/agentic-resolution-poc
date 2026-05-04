# Agent: Hicks - API & Infrastructure Updates
**Date:** 2026-05-04  
**Session:** Hosted Agents Migration  
**Scope:** Update AgenticResolution.Api and Bicep infrastructure for hosted agent deployment

## Mandate
1. **API Updates (AgenticResolution.Api)**
   - Replace deprecated `Azure.AI.Agents.Persistent` SDK calls
   - Implement HTTP client calls to classifier, incident, and request agents
   - Add resilience patterns (retry, timeout, circuit breaker)
   - Update agent orchestration logic

2. **Infrastructure Updates (Bicep)**
   - Define hosted agent container deployments
   - Configure container networking and ports
   - Set up MCP server integration environment variables
   - Define health checks and auto-restart policies

## Structure
```
AgenticResolution.Api/
  Services/
    AgentOrchestration/
      ClassifierAgentClient.cs
      IncidentAgentClient.cs
      RequestAgentClient.cs
      HostedAgentOrchestratorService.cs
  Program.cs (register services, DI)

infra/
  hosted-agents.bicep
  container-configs/
    classifier.json
    incident.json
    request.json
```

## Status
- [ ] Remove Azure.AI.Agents.Persistent dependencies
- [ ] Implement HTTP agent clients (Classifier, Incident, Request)
- [ ] Wire resilience patterns (Polly)
- [ ] Update AgenticResolution.Api Program.cs with new service registrations
- [ ] Create Bicep templates for hosted agent container deployment
- [ ] Configure environment variables for MCP server connectivity
- [ ] Validate API endpoints reach hosted agents
- [ ] Load test agent orchestration

## Notes
- Hosted agents will be deployed to Azure Container Instances (ACI) or App Service
- HTTP endpoints: classifier-agent:5000, incident-agent:5001, request-agent:5002 (TBD based on actual deployment)
- Resilience: Implement 3-retry policy with exponential backoff
- Monitoring: Integrate with existing Application Insights if present
