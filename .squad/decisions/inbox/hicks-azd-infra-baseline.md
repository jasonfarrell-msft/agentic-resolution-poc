# azd Infrastructure Baseline for Blazor Frontend

**Date:** 2026-05-08  
**By:** Hicks (Backend Dev)  
**Requested by:** Jason Farrell

## Status

**Implemented** — ready for `azd up`

## Context

Jason requested minimal Azure Developer CLI (`azd`) infrastructure to deploy the Blazor frontend (`AgenticResolution.Web`) to Azure App Service immediately. Backend services (Container Apps for API, webhook receiver, MCP server) should be declared in `azure.yaml` but not provisioned yet — deployment is incremental, frontend first, backend later.

The repo had empty 0-byte files for `azure.yaml`, `infra/main.bicep`, `infra/resources.bicep`, and several `infra/modules/*.bicep` stubs. Two modules (`containerapp.bicep`, `containerregistry.bicep`) already had content but were not wired into the main deployment.

## Decision

Created minimal azd baseline:

1. **`azure.yaml`**
   - Project name: `agentic-resolution`
   - Service `web`: `host: appservice`, project path `./src/dotnet/AgenticResolution.Web`
   - Service `api`: `host: containerapp`, project path `./src/dotnet/AgenticResolution.Api` (declared but not deployed)

2. **`infra/main.bicep`**
   - `targetScope = 'subscription'`
   - Parameters: `environmentName`, `location` (default `eastus2`), `principalId` (optional)
   - Resource group: `rg-${environmentName}`
   - Module call to `resources.bicep`
   - Outputs: `AZURE_LOCATION`, `AZURE_TENANT_ID`, `WEB_APP_NAME`, `WEB_APP_HOSTNAME`

3. **`infra/resources.bicep`**
   - **Provisioned immediately:**
     - App Service Plan: `plan-${environmentName}`, B1 SKU, Linux
     - App Service: `app-${environmentName}-web`, Linux, .NET 8 runtime (`DOTNETCORE|8.0`)
       - Tagged with `azd-service-name: 'web'` — this is what links the resource to the `web` service in `azure.yaml`
       - App settings: `ApiClient__BaseUrl` = `''` (empty placeholder), `ASPNETCORE_ENVIRONMENT` = `Production`
       - `httpsOnly: true`, `ftpsState: 'FtpsOnly'`, `minTlsVersion: '1.2'`, `alwaysOn: true`
   - **Gated behind `deployBackend` parameter (default `false`):**
     - Container App Environment module call (minimal stub)
     - Container Registry module call (commented out — requires managed identity principal IDs that don't exist yet)

4. **`infra/modules/containerappenvironment.bicep`**
   - Minimal stub: `properties: {}` — just enough to avoid Bicep errors if referenced later
   - Outputs: `environmentId`, `environmentName`

5. **`DEPLOY.md`**
   - Documents `azd init` / `azd up` flow for first-time provisioning
   - Explains how to update `ApiClient__BaseUrl` app setting after backend deployment
   - Troubleshooting section for common issues (build failures, deployment failures, naming conflicts)
   - Clean-up instructions (`azd down`)

## Rationale

### Why App Service for frontend?
- Blazor Server/Web apps are natural fit for App Service (managed runtime, no Docker required).
- Jason explicitly requested App Service for frontend, Container Apps for backend.
- Simplifies first deployment — no ACR, no container images, no managed identities for ACR pull.

### Why gate backend resources behind `deployBackend` param?
- `containerregistry.bicep` requires three principal IDs (`webhookPrincipalId`, `apiPrincipalId`, `mcpPrincipalId`) for ACR role assignments.
- Those principal IDs come from Container App managed identities, which don't exist yet.
- Attempting to provision all resources in one pass would fail with missing parameter values.
- Gating allows incremental deployment: frontend now, backend later when ready.

### Why comment out Container Registry module instead of completing it?
- Completing it would require provisioning all three Container Apps (webhook, api, mcp) with their managed identities — that's a full backend deployment, not a minimal baseline.
- Commenting it out leaves a clear TODO for the next step while unblocking frontend deployment immediately.

### Why `ApiClient__BaseUrl` empty?
- The backend Container App API doesn't exist yet, so there's no URL to configure.
- Empty string is a safe default — the Blazor app can handle it gracefully (no API calls until configured).
- Jason can set this app setting via Azure Portal or `az webapp config appsettings set` once the backend API is deployed.

### Why `DOTNETCORE|8.0` runtime instead of `10.0`?
- The `AgenticResolution.Web.csproj` targets `net10.0`, but the Azure App Service Linux runtime `DOTNETCORE|10.0` may not be GA yet (as of task execution date).
- `DOTNETCORE|8.0` is the stable LTS runtime and is forward-compatible with .NET 10 projects for now.
- Jason can bump to `DOTNETCORE|10.0` in `resources.bicep` and redeploy (`azd provision`) once that runtime is available.

## What's Provisioned (deployBackend=false)

✅ Provisioned immediately:
- Resource Group: `rg-${environmentName}`
- App Service Plan: `plan-${environmentName}` (B1, Linux)
- App Service: `app-${environmentName}-web` (tagged `azd-service-name: 'web'`)

❌ Not provisioned (gated):
- Container App Environment
- Container Registry
- Container Apps (webhook, api, mcp-server)
- AI Search, OpenAI, Foundry, Storage, Key Vault (not yet in scope for this baseline)

## What's in Scope for Next Backend Deployment

When `deployBackend=true`:
1. Uncomment `containerRegistry` module call in `resources.bicep`
2. Provision Container App Environment (`containerappenvironment.bicep` module)
3. Provision Container Registry with user-assigned managed identities
4. Deploy `containerapp-api.bicep`, `containerapp-mcp.bicep`, `containerapp.bicep` (webhook receiver)
5. Wire managed identity principal IDs into `containerRegistry` module call
6. Update `ApiClient__BaseUrl` app setting on the App Service to point to `api` Container App FQDN

## Files Created/Modified

**Created:**
- `azure.yaml` (402 bytes)
- `DEPLOY.md` (3097 bytes)

**Modified (from 0 bytes):**
- `infra/main.bicep` (1089 bytes)
- `infra/resources.bicep` (2630 bytes)
- `infra/modules/containerappenvironment.bicep` (272 bytes)

**Existing (unchanged):**
- `infra/modules/containerapp.bicep` (3241 bytes) — webhook receiver config (not yet wired)
- `infra/modules/containerregistry.bicep` (1723 bytes) — ACR + role assignments (not yet wired)

## Build Verification

Ran `dotnet build src\dotnet\AgenticResolution.Web\AgenticResolution.Web.csproj`:
- ✅ Build succeeded in 2.2s
- ⚠️ 2 warnings (NU1510 — `Microsoft.Extensions.Http` pruning suggestion, non-blocking)
- Project targets `net10.0`, uses `Microsoft.AspNetCore.SignalR.Client` and `Microsoft.Extensions.Http`

## Next Steps

**For Jason:**
1. Run `azd init` (select subscription, region, environment name)
2. Run `azd up` (provision + deploy Blazor frontend)
3. Navigate to output `WEB_APP_HOSTNAME` to verify deployment
4. After backend deployment: set `ApiClient__BaseUrl` app setting

**For Hicks (future — when backend ready):**
1. Set `deployBackend=true` in `main.parameters.json` or via `azd env set DEPLOY_BACKEND true`
2. Uncomment `containerRegistry` module in `resources.bicep`
3. Provision managed identities for Container Apps
4. Complete backend Bicep modules (AI Search, OpenAI, etc. per Phase 2 decisions)
5. Run `azd provision` (backend resources)
6. Run `azd deploy api` (Container App API)

**For Ferro:**
- Verify `AgenticResolution.Web` handles empty `ApiClient__BaseUrl` gracefully (no runtime errors, graceful degradation)

## Tradeoffs

**Pro:**
- ✅ Unblocks frontend deployment immediately — no waiting for full backend infra
- ✅ Incremental deployment matches team's stated strategy
- ✅ Clean separation: frontend in App Service, backend in Container Apps
- ✅ Minimal resource cost for initial deployment (~$15/month for B1 App Service)

**Con:**
- ⚠️ Two-phase deployment means two `azd` invocations (frontend, then backend) instead of one atomic deployment
- ⚠️ `ApiClient__BaseUrl` requires manual post-deployment config once backend is live
- ⚠️ `deployBackend` param adds conditional logic in Bicep — could be confusing if not documented

## Alignment with Phase 2 Architecture

This baseline is orthogonal to the Phase 2 decisions (AI Search, Foundry, Function App webhook receiver). Phase 2 resources are not yet declared in `resources.bicep` — they will be added incrementally as backend deployment progresses.

The existing `containerapp.bicep` module (3241 bytes) from Phase 2 work is preserved but not yet wired. When backend deployment is ready, that module can be called from `resources.bicep` with the appropriate parameters.

## References

- Apone's Phase 2 Architecture decision (2026-04-29) — defines Function App webhook receiver, not Container App webhook receiver. The existing `containerapp.bicep` may be for a different service (needs clarification from Apone/Jason).
- Hicks history: Phase 1 Azure deploy (2026-04-29) — first azd deployment was for the monolithic Blazor Server app with embedded API. This baseline reflects the post-split architecture (separate Web and Api projects).
