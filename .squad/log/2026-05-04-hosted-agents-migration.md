# Session: Hosted Agents Migration
**Date:** 2026-05-04  
**Status:** In Progress

## Session Topic
Migrate from deprecated `Azure.AI.Agents.Persistent` SDK to containerized hosted agents with MCP server integration.

## Agents Active
- **Bishop**: Scaffolding 3 hosted agent projects (classifier, incident, request) with MCP wiring
- **Hicks**: Updating AgenticResolution.Api and infra Bicep for hosted agent HTTP calls

## Goal
1. Scaffold three containerized hosted agent projects (classifier, incident, request)
2. Wire MCP server integration into each agent container
3. Update AgenticResolution.Api to make HTTP calls to hosted agents
4. Update Azure infrastructure (Bicep) to deploy hosted agent containers
5. Validate end-to-end hosted agent orchestration

## Key Context
- **Migration Driver**: Azure.AI.Agents.Persistent is deprecated; transitioning to containerized hosted agents
- **Architecture**: One container per agent (classifier, incident, request)
- **Integration Point**: MCP server already deployed and available for agent connections
- **Target Deployment**: Azure Container Instances or equivalent

## Deliverables
- [ ] Bishop: 3 scaffold projects with MCP server wiring complete
- [ ] Hicks: API updated to call hosted agents via HTTP
- [ ] Hicks: Bicep templates updated for hosted agent container deployment
- [ ] End-to-end integration validation
