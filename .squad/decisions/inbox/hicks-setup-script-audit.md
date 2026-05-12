# Setup Script Audit — MCP and Resolution Agent Fixes

**Date:** 2026-05-12  
**Author:** Hicks (Backend Dev)  
**Requested by:** Jason Farrell  
**Status:** Implemented

---

## What Was Audited

Setup script (`scripts/Setup-Solution.ps1`) and supporting docs (`DEPLOY.md`, `SETUP.md`, `azure.yaml`) reviewed against three Python/dotnet code changes made this session:

1. MCP tool refactored to per-execution factory (not singleton)
2. Decomposer agents use correct `search_kb` tool name + short queries
3. MCP server route conflict removed (`Program.cs`)

---

## What Was Correct (No Changes Needed)

| Item | Status | Notes |
|------|--------|-------|
| Python resolution image build | ✅ | `az acr build` from `src/python` with correct Dockerfile path. Picks up current source automatically. |
| MCP server image build | ✅ | `az acr build` from `src/dotnet/TicketsApi.McpServer`. Picks up route fix automatically. |
| MCP server `TICKETS_API_URL` | ✅ | Set to `$apiUrl` in both create and update paths. |
| MCP server `AZURE_CLIENT_ID` | ✅ | Set to MCP managed identity client ID. |
| Resolution `AZURE_CLIENT_ID` | ✅ | Set to resolution managed identity client ID. |
| OpenAI RBAC role assignment | ✅ | Script grants `Cognitive Services OpenAI User` to resolution identity on the OpenAI account. |

---

## What Was Missing or Wrong

### Bug 1: `MCP_SERVER_URL` had `/mcp` suffix

**Location:** Lines 680 and 695 in `Setup-Solution.ps1`

**Problem:** `MCP_SERVER_URL` was set to `$mcpUrl`, which is constructed as `"$mcpBaseUrl/mcp"`. The value passed into the resolution container included the `/mcp` path segment.

**Why it matters:** `shared/mcp_tools.py` passes `MCP_SERVER_URL` directly to `MCPStreamableHTTPTool(url=mcp_url)`. The SDK handles routing to the `/mcp` endpoint internally. Passing a URL that already includes `/mcp` would result in double-routing (`/mcp/mcp`).

**Fix:** Changed `MCP_SERVER_URL=$mcpUrl` → `MCP_SERVER_URL=$mcpBaseUrl` in both the `az containerapp update` and `az containerapp create` paths.

---

### Bug 2: Missing Azure OpenAI env vars on resolution container

**Location:** Lines 680 and 695 in `Setup-Solution.ps1`

**Problem:** Resolution container was only receiving `AZURE_CLIENT_ID` and `MCP_SERVER_URL`. The three OpenAI config env vars were absent.

`shared/client.py` reads:
- `AZURE_OPENAI_ENDPOINT` (default: `https://foundry-demo-eus2-mx01.cognitiveservices.azure.com/`)
- `AZURE_OPENAI_MODEL` (default: `gpt-4.1`)
- `AZURE_OPENAI_API_VERSION` (default: `2024-12-01-preview`)

Without explicit env vars, the container silently uses the dev defaults. This is dangerous in production — a deploy to a new environment would use a dev Foundry endpoint rather than the configured one.

**Note:** `$openAiEndpoint` was already computed earlier in the script (around line 615) for the RBAC role assignment. It was just never forwarded to the container.

**Fix:** Added all three env vars to both create and update paths:
```
AZURE_OPENAI_ENDPOINT=$openAiEndpoint
AZURE_OPENAI_MODEL=gpt-4.1
AZURE_OPENAI_API_VERSION=2024-12-01-preview
```

---

### Gap 3: DEPLOY.md missing container env var documentation

**Problem:** DEPLOY.md's Configuration section documented Web App settings only. No documentation existed for what env vars the resolution or MCP containers require.

**Fix:** Added two new subsections to DEPLOY.md:
- "Python Resolution Container Environment" — lists all 5 required env vars with descriptions
- "MCP Server Container Environment" — lists `TICKETS_API_URL` and `AZURE_CLIENT_ID`

---

## Verification

- Both `update` and `create` code paths patched (fresh deploy and re-deploy both correct)
- `$openAiEndpoint` variable is in scope at both patch sites (set ~50 lines earlier in the resolution identity block)
- Committed as `693ce7f` on `main`

---

## Impact of These Fixes

A fresh `Setup-Solution.ps1` run will now:
1. Build Python resolution image from current source (picks up factory pattern, `search_kb` fix, short-query prompts)
2. Build MCP server from current source (picks up route conflict fix)
3. Configure resolution container with correct `MCP_SERVER_URL` (base URL only)
4. Configure resolution container with all required Azure OpenAI env vars
5. Document all required env vars in DEPLOY.md for operators
