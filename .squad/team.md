# Squad Team

> agentic-resolution

## Coordinator

| Name | Role | Notes |
|------|------|-------|
| Squad | Coordinator | Routes work, enforces handoffs and reviewer gates. |

## Members

| Name | Role | Charter | Status |
|------|------|---------|--------|
| 🏗️ Apone | Lead | `.squad/agents/apone/charter.md` | Active |
| ⚛️ Ferro | Frontend Dev | `.squad/agents/ferro/charter.md` | Active |
| 🔧 Hicks | Backend Dev | `.squad/agents/hicks/charter.md` | Active |
| 🤖 Bishop | AI/Agents Specialist | `.squad/agents/bishop/charter.md` | Active |
| 🧪 Vasquez | Tester | `.squad/agents/vasquez/charter.md` | Active |
| 📋 Scribe | Session Logger | `.squad/agents/scribe/charter.md` | Active |
| 🔄 Ralph | Work Monitor | `.squad/agents/ralph/charter.md` | Active |

## Project Context

- **Owner:** Jason Farrell
- **Project:** agentic-resolution — demo system simulating an automated ServiceNow resolution engine. Blazor frontend captures tickets, persists to SQL Server, fires a webhook; future phases route through Azure AI Search and Foundry Agents (with Agent Framework) for automated resolution or escalation.
- **Stack:** .NET / Blazor on Azure App Service, Azure SQL Server, webhooks, Azure AI Search, Azure AI Foundry Agents (Phase 2+)
- **Phase 1:** Basic Azure resource plan + Blazor frontend. **Do NOT deploy to Azure** during Phase 1.
- **Universe:** Alien
- **Created:** 2026-04-29

## Orchestration Log

**2026-04-29 Phase 1 Closure:**  
Blazor frontend live in Azure (RG: `rg-agentic-res-agentic-resolution-dev`, East US 2; app-service URL captured in decisions). Priority enum + DTO alignment shipped; Bicep refactored for Entra-only SQL auth (MCAPS compliance). Vasquez Phase 2 SQL test plan ready (Testcontainers recommended). Bishop unblocked for Phase 2 AI Search + Foundry Agents integration when Jason kicks off.
