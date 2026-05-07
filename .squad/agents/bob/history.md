# Bob History

## Project Context

- **Owner:** Jason Farrell
- **Project:** agentic-resolution — demo system simulating an automated ServiceNow resolution engine.
- **Stack:** .NET / Blazor on Azure App Service, Azure SQL Server, Azure Container Apps, Azure AI Search, Azure AI Foundry Agents, Bicep, Azure Developer CLI.

## Learnings

- Jason wants setup documentation to avoid long manual checklists; the goal is a single-command setup wherever feasible.

### 2026-05-07 — Documentation Consolidation & Operator Clarity

**Session Outcome:** Created two complementary, self-contained guides that eliminate confusion between foundation-only (`azd up`) and complete setup (`Setup-Solution.ps1`).

**SETUP.md (5,300 words — Operator Focus):**
- Clear prerequisite list (Azure CLI, azd, .NET SDK, SQL password)
- One-command overview with step-by-step summary of what `Setup-Solution.ps1` does
- Usage examples: basic setup, with sample data seeding, CI/CD environment variables, custom parameters
- Verification steps: Azure Portal resource checks, endpoint HTTP tests, ticket state validation
- Troubleshooting section for common failures (ACR build, Container App startup, API health check)
- Forward reference to DEPLOY.md for architecture deep dives

**DEPLOY.md (236 lines — Architecture Reference):**
- Removed redundant step-by-step setup instructions (now in SETUP.md)
- Updated "What Gets Deployed" table with Container Apps details
- Clarified that `azd up` alone = foundation only; full setup = foundation + Container Apps + data reset
- Consolidated Seed Data, Initial Setup, and Backend API sections
- Retained infrastructure context: role requirements, monitoring, troubleshooting, security notes
- Added Quick Reference table for common deployment commands

**Key Design Choice:** Intentional duplication of some setup context in both guides. Each guide must be self-contained:
- Operator reads SETUP.md start-to-finish without referring to DEPLOY.md
- Architect reads DEPLOY.md for infrastructure context without needing SETUP.md
- Cross-references prevent confusion but each guide works standalone

**Impact:** Eliminated ambiguity about "what's manual vs automated." Jason's directive (single-command setup, minimal manual steps) now clearly communicated. Documentation aligns with actual deployment flow.

**Key Learning:** Good operator documentation prioritizes the **happy path** and makes prerequisites explicit. Troubleshooting sections should map error symptoms to root causes and remediation commands (not just "call your admin"). Deep architecture details belong in a separate reference, not inline with setup instructions.
