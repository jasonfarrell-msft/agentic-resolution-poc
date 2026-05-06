# Resolution API

Python FastAPI service that orchestrates the existing agent workflow and streams execution events via Server-Sent Events (SSE).

## Purpose

This service is deployed as `ca-resolution` in Azure Container Apps. It provides a clean API interface for the Blazor frontend to trigger ticket resolution workflows without depending on the .NET API.

## Architecture

```
Blazor UI → ca-resolution (POST /resolve, SSE events back)
              ↓
           Agent Workflow (classifier → decomposer → evaluator → resolution/escalation)
              ↓
           MCP Server → TicketsNow API (REST)
```

## API Endpoints

### `POST /resolve`

Starts resolution workflow for a ticket. Returns SSE stream of workflow events.

**Request:**
```json
{
  "ticket_number": "INC0010101"
}
```

**Response:**
- Content-Type: `text/event-stream`
- Stream format:

```
data: {"stage": "classifier", "status": "started", "timestamp": "2026-05-06T12:34:56Z"}

data: {"stage": "classifier", "status": "completed", "result": {"type": "incident", "ticket_number": "INC0010101"}}

data: {"stage": "incident_fetch", "status": "started", "timestamp": "..."}

data: {"stage": "incident_fetch", "status": "completed", "result": {...}}

data: {"stage": "incident_decomposer", "status": "started", "timestamp": "..."}

data: {"stage": "incident_decomposer", "status": "completed", "result": {...}}

data: {"stage": "evaluator", "status": "started", "timestamp": "..."}

data: {"stage": "evaluator", "status": "completed", "result": {"confidence": 0.85}}

data: {"stage": "resolution", "status": "started", "timestamp": "..."}

data: {"stage": "resolution", "status": "completed", "result": {"output": "..."}}
```

**Stages:**
- `classifier` — Classify ticket as incident or request
- `incident_fetch` / `request_fetch` — Fetch ticket details via MCP
- `incident_decomposer` / `request_decomposer` — KB-driven question analysis
- `evaluator` — Confidence scoring
- `resolution` (confidence ≥ 80%) — Auto-resolve via MCP
- `escalation` (confidence < 80%) — Escalate to human via MCP

**Error format:**
```
data: {"stage": "error", "status": "failed", "error": "...", "timestamp": "..."}
```

### `GET /health`

Health check endpoint for Azure Container Apps.

**Response:**
```json
{
  "status": "healthy",
  "timestamp": "2026-05-06T12:34:56Z"
}
```

## Local Development

### Prerequisites
1. Python 3.11+
2. Azure OpenAI endpoint with gpt-4o model
3. MCP server running (for ticket operations)

### Setup

```bash
cd src/python/resolution_api
pip install -r requirements.txt
```

### Environment Variables

Create `.env` in `src/python/`:

```bash
AZURE_OPENAI_ENDPOINT=https://oai-agentic-res-src-dev.cognitiveservices.azure.com/
AZURE_OPENAI_MODEL=gpt-5.1-deployment
AZURE_OPENAI_API_VERSION=2024-12-01-preview
```

### Run

```bash
cd src/python
python -m uvicorn resolution_api.main:app --reload --port 8000
```

Or:

```bash
cd src/python/resolution_api
python main.py
```

### Test with curl

```bash
curl -N -X POST http://localhost:8000/resolve \
  -H "Content-Type: application/json" \
  -d '{"ticket_number": "INC0010101"}'
```

## Deployment

### Build Docker Image

```bash
cd C:\Projects\aes\agentic-resolution
docker build -f src/python/resolution_api/Dockerfile -t acragressrcdevtocqjp4pnegfo.azurecr.io/resolution-api:latest .
```

### Push to ACR

```bash
az acr login --name acragressrcdevtocqjp4pnegfo
docker push acragressrcdevtocqjp4pnegfo.azurecr.io/resolution-api:latest
```

### Deploy to Container Apps

```bash
az containerapp update \
  --name ca-resolution \
  --resource-group rg-agentic-res-src-dev \
  --image acragressrcdevtocqjp4pnegfo.azurecr.io/resolution-api:latest
```

## Design Decisions

1. **Thin wrapper:** This service does NOT duplicate agent logic. It imports and orchestrates the existing workflow from `src/python/workflow/`.

2. **SSE streaming:** Real-time event streaming allows Blazor UI to visualize workflow progression without polling.

3. **No run persistence:** Unlike the .NET API (which persists WorkflowRun to SQL), this service is stateless. Run tracking happens in the .NET layer if needed.

4. **MCP dependency:** Agents use MCP tools to interact with TicketsNow API. The MCP server must be running and accessible.

5. **Environment parity:** Uses same Azure OpenAI endpoint and model as devui_serve.py. No configuration drift.

## Integration Points

- **Blazor UI (`app-agentic-resolution-web`):** Calls POST /resolve, consumes SSE stream
- **MCP Server (`ca-mcp-*`):** Agents call MCP tools for ticket CRUD operations
- **TicketsNow API (`ca-api-*`):** MCP server proxies to this REST API
- **Azure OpenAI:** Agents use gpt-4o-mini via Azure OpenAI endpoint

## Differences from devui_serve.py

| devui_serve.py | resolution_api/main.py |
|----------------|------------------------|
| Development UI (agent_framework_devui) | Production API (FastAPI) |
| Auto-opens browser | Headless service |
| Interactive agent testing | Automated workflow orchestration |
| Single-threaded sync | Async SSE streaming |
| Port 8080 | Port 8000 (or Azure-injected PORT) |

## Known Limitations

1. **No request cancellation:** Once workflow starts, it runs to completion. Client disconnect does not stop agents.
2. **No run history:** Events are streamed but not persisted. For audit trail, integrate with .NET WorkflowRun table.
3. **Single-tenant:** No isolation between concurrent requests. For production multi-tenancy, add request scoping.

## Next Steps (Future Enhancements)

- [ ] Add request cancellation via CancelToken
- [ ] Integrate with .NET WorkflowRun table for persistence
- [ ] Add authentication (Azure AD or API key)
- [ ] Add Prometheus metrics endpoint
- [ ] Add structured logging to Application Insights
- [ ] Support batch resolution (multiple tickets)
