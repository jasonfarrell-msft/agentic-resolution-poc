# Agent: Bishop - Hosted Agent Scaffolding
**Date:** 2026-05-04  
**Session:** Hosted Agents Migration  
**Scope:** Scaffold 3 hosted agent projects with MCP server wiring

## Mandate
Scaffold three containerized hosted agent projects:
1. **Classifier Agent** - classifies incidents
2. **Incident Agent** - manages incident workflows
3. **Request Agent** - handles request routing

Each agent must:
- Include MCP server integration (connection to already-deployed MCP server)
- Follow containerization best practices (Dockerfile, entry point)
- Expose HTTP API for orchestration calls
- Include health check endpoints

## Structure
```
src/
  classifier-agent/
    Dockerfile
    Program.cs
    AgentService.cs
    MCPIntegration.cs
  incident-agent/
    Dockerfile
    Program.cs
    AgentService.cs
    MCPIntegration.cs
  request-agent/
    Dockerfile
    Program.cs
    AgentService.cs
    MCPIntegration.cs
```

## Status
- [ ] Classifier agent scaffold complete
- [ ] Incident agent scaffold complete
- [ ] Request agent scaffold complete
- [ ] MCP wiring validated in all three agents
- [ ] Dockerfiles validated
- [ ] HTTP endpoints confirmed functional

## Notes
- MCP server endpoint: TBD (provided by deployment environment)
- Base image: mcr.microsoft.com/dotnet/aspnet:9.0
- Port mapping: TBD (coordinated with infra)
