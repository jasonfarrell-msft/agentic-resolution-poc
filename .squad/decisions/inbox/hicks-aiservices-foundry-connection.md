# Decision: Migrate from standalone OpenAI to AI Services (Foundry-integrated)

**By:** Hicks (Backend Dev)  
**Date:** 2026-04-29  
**Status:** Implemented  

## What

Replaced `Microsoft.CognitiveServices/accounts` `kind: 'OpenAI'` with `kind: 'AIServices'` in `infra/modules/openai.bicep`. Added an AI Services → Foundry hub connection in `infra/modules/foundry.bicep` via `Microsoft.MachineLearningServices/workspaces/connections`. Updated `infra/resources.bicep` to pass `aiServicesId` and `aiServicesEndpoint` into the foundry module with explicit `dependsOn: [oai]`.

### File changes
- `infra/modules/openai.bicep` — `kind: 'OpenAI'` → `kind: 'AIServices'`; parameter descriptions updated.
- `infra/modules/foundry.bicep` — added `aiServicesId` + `aiServicesEndpoint` params; added `aiServicesConnection` resource (`category: 'AIServices'`, `authType: 'AAD'`, `isSharedToAll: true`).
- `infra/resources.bicep` — foundry module now receives AI Services outputs; `dependsOn: [oai]` added.

## Why

`kind: 'OpenAI'` is the deprecated standalone Azure OpenAI resource type. `kind: 'AIServices'` is the current Foundry-integrated type that Microsoft recommends for all new deployments. The AI Foundry hub must be explicitly connected to an AI Services account so that agents running in the Foundry project can resolve model deployments (gpt-4.1-mini, text-embedding-3-small). Without the connection resource, the hub has no knowledge of which AI Services account to use for inference. The `authType: 'AAD'` setting ensures all access goes through Managed Identity (no key-based auth).

## Trade-offs

- No functional change to model deployments or role assignments — both still work identically against `AIServices`.
- `authType: 'AAD'` removes any fallback to key-based auth; consistent with the project's Entra-only posture.
- `isSharedToAll: true` makes the connection available to all projects under the hub (correct for single-project topology).
