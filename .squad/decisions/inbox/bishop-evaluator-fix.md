# Bishop: Evaluator "No KB Documentation Found" Fix

**Date:** 2026-05-12  
**Author:** Bishop (AI/Agents Specialist)  
**Status:** Implemented & Deployed  
**Ticket context:** INC0010019 ("OneDrive files show 'locked by another user' preventing edits")

---

## Problem

The evaluator stage consistently reported "No KB documentation found" despite KB0001012 ("Resolving 'File Locked by Another User' Errors in SharePoint/OneDrive") being a perfect match. Drake observed: "the evaluator likely isn't invoking `search_kb` at all."

---

## Root Cause

**Two bugs in both decomposer agents (not the evaluator):**

### Bug 1 — Wrong tool name in system prompts
Both `incident_decomposer` and `request_decomposer` instructed the agent:
```
Call search_knowledge_base MULTIPLE TIMES
```
The actual MCP tool is named `search_kb`. The nonexistent tool caused all KB searches to silently fail, resulting in "No KB documentation found" answers propagated to the evaluator.

### Bug 2 — Missing second-step get_kb_article call
`search_kb` returns only article titles, categories, and tags — **not body text**. To read the full resolution steps, the agent must call `get_kb_article` after identifying relevant articles. Neither decomposer prompt mentioned this. Even with a correct tool name, agents would have had no substantive content to synthesize answers from.

---

## Architecture Clarification

- **Evaluator** — pure reasoning agent with NO tools (correct by design). It receives pre-fetched KB data from the decomposer via `ResolutionAnalysis`. The evaluator never calls KB directly.
- **Decomposers** — own all KB retrieval. They have `create_mcp_tool()` registered. The MCP tool exposes both `search_kb` and `get_kb_article`.
- **KB search endpoint** — `GET /api/kb?q={terms}` (NOT `/api/kb/search` which doesn't exist). The MCP `KbApiClient` already uses the correct URL; the only issue was the agent prompt.

---

## Fix

Updated system prompts in both decomposers:

### `src/python/agents/incident_decomposer/__init__.py`
- STEP 3: Changed `Call search_knowledge_base` → `Call search_kb`
- Added explicit two-step KB retrieval instruction:
  ```
  Use BOTH tools for each question:
    1. Call search_kb with short keyword terms — returns titles/tags/categories
    2. For each article that looks relevant, call get_kb_article to retrieve the full body text
    Do NOT try to answer from search_kb results alone — you need the full article body.
  ```
- CRITICAL REMINDERS: Updated tool name references

### `src/python/agents/request_decomposer/__init__.py`
- Same changes for the fulfillment path

---

## Deployment

- **Image tag:** `res-eval-20260512132928`
- **Registry:** `cragentresolutiontest4.azurecr.io`
- **Container App:** `ca-res-agent-resolution-test4` (rg-agent-resolution-test4)
- **Status:** provisioningState: Succeeded, runningStatus: Running
- **Commit:** `1eef739`

---

## Expected Behavior After Fix

1. INC0010019 enters workflow → Classifier → IncidentFetch → IncidentDecomposer
2. Decomposer calls `search_kb("locked OneDrive")` → finds KB0001012 in results
3. Decomposer calls `get_kb_article("KB0001012")` → receives full body with Step 1–4 resolution
4. Decomposer synthesizes answers, sets `preliminary_confidence ≥ 0.85`
5. Evaluator receives `ResolutionAnalysis` with populated answers → scores ≥ 0.80
6. Workflow routes to ResolutionExecutor → ticket auto-resolved

---

## Integration Notes

- **Drake:** Re-run E2E test for INC0010019. Should now see KB hits in decomposer output and confidence ≥0.80.
- **Vasquez:** Add test coverage for decomposer tool invocation sequence (`search_kb` → `get_kb_article`).
- **No changes needed** to evaluator, workflow orchestration, or .NET API.
