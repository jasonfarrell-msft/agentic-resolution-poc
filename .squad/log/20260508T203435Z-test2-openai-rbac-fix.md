# Session: test2 Azure OpenAI RBAC Fix

**Date:** 2026-05-08  
**Agents:** Hicks, Bishop  
**Issue:** test2 Resolution API failed with Azure OpenAI 401 PermissionDenied

## Resolution

**Root Cause:** Missing data-plane RBAC for Resolution API managed identity (`c6b82506-1e92-49b1-8e4b-962defc93a9f`).

**Fix:** Granted `Cognitive Services OpenAI User` on `oai-agentic-res-src-dev`; restarted revision.

**Outcome:** `POST /resolve` now reaches terminal `resolved` state.

**Propagated:** Updated `scripts\Setup-Solution.ps1` to assign role automatically on future deployments.

## Decision Recorded

See `.squad/decisions/inbox/{bishop,hicks}-openai-rbac.md` merged to `decisions.md`.
