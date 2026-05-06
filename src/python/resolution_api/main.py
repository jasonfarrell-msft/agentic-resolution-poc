import sys
import os
from pathlib import Path

# Fix Python PATH on Windows before importing agent_framework packages
python_root = Path(__file__).parent.parent
sys.path.insert(0, str(python_root))

import asyncio
import json
from dataclasses import asdict, is_dataclass
from datetime import datetime
from typing import Any, AsyncGenerator
from fastapi import FastAPI, HTTPException
from fastapi.responses import StreamingResponse
from pydantic import BaseModel
from contextlib import asynccontextmanager

from shared.messages import TicketInput
from workflow import workflow

app = FastAPI(
    title="Agentic Resolution API",
    description="Python Resolution API for ticket workflow orchestration",
    version="1.0.0"
)


class ResolveRequest(BaseModel):
    ticket_number: str


class HealthResponse(BaseModel):
    status: str
    timestamp: str


@asynccontextmanager
async def lifespan(app: FastAPI):
    """Lifespan event handler for startup/shutdown."""
    print("Resolution API starting up...")
    yield
    print("Resolution API shutting down...")


app = FastAPI(
    title="Agentic Resolution API",
    description="Python Resolution API for ticket workflow orchestration",
    version="1.0.0",
    lifespan=lifespan
)


@app.get("/", response_model=HealthResponse)
async def root():
    """Root endpoint for health check."""
    return HealthResponse(
        status="healthy",
        timestamp=datetime.utcnow().isoformat() + "Z"
    )


@app.get("/health", response_model=HealthResponse)
async def health():
    """Health check endpoint for Azure Container Apps."""
    return HealthResponse(
        status="healthy",
        timestamp=datetime.utcnow().isoformat() + "Z"
    )


async def workflow_event_stream(ticket_number: str) -> AsyncGenerator[str, None]:
    """
    Execute the workflow and stream events as SSE.
    
    Event format:
    data: {"stage": "classifier", "status": "started", "timestamp": "..."}
    data: {"stage": "classifier", "status": "completed", "result": {...}}
    
    Stages:
    - classifier
    - incident_fetch / request_fetch
    - incident_decomposer / request_decomposer
    - evaluator
    - resolution / escalation
    """
    run_timeout_seconds = int(os.environ.get("RESOLUTION_RUN_TIMEOUT_SECONDS", "240"))

    class EventCapture:
        """Captures workflow events by monitoring context state."""
        def __init__(self):
            self.events = []
            self.terminal_emitted = False
            
        async def emit(
            self,
            stage: str,
            status: str,
            result: dict | None = None,
            message: str | None = None,
            error: str | None = None,
            terminal: bool = False,
        ):
            event_data = {
                "stage": stage,
                "status": status,
                "timestamp": datetime.utcnow().isoformat() + "Z"
            }
            if result is not None:
                event_data["result"] = result
            if message is not None:
                event_data["message"] = message
            if error is not None:
                event_data["error"] = error
            if terminal:
                event_data["event"] = status
                event_data["terminal"] = True
                self.terminal_emitted = True
             
            self.events.append(event_data)
            return f"data: {json.dumps(event_data)}\n\n"

    def to_jsonable(value: Any) -> Any:
        if is_dataclass(value):
            return asdict(value)
        if isinstance(value, (str, int, float, bool)) or value is None:
            return value
        if isinstance(value, list):
            return [to_jsonable(item) for item in value]
        if isinstance(value, dict):
            return {str(key): to_jsonable(item) for key, item in value.items()}
        return str(value)

    def stage_for_executor(executor_id: str | None) -> str:
        return {
            "ClassifierExecutor": "classifier",
            "IncidentFetchExecutor": "incident_fetch",
            "RequestFetchExecutor": "request_fetch",
            "IncidentDecomposerExecutor": "incident_decomposer",
            "RequestDecomposerExecutor": "request_decomposer",
            "EvaluatorExecutor": "evaluator",
            "ResolutionExecutor": "resolution",
            "EscalationExecutor": "escalation",
        }.get(executor_id or "", "workflow")

    def summarize_executor_result(executor_id: str | None, data: Any) -> dict:
        outputs = data if isinstance(data, list) else [data]
        json_outputs = [to_jsonable(item) for item in outputs]
        primary = next((item for item in outputs if item is not None), None)
        primary_json = to_jsonable(primary)

        if executor_id == "ClassifierExecutor":
            route_type = type(primary).__name__
            return {
                "type": "incident" if route_type == "IncidentRoute" else "request",
                "output": primary_json,
            }
        if executor_id in ("IncidentFetchExecutor", "RequestFetchExecutor"):
            return primary_json if isinstance(primary_json, dict) else {"output": primary_json}
        if executor_id in ("IncidentDecomposerExecutor", "RequestDecomposerExecutor"):
            if isinstance(primary_json, dict):
                return {
                    "core_problem": primary_json.get("core_problem"),
                    "questions_count": len(primary_json.get("questions") or []),
                    "preliminary_confidence": primary_json.get("preliminary_confidence"),
                }
            return {"output": primary_json}
        if executor_id == "EvaluatorExecutor":
            if isinstance(primary_json, dict):
                return {
                    "confidence": primary_json.get("confidence"),
                    "kb_source": primary_json.get("kb_source"),
                }
            return {"output": primary_json}
        if executor_id in ("ResolutionExecutor", "EscalationExecutor"):
            text = " ".join(str(item) for item in outputs if item is not None)
            return {"output": text[:500]}

        return {"outputs": json_outputs}
     
    capture = EventCapture()
     
    try:
        ticket_input = TicketInput(ticket_number=ticket_number)
        stream = workflow.run(ticket_input, stream=True)

        async with asyncio.timeout(run_timeout_seconds):
            async for event in stream:
                event_type = getattr(event, "type", None)
                executor_id = getattr(event, "executor_id", None)
                stage = stage_for_executor(executor_id)

                if event_type == "executor_invoked":
                    yield await capture.emit(stage, "started")
                elif event_type == "executor_completed":
                    result = summarize_executor_result(executor_id, getattr(event, "data", None))
                    yield await capture.emit(stage, "completed", result)

                    if executor_id == "ResolutionExecutor":
                        yield await capture.emit(
                            "workflow",
                            "resolved",
                            result,
                            message="Ticket resolution completed.",
                            terminal=True,
                        )
                    elif executor_id == "EscalationExecutor":
                        yield await capture.emit(
                            "workflow",
                            "escalated",
                            result,
                            message="Ticket was escalated to a human assignee.",
                            terminal=True,
                        )
                elif event_type in ("executor_failed", "failed", "error"):
                    detail = to_jsonable(getattr(event, "details", None) or getattr(event, "data", None))
                    yield await capture.emit(
                        stage,
                        "failed",
                        {"details": detail},
                        error=str(detail),
                        terminal=True,
                    )
                    return

            await stream.get_final_response()

        if not capture.terminal_emitted:
            yield await capture.emit(
                "workflow",
                "completed",
                message="Workflow completed without a resolution or escalation action.",
                terminal=True,
            )
    except TimeoutError:
        yield await capture.emit(
            "workflow",
            "failed",
            error=f"Resolution workflow exceeded {run_timeout_seconds} seconds.",
            terminal=True,
        )
    except Exception as e:
        yield await capture.emit("workflow", "failed", error=str(e), terminal=True)


@app.post("/resolve")
async def resolve_ticket(request: ResolveRequest):
    """
    Start resolution workflow for a ticket and stream SSE events.
    
    Returns:
        StreamingResponse with text/event-stream content type
        
    Event format:
        data: {"stage": "classifier", "status": "started", "timestamp": "2026-05-06T12:34:56Z"}
        data: {"stage": "classifier", "status": "completed", "result": {"type": "incident"}}
    """
    ticket_number = request.ticket_number.strip()
    
    if not ticket_number:
        raise HTTPException(status_code=400, detail="ticket_number is required")
    
    return StreamingResponse(
        workflow_event_stream(ticket_number),
        media_type="text/event-stream",
        headers={
            "Cache-Control": "no-cache",
            "Connection": "keep-alive",
            "X-Accel-Buffering": "no"
        }
    )


if __name__ == "__main__":
    import uvicorn
    port = int(os.environ.get("PORT", "8000"))
    uvicorn.run(app, host="0.0.0.0", port=port)
