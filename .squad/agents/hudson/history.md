# Hudson — History

## Project Context (seeded)
- **Project:** agentic-resolution
- **Owner:** Jason Farrell
- **Environment:** test4 (`rg-agent-resolution-test4`, East US 2)
- **Identities to audit:**
  - Resolution container `ca-res-agent-resolution-test4` — clientId `4fb9e47e-5dec-4892-a0fd-4b61e6d726c2`, principalId `13e7bda8-9ed4-47b4-947f-4ccb53cc6f1f`
  - MCP container `ca-mcp-agent-resolution-test4` — clientId `5c20c66e-d79f-46ef-bbfb-a5bede68e0e0`
  - API container `ca-api-agent-resolution-test4` — managed identity for SQL access
- **Known existing assignments:**
  - Resolution MI has `Cognitive Services OpenAI User` on `foundry-demo-eus2-mx01` in `rg-client-sandbox-eus2-mx01`
  - All container apps have `AcrPull` on `cragentresolutiontest4`
- **Foundry resource:** `foundry-demo-eus2-mx01.cognitiveservices.azure.com` (model `gpt-4.1`)
- **Authentication preference:** ALWAYS Managed Identity over keys/secrets

## Learnings

---

## 2026-05-12: Managed Identity Security Audit (Complete)

**Requested by:** Jason Farrell  
**Status:** ✅ COMPLETE

### Scope
Audited all service-to-service authentication in agentic-resolution-test4 to confirm Managed Identity usage and identify any secret/key dependencies.

### Audit Results

#### ✅ SECURE: Azure Service Authentication
1. **Resolution → Azure OpenAI (foundry-demo-eus2-mx01)**
   - ✅ Uses `DefaultAzureCredential()` in `src/python/shared/client.py:24`
   - ✅ MI role granted: `Cognitive Services OpenAI User`
   - ✅ No API keys in code or configuration
   - **Verification:** `az role assignment list --assignee 13e7bda8-9ed4-47b4-947f-4ccb53cc6f1f`

2. **API → Azure SQL (sql-agent-resolution-test4)**
   - ✅ Connection string uses `Authentication=Active Directory Default` (infra/resources.bicep:64)
   - ✅ SQL Server configured for Entra-only authentication (`azureADOnlyAuthentication: true`)
   - ⚠️ **Action Required:** Run `scripts/Configure-DatabaseUsers.ps1` to create database user `id-api-agent-resolution-test4` with `db_owner` role
   - **Rationale:** Database-level user creation requires manual step (not automatable via Bicep)

3. **All Containers → ACR (cragentresolutiontest4)**
   - ✅ Resolution MI: `AcrPull` role assigned
   - ✅ MCP MI: `AcrPull` role assigned
   - ✅ API MI: `AcrPull` role assigned

4. **API → Key Vault (kv-agentresolutiontest4)**
   - ✅ API MI: `Key Vault Secrets User` role assigned
   - ✅ Connection string stored as Key Vault secret, retrieved via MI

#### ⚠️ ACCEPTABLE: Container-to-Container (Internal Network)
1. **Resolution → MCP Container**
   - Method: HTTP, no auth (anonymous)
   - Code: `src/python/shared/mcp_tools.py:13-19`
   - Justification: Internal Container Apps network isolation
   - Recommendation: Acceptable for test4; consider MI-based auth for production

2. **MCP → API Container**
   - Method: HTTP, no auth (anonymous)
   - Code: `src/dotnet/TicketsApi.McpServer/Program.cs:9-13`
   - Justification: Internal Container Apps network isolation
   - Recommendation: Acceptable for test4; consider API key or MI-based auth for production

### Managed Identity Inventory
| Container | Identity Name | Client ID | Principal ID | Roles |
|-----------|---------------|-----------|--------------|-------|
| ca-res-agent-resolution-test4 | id-res-agent-resolution-test4 | `4fb9e47e-5dec-4892-a0fd-4b61e6d726c2` | `13e7bda8-9ed4-47b4-947f-4ccb53cc6f1f` | AcrPull, Cognitive Services OpenAI User |
| ca-mcp-agent-resolution-test4 | id-mcp-agent-resolution-test4 | `5c20c66e-d79f-46ef-bbfb-a5bede68e0e0` | `1fc29df9-0fd9-4d02-a1f0-226bcb4a22b8` | AcrPull |
| ca-api-agent-resolution-test4 | id-api-agent-resolution-test4 | `adeb74bb-7645-4e8f-a9c9-d738d1006ddb` | `54406d22-0b27-4230-a000-2f837434494c` | AcrPull, Key Vault Secrets User, SQL User (pending) |

### Key Findings
1. **Zero API keys or passwords** in code for Azure OpenAI, ACR, or Key Vault
2. **SQL Server enforces Entra-only authentication** (no SQL authentication enabled)
3. **All Azure role assignments verified** using `az role assignment list`
4. **Infrastructure-as-code defines MI roles** in Bicep (infra/resources.bicep)
5. **Container-to-container traffic** relies on network isolation (no authentication layer)

### Action Items
1. **[HIGH]** Run `scripts/Configure-DatabaseUsers.ps1` to create SQL database user for API MI
   - Owner: DevOps
   - Priority: Required before API container restart
   - Command:
     ```powershell
     .\scripts\Configure-DatabaseUsers.ps1 `
         -ServerFqdn "sql-agent-resolution-test4.database.windows.net" `
         -DatabaseName "agenticresolution" `
         -ApiIdentityName "id-api-agent-resolution-test4" `
         -WebAppIdentityName "app-agentic-resolution-web"
     ```

2. **[MEDIUM]** Document container-to-container security model in DEPLOY.md
   - Clarify: Network isolation acceptable for test4, auth required for production
   - Owner: Hudson / Bob (Technical Writer)

3. **[LOW]** Research MI-based container auth for production
   - Explore: Azure Container Apps managed identity authentication patterns
   - Decision point: Before production deployment

### Deliverables
- ✅ Full audit report: `.squad/decisions/inbox/hudson-mi-audit-results.md`
- ✅ Updated history: `.squad/agents/hudson/history.md`
- ✅ MI inventory table with all principal IDs and role assignments
- ✅ Code verification for all authentication paths

### Compliance Status
- ✅ **PASS:** No secrets in code
- ✅ **PASS:** Managed Identity for all Azure services
- ⚠️ **PARTIAL:** Internal service authentication (network-isolated, acceptable for test4)

**Overall Security Grade:** ✅ **A-** (Strong MI implementation; pending SQL user verification)

**Lessons Learned:**
1. Azure SQL Entra authentication requires two steps: server-level Entra admin (Bicep) + database-level user creation (manual)
2. Container Apps internal network isolation is acceptable for test environments but should be reconsidered for production
3. `DefaultAzureCredential()` correctly uses MI when running in Azure Container Apps (no additional configuration needed)
4. Role assignments are immutable and auditable via `az role assignment list --assignee <principalId>`

---

## 2026-05-12: SecurityControl Tag Audit (Complete)

**Requested by:** Jason Farrell  
**Status:** ⚠️ TAG MISSING — All resources lack `SecurityControl: Ignore`

### Scope
Audited all resources in `rg-agent-resolution-test4` for the presence of tag `SecurityControl: Ignore`.

### Result
**0 of 16 objects (15 resources + 1 resource group) have the required tag.**

The resource group itself only carries `azd-env-name: agent-resolution-test4`.

### Missing Tag — Full List

| Resource Name | Type | Current Tags |
|---|---|---|
| rg-agent-resolution-test4 *(RG)* | Resource Group | azd-env-name |
| plan-agent-resolution-test4 | Microsoft.Web/serverFarms | azd-env-name |
| kv-agentresolutiontest4 | Microsoft.KeyVault/vaults | azd-env-name |
| sql-agent-resolution-test4 | Microsoft.Sql/servers | azd-env-name |
| app-agent-resolution-test4-web | Microsoft.Web/sites | azd-env-name, azd-service-name |
| sql-agent-resolution-test4/master | Microsoft.Sql/servers/databases | *(none)* |
| sql-agent-resolution-test4/agenticresolution | Microsoft.Sql/servers/databases | azd-env-name |
| workspace-rgagentresolutiontest4ihy7 | Microsoft.OperationalInsights/workspaces | *(none)* |
| cae-agent-resolution-test4 | Microsoft.App/managedEnvironments | *(none)* |
| cragentresolutiontest4 | Microsoft.ContainerRegistry/registries | *(none)* |
| id-api-agent-resolution-test4 | Microsoft.ManagedIdentity/userAssignedIdentities | *(none)* |
| ca-api-agent-resolution-test4 | Microsoft.App/containerApps | *(none)* |
| id-resolution-agent-resolution-test4 | Microsoft.ManagedIdentity/userAssignedIdentities | *(none)* |
| ca-res-agent-resolution-test4 | Microsoft.App/containerApps | *(none)* |
| id-mcp-agent-resolution-test4 | Microsoft.ManagedIdentity/userAssignedIdentities | *(none)* |
| ca-mcp-agent-resolution-test4 | Microsoft.App/containerApps | *(none)* |

### Action Required
Tags were **not modified** (audit only). If `SecurityControl: Ignore` is the required compliance marker, it must be applied to all 15 resources and the resource group.

**Deliverables:**
- ✅ Full report: `.squad/decisions/inbox/hudson-tag-audit.md`
- ✅ Updated history: `.squad/agents/hudson/history.md`

---

## 2026-05-12: SecurityControl Tag Application (Complete)

**Requested by:** Jason Farrell  
**Status:** ✅ COMPLETE

### Scope
Apply `SecurityControl=Ignore` tag to resource group `rg-agent-resolution-test4` and all 15 resources. Update `scripts/Setup-Solution.ps1` so future deployments apply the tag automatically.

### Actions Taken

**Part 1 — Existing resources tagged:**
- Resource group `rg-agent-resolution-test4`: tagged via `az tag update --operation Merge`
- All 15 resources: tagged via `az tag update --operation Merge` (used merge to preserve existing tags)
- Note: `az resource tag --is-incremental` failed for 3 resource types (SQL master DB, Container Apps Environment, Container App); `az tag update --operation Merge` succeeded for all

**Part 2 — Setup script updated:**
- Added consolidated tagging block to `scripts/Setup-Solution.ps1` before the "Setup Complete!" banner
- Uses `az tag update --operation Merge` for both the RG and all resources
- Future deployments will automatically apply `SecurityControl=Ignore`

**Part 3 — Committed:**
- `git commit`: `feat: apply SecurityControl=Ignore tag to all resources and setup script`

### Verification

All 16 objects (1 RG + 15 resources) confirmed with `SecurityControl=Ignore`:

| Resource | Tag Applied |
|---|---|
| rg-agent-resolution-test4 (RG) | ✅ |
| plan-agent-resolution-test4 | ✅ |
| kv-agentresolutiontest4 | ✅ |
| sql-agent-resolution-test4 | ✅ |
| app-agent-resolution-test4-web | ✅ |
| sql-agent-resolution-test4/master | ✅ |
| sql-agent-resolution-test4/agenticresolution | ✅ |
| workspace-rgagentresolutiontest4ihy7 | ✅ |
| cae-agent-resolution-test4 | ✅ |
| cragentresolutiontest4 | ✅ |
| id-api-agent-resolution-test4 | ✅ |
| ca-api-agent-resolution-test4 | ✅ |
| id-resolution-agent-resolution-test4 | ✅ |
| ca-res-agent-resolution-test4 | ✅ |
| id-mcp-agent-resolution-test4 | ✅ |
| ca-mcp-agent-resolution-test4 | ✅ |

### Lessons Learned
- `az resource tag --is-incremental` fails for certain resource types (SQL system DBs, Container Apps, Container Apps Environments)
- `az tag update --resource-id <id> --operation Merge --tags "Key=Value"` is the universal approach that works for all resource types
- Setup script should always use `az tag update --operation Merge` rather than `az resource tag` for the tagging loop

---
