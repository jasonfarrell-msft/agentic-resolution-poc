# Decision: Phase 2 Bicep IaC — Hicks

**By:** Hicks (Backend Dev)  
**Date:** 2026-04-29  
**Status:** Proposed — pending `azd up` validation against existing RG

---

## What was added

Four new Bicep modules and updates to the existing orchestration files:

| File | New/Updated | Summary |
|---|---|---|
| `infra/modules/storage.bicep` | New | Standard_LRS StorageV2, no public blob, conn string → KV secret |
| `infra/modules/openai.bicep` | New | Azure OpenAI S0; text-embedding-3-small + gpt-4o-mini deployments; RBAC for app service + func MI |
| `infra/modules/functionapp.bicep` | New | Consumption Y1, .NET 8 isolated; user-assigned MI; KV refs for storage + HMAC secret |
| `infra/modules/foundry.bicep` | New | AI Foundry Hub + Project (Standard); Azure AI Developer role for func MI |
| `infra/modules/appinsights.bicep` | Updated | Added `resourceId` output (non-breaking) for Foundry hub association |
| `infra/resources.bicep` | Updated | Wires all new modules; adds `var storageAccountName`; nine new outputs |
| `infra/main.bicep` | Updated | Eight new azd outputs for Phase 2 resources |

---

## Naming conventions used

All names follow the existing `namePrefix = 'agentic-res-${environmentName}'` convention:

- **Storage account:** `saagres{env}{uniqueString}` truncated to 24 chars (matches KV pattern `kvagres...`)
- **OpenAI:** `oai-{namePrefix}` — custom subdomain makes it globally unique without uniqueString suffix
- **Function consumption plan:** `plan-func-{namePrefix}` — distinct from app service plan `plan-{namePrefix}`
- **Function app:** `func-{namePrefix}-{uniqueString}` — matches `app-{namePrefix}-{uniqueString}` for web app
- **Function app MI:** `id-func-{namePrefix}` — mirrors `id-{namePrefix}` for web app MI
- **Foundry Hub:** `hub-{namePrefix}`
- **Foundry Project:** `proj-{namePrefix}`

Role assignment GUIDs all use `guid(scopeId, principalId, 'friendly-name')` — consistent with keyvault.bicep pattern.

---

## Architecture decisions and rationale

### Storage account shared between function app and Foundry hub

The Foundry Hub resource requires a backing storage account. Rather than provision two storage accounts, the single `storage` module output is passed to both `functionapp` and `foundry` modules. Saves ~$1–2/mo and halves the storage surface area to manage.

### Function app KV reference for AzureWebJobsStorage

The task required the storage connection string to be in Key Vault (not inline). The function app's `AzureWebJobsStorage` setting uses `@Microsoft.KeyVault(SecretUri=...)` referencing the secret URI. The function app MI has `Key Vault Secrets User` role (same role as the web app MI). The `dependsOn: [kvRole]` on the `func` resource ensures the role assignment is in place before the app resource is created. **Note:** KV references for `AzureWebJobsStorage` on Consumption plan functions can have cold-start latency; if this becomes an issue, consider `AzureWebJobsStorage__accountName` (MI-based, no connection string).

### webhook-hmac-secret not auto-populated

The `Webhook__HmacSecret` app setting references a KV secret named `webhook-hmac-secret` that is NOT created by Bicep. The function app will start but fail to read the secret until the operator populates it. This is intentional — the HMAC secret should be operator-set, not auto-generated at deploy time. The empty-secret state mirrors the Phase 1 behaviour where `Webhook__Secret` was an empty string.

### KV role for function app in functionapp.bicep (not keyvault.bicep)

The existing `keyvault.bicep` accepts a single `accessPrincipalId`. Rather than change the existing module (which would re-deploy the already-live KV resource and risk drift), the function app's KV role assignment is scoped to the existing KV resource inside `functionapp.bicep` using an `existing` resource reference. The `dependsOn: [kvRole]` ensures the role exists before the function app is created.

### .NET 8 (not .NET 10) for function app

Apone's Phase 2 decision specified .NET 10 for the function app. Jason's task explicitly requested .NET 8 isolated worker. Jason's directive takes precedence. `FUNCTIONS_WORKER_RUNTIME = dotnet-isolated`, `netFrameworkVersion = v8.0`.

### AI Search excluded

Explicitly deferred by Jason. No `search.bicep` module was created. The `text-embedding-3-small` deployment is still provisioned now so it's ready when AI Search is connected later (G2 gate criterion).

### Foundry project endpoint output

Azure AI Foundry project endpoints take the form `https://{location}.api.azureml.ms`. The `foundry.bicep` module outputs a constructed URL using the location parameter. The full SDK connection string (subscription/RG/hub/project) requires runtime values and is not output from Bicep — callers derive it from the `foundryHubName` and `foundryProjectName` azd outputs.

---

## Files touched

- `infra/modules/storage.bicep` — **created**
- `infra/modules/openai.bicep` — **created**
- `infra/modules/functionapp.bicep` — **created**
- `infra/modules/foundry.bicep` — **created**
- `infra/modules/appinsights.bicep` — `resourceId` output added
- `infra/resources.bicep` — new module calls + outputs
- `infra/main.bicep` — new azd output declarations
