# E2E Test Report - Round 1
**Test Engineer:** Drake  
**Date:** 2026-05-12 01:46 UTC  
**Ticket:** INC0010019  
**Status:** ❌ FAILED

---

## Test Execution Summary

### Round 1: Baseline Test (2026-05-12 01:45 UTC)

**Pre-Test Ticket State:**
- State: `New`
- Agent Action: `escalated_to_human`
- Agent Confidence: 0.20
- Prior failure: "Escalated to Cha Hae-In (Microsoft 365): automated confidence 0.20 below threshold"

**Test Execution:**
1. ✅ Ticket retrieved successfully via API (contains full description, category, priority)
2. ✅ Resolution workflow triggered via `POST /resolve`
3. ❌ **Workflow failed**: MCP tools returned empty ticket data to agents

---

## Failure Analysis

### Critical Finding: MCP Tools Work When Called Directly, But Fail During Workflow

**Direct MCP Tool Test (Manual):**
```bash
# Manual MCP call to get_ticket_by_number
curl -X POST https://ca-mcp-agent-resolution-test4.../
  -d '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"get_ticket_by_number","arguments":{"ticket_number":"INC0010019"}}}'

# RESULT: ✅ SUCCESS - Full ticket data returned including:
- description: "Multiple users in the Finance department report..."
- category: "Cloud Storage"
- priority: "High"
- All fields populated correctly
```

**Workflow MCP Call (During Resolution):**
```
SSE Stage: request_fetch
Status: completed
Result: {
  "ticket_number": "INC0010019",
  "ticket_id": "INC0010019",
  "short_description": "INC0010019",
  "ticket_description": "",      ← EMPTY
  "ticket_category": "",          ← EMPTY
  "ticket_priority": "",          ← EMPTY
  "ticket_type": "request"
}
```

### Root Cause: Agent Framework → MCP Communication Issue

**Evidence:**
1. MCP server is healthy (`/health` returns OK)
2. MCP tools/list returns all 6 tools including `get_ticket_by_number`
3. Manual tool calls via JSON-RPC work perfectly
4. BUT: Agent Framework's `MCPStreamableHTTPTool` calls during workflow return empty data

**Hypothesis:**
The Python Agent Framework's `MCPStreamableHTTPTool` is NOT successfully invoking MCP tools during agent execution. Possible causes:
- Agent Framework may be sending malformed JSON-RPC requests
- MCP tool invocation may be failing silently within the Agent Framework
- Error handling in Agent Framework may be suppressing MCP failures
- Agents may not be waiting for async MCP responses before proceeding

---

## SSE Stream Analysis (Round 1)

### Stage 1: Classifier
- Status: ✅ Completed
- Classification: `request` (WRONG — should be `incident`)
- Ticket data: Only has ticket_number, NO description/category/priority
- **Problem:** Classifier couldn't retrieve ticket data to make correct classification

### Stage 2: Request Fetch
- Status: ⚠️ Completed with empty data
- ticket_description: `""` (empty)
- ticket_category: `""` (empty)
- ticket_priority: `""` (empty)
- **Problem:** MCP tool call failed silently

### Stage 3: Request Decomposer
- Status: ❌ Failed
- Core problem: "Problem analysis not available"
- Questions: 0
- Preliminary confidence: 0.0
- **Problem:** No ticket data to analyze

### Stage 4: Evaluator
- Status: ❌ Failed
- Confidence: 0.0
- KB source: `""` (empty)
- **Problem:** No meaningful input to evaluate

### Stage 5: Escalation
- Status: ⚠️ Executed (fallback)
- Output: "I'm unable to retrieve the full details for ticket INC0010019 due to a system error when accessing its information"
- Assignee: Go Gun-Hee (Service Desk Tier 2) — generic catch-all
- **Problem:** System acknowledges MCP tool failure but can't resolve ticket

---

## Expected vs Actual Behavior

### Expected (Per User Requirements):
1. ✅ Ticket state starts as `New`
2. ❌ Classifier classifies as INCIDENT (actual: classified as REQUEST)
3. ❌ MCP tool returns real ticket data (actual: empty fields)
4. ❌ KB search finds KB0001012 (actual: no search performed due to missing ticket data)
5. ❌ Confidence >= 0.80 (actual: 0.0)
6. ❌ Final state = Resolved (actual: remains `New`, not updated)
7. ❌ agent_action = "auto_resolved" (actual: still shows old "escalated_to_human")

---

## Verification Tests Performed

### ✅ KB Article Exists
```bash
curl https://ca-api-agent-resolution-test4.../api/kb/KB0001012
```
**Result:** KB0001012 exists with full content about "File Locked by Another User" errors in SharePoint/OneDrive. Perfect match for INC0010019 symptoms.

### ✅ MCP Server Health
```bash
curl https://ca-mcp-agent-resolution-test4.../health
```
**Result:** `{"status":"Healthy","timestamp":"2026-05-12T01:45:59..."}`

### ✅ MCP Initialize
```bash
curl -X POST https://ca-mcp-agent-resolution-test4.../ -d '{"jsonrpc":"2.0","id":1,"method":"initialize",...}'
```
**Result:** Success, protocol version 2024-11-05, server name "TicketsApi.McpServer"

### ✅ MCP Tools List
```bash
curl -X POST https://ca-mcp-agent-resolution-test4.../ -d '{"jsonrpc":"2.0","id":2,"method":"tools/list"}'
```
**Result:** Returns 6 tools: search_tickets, search_kb, get_kb_article, list_tickets, get_ticket_by_number, update_ticket

### ✅ MCP Get Ticket Tool (Direct Call)
```bash
curl -X POST https://ca-mcp-agent-resolution-test4.../ -d '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"get_ticket_by_number","arguments":{"ticket_number":"INC0010019"}}}'
```
**Result:** Full ticket JSON with all fields populated correctly

### ❌ Agent Framework MCP Tool Invocation
**Result:** Empty ticket data returned during workflow execution

---

## Bishop's Diagnosis Confirmation

Bishop's diagnosis from `.squad/decisions/inbox/bishop-0019-diagnosis.md` stated:
> "The MCP server is returning HTTP 404 when agents call tools via the Model Context Protocol."

**Drake's Update:**
- MCP server is NOT returning 404 when called via standard JSON-RPC
- The problem is specifically in how Agent Framework's `MCPStreamableHTTPTool` communicates with the MCP server
- This suggests the issue is in the Python Agent Framework library, NOT the MCP server itself

---

## Recommended Next Steps

### Immediate Action Required:
1. **Enable Agent Framework debug logging** in Python resolution container
   - Set env var: `AGENT_FRAMEWORK_LOG_LEVEL=DEBUG`
   - Capture MCP tool invocation requests/responses
   - Look for JSON-RPC errors or malformed requests

2. **Inspect Agent Framework MCP Tool Implementation**
   - File: `src/python/shared/mcp_tools.py`
   - Check `MCPStreamableHTTPTool` initialization parameters
   - Verify URL format, headers, protocol version compatibility

3. **Add Fallback Direct API Calls (Workaround)**
   - Modify agents to use direct HTTP calls to Tickets API as backup
   - Example: `https://ca-api-agent-resolution-test4.../api/tickets/{number}`
   - This bypasses Agent Framework's MCP layer entirely

4. **Test with Agent Framework Diagnostics**
   - Add logging to `fetch_request_details()` and `fetch_incident_details()` in `workflow/__init__.py`
   - Capture raw agent responses before JSON parsing
   - Check if agents are receiving error messages from MCP tool calls

---

## Files to Investigate

**Working Correctly:**
- `src/dotnet/TicketsApi.McpServer/Program.cs` ✅ (MCP server routes and tool registration)
- `src/dotnet/TicketsApi.McpServer/Tools/TicketTools.cs` ✅ (Tool implementations)
- `src/dotnet/AgenticResolution.Api/Api/TicketsEndpoints.cs` ✅ (CRUD API endpoints)

**Needs Investigation:**
- `src/python/shared/mcp_tools.py` ⚠️ (Agent Framework MCP tool creation)
- `src/python/agents/classifier/__init__.py` ⚠️ (How agents use MCP tools)
- `src/python/agents/request/__init__.py` ⚠️ (Request agent MCP usage)
- `src/python/agents/incident/__init__.py` ⚠️ (Incident agent MCP usage)
- Agent Framework library source code ⚠️ (External dependency)

---

## Test Artifacts

- **SSE Stream:** `C:\Projects\aes\agentic-resolution\round1-sse-stream.txt`
- **Ticket State (Pre-Test):** State=New, confidence=0.20, action=escalated_to_human
- **Ticket State (Post-Test):** UNCHANGED (workflow did not update ticket due to failure)
- **API Endpoint:** `https://ca-api-agent-resolution-test4.kindwater-9dbc4735.eastus2.azurecontainerapps.io`
- **MCP Endpoint:** `https://ca-mcp-agent-resolution-test4.kindwater-9dbc4735.eastus2.azurecontainerapps.io`

---

## Conclusion

**Round 1: FAILED**

INC0010019 cannot resolve automatically because the Agent Framework's MCP tool integration is broken. The MCP server itself works perfectly when called directly via JSON-RPC, but Agent Framework agents receive empty ticket data during workflow execution. This causes:
1. Misclassification (incident → request)
2. Zero confidence (0.0)
3. No KB search performed
4. Generic escalation to catch-all assignee

**Blocking Issue:** Python Agent Framework → MCP Server communication  
**Next Action:** Wait for Bishop to investigate Agent Framework MCP tool invocation layer  
**Estimated Time to Fix:** Unknown (requires Agent Framework library debugging or direct API fallback implementation)

---

**Test Cycle Status:** Round 1 of 5 completed. Waiting for team fixes before Round 2.
