# Decision: Modern Azure AI Foundry Resource Type

**By:** Hicks (Backend Dev)  
**Date:** 2026-04-29  
**Status:** Accepted  
**Scope:** `infra/modules/foundry.bicep`, `infra/modules/openai.bicep`, `infra/resources.bicep`

---

## Context

`infra/modules/foundry.bicep` was using the deprecated ML Workspaces approach:
- `Microsoft.MachineLearningServices/workspaces` with `kind: 'Hub'` — deprecated "Foundry classic" hub
- `Microsoft.MachineLearningServices/workspaces` with `kind: 'Project'` — deprecated Foundry project
- `Microsoft.MachineLearningServices/workspaces/connections` to link the AIServices account

Jason directed a rewrite to the modern 2025+ resource types.

## Investigation

Checked what is actually deployed (`az cognitiveservices account list-kinds`, `az provider show`):

- `kind: 'AIFoundry'` **does not exist** as a CognitiveServices account kind. The CLI list-kinds output confirms it. There is no separate "AIFoundry" resource type.
- `Microsoft.CognitiveServices/accounts/projects` **does exist** (confirmed via `az provider show`), with API versions from `2025-04-01-preview` through `2026-03-01` and GA versions at `2025-06-01`.
- The Microsoft Bicep documentation example for `accounts/projects` uses the **parent account with `kind: 'AIServices'` and `allowProjectManagement: true`** — not a separate Foundry account kind.

## Decision

**The `AIServices` account IS the Foundry hub.** No separate resource type needed.

Modern pattern:
1. `Microsoft.CognitiveServices/accounts@2025-06-01` with `kind: 'AIServices'` and `allowProjectManagement: true` → this account is the Foundry hub
2. `Microsoft.CognitiveServices/accounts/projects@2025-06-01` as child resources → Foundry projects

Since the project already deploys an `AIServices` account in `openai.bicep`, the foundry project is simply a child of that account. No separate hub resource, no ML workspace connections.

## Changes Made

- **`infra/modules/foundry.bicep`** — Complete rewrite:
  - Removed all `Microsoft.MachineLearningServices` resources (hub, project, connection)
  - References the existing AIServices account via `existing` keyword
  - Creates `Microsoft.CognitiveServices/accounts/projects@2025-06-01` as child
  - Grants Azure AI Developer role on the project to function app MI
  - Removed params: `hubName`, `storageAccountId`, `keyVaultId`, `appInsightsId`, `aiServicesId`, `aiServicesEndpoint`
  - New param: `aiServicesAccountName` (name of the AIServices account)
  - `projectEndpoint` output now uses the real AIServices `properties.endpoint`

- **`infra/modules/openai.bicep`** — Updated API version to `2025-06-01` (all resources) and added `allowProjectManagement: true` to the AIServices account properties.

- **`infra/resources.bicep`** — Updated foundry module call to pass `aiServicesAccountName: oai.outputs.name`; removed the now-unnecessary storage/KV/AppInsights params. `foundryHubName` output now references `oai.outputs.name` directly (the AIServices account IS the hub).

## Why ML Workspaces Hub/Project Is Deprecated

Azure AI Foundry originally ran on top of Azure Machine Learning (ML Workspaces with Hub/Project kinds). As of 2025, Microsoft rebuilt Foundry as a native CognitiveServices capability. The old ML Workspace approach ("Foundry classic") will eventually be retired. The new `accounts/projects` model is simpler: no separate hub resource, no workspace connections, no ML-specific SKU/tier requirements. One AIServices account can host multiple projects natively.

## Role Assignment Note

Azure AI Developer (`64702f94-c441-49e6-a78b-ef80e0188fee`) is still the correct role for the function app MI on the Foundry project. This role allows inference calls and project operations via the Agents SDK.

## Project Endpoint

The Agents SDK (Azure AI Projects client) connects to: `{aiServicesEndpoint}/projects/{projectName}`. The `projectEndpoint` output now correctly reflects the real AIServices endpoint rather than the old `https://{location}.api.azureml.ms` constructed string.
