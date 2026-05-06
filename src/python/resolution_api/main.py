import sys
import os
from pathlib import Path

# Fix Python PATH on Windows before importing agent_framework packages
python_root = Path(__file__).parent.parent
sys.path.insert(0, str(python_root))

import asyncio
import json
from datetime import datetime
from typing import AsyncGenerator
from fastapi import FastAPI, HTTPException
from fastapi.responses import StreamingResponse
from pydantic import BaseModel
from contextlib import asynccontextmanager

from agent_framework import WorkflowContext
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
    
    class EventCapture:
        """Captures workflow events by monitoring context state."""
        def __init__(self):
            self.current_stage = None
            self.events = []
            
        async def emit(self, stage: str, status: str, result: dict = None):
            event_data = {
                "stage": stage,
                "status": status,
                "timestamp": datetime.utcnow().isoformat() + "Z"
            }
            if result is not None:
                event_data["result"] = result
            
            self.events.append(event_data)
            return f"data: {json.dumps(event_data)}\n\n"
    
    capture = EventCapture()
    
    try:
        # Emit classifier started
        yield await capture.emit("classifier", "started")
        
        # Create workflow context and execute
        ticket_input = TicketInput(ticket_number=ticket_number)
        
        # Track workflow execution through message types
        class WorkflowMonitor:
            def __init__(self):
                self.stage = "classifier"
                self.ticket_type = None
                
        monitor = WorkflowMonitor()
        
        # Execute workflow and capture outputs
        outputs = []
        async for output in workflow.run_stream(ticket_input):
            # Track stage transitions based on output type
            output_type = type(output).__name__
            
            if output_type == "IncidentRoute":
                yield await capture.emit("classifier", "completed", {"type": "incident", "ticket_number": output.ticket_number})
                monitor.ticket_type = "incident"
                monitor.stage = "incident_fetch"
                yield await capture.emit("incident_fetch", "started")
                
            elif output_type == "RequestRoute":
                yield await capture.emit("classifier", "completed", {"type": "request", "ticket_number": output.ticket_number})
                monitor.ticket_type = "request"
                monitor.stage = "request_fetch"
                yield await capture.emit("request_fetch", "started")
                
            elif output_type == "TicketDetails":
                if monitor.ticket_type == "incident":
                    yield await capture.emit("incident_fetch", "completed", {
                        "ticket_id": output.ticket_id,
                        "category": output.ticket_category,
                        "priority": output.ticket_priority
                    })
                    monitor.stage = "incident_decomposer"
                    yield await capture.emit("incident_decomposer", "started")
                else:
                    yield await capture.emit("request_fetch", "completed", {
                        "ticket_id": output.ticket_id,
                        "category": output.ticket_category,
                        "priority": output.ticket_priority
                    })
                    monitor.stage = "request_decomposer"
                    yield await capture.emit("request_decomposer", "started")
                    
            elif output_type == "ResolutionAnalysis":
                if monitor.ticket_type == "incident":
                    yield await capture.emit("incident_decomposer", "completed", {
                        "core_problem": output.core_problem,
                        "questions_count": len(output.questions),
                        "preliminary_confidence": output.preliminary_confidence
                    })
                else:
                    yield await capture.emit("request_decomposer", "completed", {
                        "core_problem": output.core_problem,
                        "questions_count": len(output.questions),
                        "preliminary_confidence": output.preliminary_confidence
                    })
                monitor.stage = "evaluator"
                yield await capture.emit("evaluator", "started")
                
            elif output_type == "ResolutionProposal":
                yield await capture.emit("evaluator", "completed", {
                    "confidence": output.confidence,
                    "kb_source": output.kb_source
                })
                
                # Determine next stage based on confidence
                from workflow import CONFIDENCE_THRESHOLD
                if output.confidence >= CONFIDENCE_THRESHOLD:
                    monitor.stage = "resolution"
                    yield await capture.emit("resolution", "started")
                else:
                    monitor.stage = "escalation"
                    yield await capture.emit("escalation", "started")
                    
            elif isinstance(output, str):
                # Final output from resolution or escalation agent
                if monitor.stage == "resolution":
                    yield await capture.emit("resolution", "completed", {"output": output[:200]})
                elif monitor.stage == "escalation":
                    yield await capture.emit("escalation", "completed", {"output": output[:200]})
                outputs.append(output)
        
        # If we got here, workflow completed successfully
        if not outputs:
            # No final output received, emit generic completion
            yield await capture.emit(monitor.stage, "completed")
            
    except Exception as e:
        error_event = {
            "stage": "error",
            "status": "failed",
            "error": str(e),
            "timestamp": datetime.utcnow().isoformat() + "Z"
        }
        yield f"data: {json.dumps(error_event)}\n\n"


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
