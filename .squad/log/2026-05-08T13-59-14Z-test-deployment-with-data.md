# Session Log: Test Deployment with Data

**Date:** 2026-05-08T13:59:14Z  
**Requested by:** Jason Farrell  
**Request:** Rerun deployment process, deploy test version with seeded data

## Deployment Summary

✅ **Test environment `agent-resolution-test` deployed successfully**

### Infrastructure
- **Region:** eastus2
- **Resource group:** `rg-agent-resolution-test`
- **Status:** Healthy

### Services Deployed
- **Web App:** `app-agent-resolution-test-web` (HTTP 200)
- **Tickets API:** `ca-api-agent-resolution-test` (healthy, connected)
- **Resolution API:** `ca-res-agent-resolution-test` (healthy)

### Data
- **Sample tickets seeded:** 15
- **State:** New

### Known Issue Documented
- SQL Server public access enabled (workaround for lack of private endpoints)
- Marked for production: Add VNet + private endpoint

## Validation Gates
- ✅ Web endpoint responds
- ✅ API health endpoint responds
- ✅ Database connected and seeded
- ✅ 15 tickets returned on API call

## Next Steps
- Working tree retains uncommitted product changes from reseed fix/docs/tests
- No product files committed per instruction
