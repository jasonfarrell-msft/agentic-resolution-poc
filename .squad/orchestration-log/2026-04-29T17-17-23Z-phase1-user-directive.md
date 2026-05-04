# Orchestration Log — Phase 1 User Directive

**Timestamp:** 2026-04-29T17:17:23Z  
**Captured By:** Copilot Coordinator  
**Source:** Jason Farrell (MSFT)  
**Phase:** Phase 1 Scope Lock  

## Directive Summary

### 1. Tech Stack — .NET 10 Across the Board

- Use .NET 10 for the entire solution: Blazor app, API endpoints, tests, and libraries.
- All agents respect this version floor; no .NET 9 downgrade or framework selection debates.

### 2. Skip Local-Dev Story

- Do NOT scaffold Docker SQL Server setup in Phase 1.
- Do NOT document `dotnet user-secrets` workflows.
- Do NOT stand up webhook.site stubs or receiver mocks in the codebase.
- Local-dev concerns are out of scope; Phase 2 will revisit if needed.

### 3. Open Questions — Accepted with Apone's Defaults

- **Category:** free-text (no fixed enum)
- **HMAC Secret:** static secret acceptable for Phase 1
- **Ticket Number Format:** `INC0010001` ServiceNow-style (7-digit, INC-prefixed)

## Rationale

User direction during Phase 1 kickoff to ensure Hicks, Ferro, Vasquez, and Bishop align on stack, scope, and naming conventions. Recorded for long-term team memory and decision auditability.

## Status

✅ Captured and distributed to all Phase 1 agents
