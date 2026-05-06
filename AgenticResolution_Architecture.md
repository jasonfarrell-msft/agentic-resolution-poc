# Agentic Resolution - Azure Deployment Architecture Plan

## Executive Summary

This document defines the Azure deployment topology for the Agentic Resolution PoC — a system that uses AI agents to autonomously resolve IT support tickets. The architecture prioritizes simplicity (PoC-appropriate), clear separation of concerns, and a production-grade core (Python Resolution API) while keeping demo components lightweight.

---

## Deployment Topology (ASCII Diagram)

```
┌─────────────────────────────────────────────────────────────────────────┐
│  Resource Group: rg-agentic-resolution-{env}                            │
│                                                                         │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │  Azure Container Apps Environment (cae-{env})                   │    │
│  │  ┌───────────────────┐  ┌───────────────────┐                  │    │
│  │  │  Python Resolution│  │    MCP Server     │                  │    │
│  │  │  API (Container   │──│  (Container App)  │                  │    │
│  │  │  App)             │  │                   │                  │    │
│  │  └─────────┬─────────┘  └────────┬──────────┘                  │    │
│  │            │                      │                             │    │
│  │            │  internal ingress     │  internal ingress           │    │
│  └────────────┼──────────────────────┼─────────────────────────────┘    │
│               │                      │                                  │
│               │                      ▼                                  │
│  ┌────────────┼──────────────────────────────────────────────┐          │
│  │            │         App Service Plan (B1 Linux)           │          │
│  │            │  ┌───────────────────┐  ┌─────────────────┐  │          │
│  │            │  │   TicketsNow API  │  │   Blazor UI     │  │          │
│  │            │  │   (.NET 8)        │  │   (Demo only)   │  │          │
│  │            └─▶│   App Service     │  │   App Service   │  │          │
│  │               └───────────────────┘  └────────┬────────┘  │          │
│  └───────────────────────────────────────────────┼────────────┘          │
│                                                  │                      │
│  ┌───────────────────────────────────────────────┼──────────────────┐   │
│  │  Shared Services                              │                  │   │
│  │  ┌──────────────┐  ┌──────────────┐  ┌───────┴──────────┐       │   │
│  │  │ Azure AI     │  │  Key Vault   │  │ Container        │       │   │
│  │  │ Foundry      │  │  (secrets)   │  │ Registry (ACR)   │       │   │
│  │  │ (GPT-4o)     │  │              │  │                  │       │   │
│  │  └──────────────┘  └──────────────┘  └──────────────────┘       │   │
│  └──────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────┘

  External User
       │
       ▼
  Blazor UI ──────► Python Resolution API (start resolution)
       │
       └──────────► TicketsNow API (display ticket data)
```

---

## Component → Azure Service Mapping

| Component | Azure Service | SKU / Tier | Justification |
|-----------|--------------|------------|---------------|
| **TicketsNow API** (.NET 8) | App Service | B1 Linux | Simple REST API, no containers needed. Shares plan with Blazor UI to reduce cost. Easily swappable for real ServiceNow later. |
| **MCP Server** | Container App | Consumption (0.25 vCPU / 0.5 GB) | Separate process, needs internal-only networking. Container Apps gives free internal DNS + scaling to zero. |
| **Python Resolution API** | Container App | Consumption (0.5 vCPU / 1 GB) | Production-grade component. Container Apps provides auto-scale, revision management, and health probes. External ingress for Blazor to call. |
| **Blazor UI** | App Service | B1 Linux (shared plan) | Demo-only, minimal cost. Shares the same App Service Plan as TicketsNow. |
| **AI Models** | Azure AI Foundry | GPT-4o (Standard) | Per project standards. Used by Python Resolution API via Azure AI Agent Service. |
| **Secrets** | Key Vault | Standard | Stores API keys, connection strings, AI Foundry keys. All services reference via managed identity. |
| **Container Images** | Azure Container Registry | Basic | Hosts images for Python Resolution API and MCP Server. |

---

## Networking & Communication

| From | To | Protocol | Access Level | Notes |
|------|----|----------|--------------|-------|
| Blazor UI | Python Resolution API | HTTPS | External ingress on Container App | Direct call to start resolution workflow |
| Blazor UI | TicketsNow API | HTTPS | Public App Service URL | Read ticket data for display |
| Python Resolution API | MCP Server | HTTPS | **Internal ingress** (Container Apps internal DNS) | Agents call MCP tools; not exposed externally |
| MCP Server | TicketsNow API | HTTPS | App Service URL (internal VNet or public) | Translates tool calls → REST calls |
| Python Resolution API | Azure AI Foundry | HTTPS | Azure backbone | Agent Service SDK calls |
| All services | Key Vault | HTTPS | Managed Identity | Secret retrieval at startup |

### Key Networking Decisions

1. **MCP Server is internal-only** — Container Apps `internal` ingress means it gets a `*.internal.<env>.azurecontainerapps.io` FQDN accessible only within the Container Apps Environment. No public exposure.

2. **Python Resolution API is external** — Needs to be callable from the Blazor UI (which runs on a separate App Service). Uses `external` ingress with HTTPS.

3. **No VNet required for PoC** — All communication happens over HTTPS with service-level authentication. A VNet integration can be added later for production hardening.

---

## Resource Group Structure

```
rg-agentic-resolution-{env}
├── plan-{env}                          (App Service Plan - B1 Linux)
│   ├── app-{env}-web                   (Blazor UI)
│   └── app-{env}-ticketsnow            (TicketsNow API)
├── cae-{env}                           (Container Apps Environment)
│   ├── ca-{env}-resolution-api         (Python Resolution API)
│   └── ca-{env}-mcp-server             (MCP Server)
├── cr{env}                             (Container Registry)
├── kv-{env}                            (Key Vault)
└── ai-{env}                            (Azure AI Foundry project/hub)
    └── gpt-4o deployment
```

**Single resource group** — appropriate for PoC. In production, you'd split into platform/app/shared layers.

---

## Shared Infrastructure

| Resource | Purpose | Consumed By |
|----------|---------|-------------|
| **Key Vault** (`kv-{env}`) | API keys, AI Foundry connection string, any shared secrets | All 4 components |
| **Azure AI Foundry** (`ai-{env}`) | GPT-4o model deployment, Agent Service | Python Resolution API |
| **Container Registry** (`cr{env}`) | Docker images for containerized services | MCP Server, Python Resolution API |
| **App Service Plan** (`plan-{env}`) | Shared compute for .NET services | TicketsNow, Blazor UI |
| **Log Analytics Workspace** | Centralized logging & monitoring | All components |

---

## Ingress Configuration Summary

| Service | Ingress Type | FQDN Pattern | Auth |
|---------|-------------|--------------|------|
| Blazor UI | Public (App Service) | `app-{env}-web.azurewebsites.net` | None (demo) |
| TicketsNow API | Public (App Service) | `app-{env}-ticketsnow.azurewebsites.net` | API Key header |
| Python Resolution API | External (Container App) | `ca-{env}-resolution-api.{region}.azurecontainerapps.io` | API Key / Entra ID |
| MCP Server | **Internal** (Container App) | `ca-{env}-mcp-server.internal.{cae-domain}` | Managed Identity |

---

## Sequence of Deployment (azd up)

```
1. Provision shared infra     → Key Vault, Log Analytics, ACR, AI Foundry
2. Provision App Service Plan → B1 Linux
3. Deploy TicketsNow API      → App Service (.NET 8 publish)
4. Deploy Blazor UI           → App Service (.NET 8 publish)
5. Provision Container Env    → Container Apps Environment
6. Build & push images        → ACR (Python API + MCP Server)
7. Deploy MCP Server          → Container App (internal ingress)
8. Deploy Python Resolution   → Container App (external ingress)
9. Wire app settings          → Point Blazor at Resolution API + TicketsNow URLs
```

---

## Design Decisions & Trade-offs

| Decision | Rationale | Trade-off |
|----------|-----------|-----------|
| App Service for TicketsNow (not Container App) | Simulates an "external" system; App Service keeps it separated from the agent ecosystem | Slightly different deployment model than the agents |
| Container Apps over AKS | PoC doesn't need K8s complexity; Container Apps gives scale-to-zero, simple networking, and revision management | Less control over networking and scheduling |
| Single resource group | Simplicity for PoC; one `azd down` cleans everything | Not suitable for multi-team production |
| Internal ingress for MCP Server | Security — only the Python agents should talk to it | Blazor UI cannot directly call MCP (by design) |
| Shared App Service Plan for UI + TicketsNow | Cost savings — both are lightweight .NET apps | They share compute ceiling (B1 = 1 core) |

---

## NFR Considerations (PoC-appropriate)

| NFR | Approach |
|-----|----------|
| **Scalability** | Container Apps auto-scales Python API (0→5 replicas). TicketsNow is static load. |
| **Security** | Key Vault for secrets, managed identity for service-to-service, internal ingress for MCP, HTTPS everywhere |
| **Reliability** | Container Apps health probes + automatic restarts. App Service always-on for TicketsNow. |
| **Observability** | Log Analytics workspace collects logs from all components. Application Insights optional add-on. |
| **Cost** | Scale-to-zero on Container Apps, B1 shared plan ≈ $13/month, ACR Basic ≈ $5/month. Total PoC ~$50-80/month + AI usage. |

---

## Next Steps

1. **Enable `deployBackend = true`** in `resources.bicep` and flesh out the Container App modules already stubbed in `infra/modules/`
2. **Add TicketsNow as a second App Service** to the existing App Service Plan
3. **Configure AI Foundry** — deploy GPT-4o model, create Agent Service connection
4. **Set up managed identities** for Container Apps → Key Vault and → AI Foundry access
5. **Update `azure.yaml`** to declare all 4 services for `azd` orchestration
