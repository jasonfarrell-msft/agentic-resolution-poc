# Decision: Replace Function App with Container App for Webhook Receiver

**Date:** 2026-04-29  
**Author:** Hicks  
**Direction from:** Jason Farrell

## Change Summary

Replaced the Azure Consumption Function App (`infra/modules/functionapp.bicep`) with an Azure Container App (`infra/modules/containerapp.bicep`) backed by a new Container Apps Environment (`infra/modules/containerappenvironment.bicep`). The Consumption Function App plan and all associated App Service resources have been removed.

## Why

Jason's directive. The Container App model is a better fit for a standalone webhook receiver service — it's container-native, supports any runtime, and is simpler to wire into a future CI/CD pipeline where the image is built and pushed on commit. The Consumption Function App required an Azure Storage dependency and a specific .NET Functions host, which adds complexity for what is ultimately a simple HTTP receiver.

## Key Differences from Function App

| Concern | Function App (removed) | Container App (added) |
|---|---|---|
| Key Vault secrets | App Service KV reference syntax: `@Microsoft.KeyVault(VaultName=...;SecretName=...)` — resolved by the App Service platform inline in app settings | CA `secrets` array at container app level with `keyVaultUrl` + `identity` (MI resource ID); referenced in env vars via `secretRef: '<secret-name>'` |
| Storage dependency | Required `AzureWebJobsStorage` (Azure Storage connection string via KV ref) | No storage dependency |
| Host | .NET 8 isolated worker Functions host | Placeholder `mcr.microsoft.com/dotnet/samples:aspnetapp` — replaced when code is built |
| Scale-to-zero | Yes (Consumption Y1 plan) | Yes (`minReplicas: 0`) |
| Max scale | Controlled by Functions runtime | `maxReplicas: 3` |
| Ingress | Functions default host (HTTPS) | External ingress, port 8080, HTTPS |
| Managed identity | User-assigned (`id-func-{namePrefix}`) | Same user-assigned identity reused |

## What Still Needs to Happen When Code Is Built

1. **Build the webhook receiver application** — ASP.NET Core minimal API (or similar) listening on port 8080.
2. **Push image to a container registry** — either Azure Container Registry (preferred, can be added as an infra module) or GitHub Container Registry.
3. **Update the container image reference** — replace `mcr.microsoft.com/dotnet/samples:aspnetapp` with the actual image. Options:
   - Update `containerapp.bicep` and redeploy via `azd up`
   - Use `az containerapp update --name <name> --resource-group <rg> --image <image>` for a targeted update without full Bicep redeploy
4. **Wire `Foundry__Endpoint`** — Bishop populates after the Foundry project is confirmed live (same as before).
5. **Populate `webhook-hmac-secret` in Key Vault** — operator action; the CA will surface the secret to `Webhook__HmacSecret` env var once the KV secret exists.

## Modules Added

- `infra/modules/containerappenvironment.bicep` — CAE backed by existing Log Analytics workspace
- `infra/modules/containerapp.bicep` — Container App, external ingress port 8080, min 0/max 3 replicas, 0.25 CPU / 0.5Gi

## Modules Removed

- `infra/modules/functionapp.bicep`

## azd Env Var Changes

| Before | After |
|---|---|
| `FUNCTION_APP_NAME` | `CONTAINER_APP_NAME` |
| `FUNCTION_APP_HOSTNAME` | `CONTAINER_APP_HOSTNAME` |
