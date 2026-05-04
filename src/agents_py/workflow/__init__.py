import sys
import os
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from typing import Union
from agent_framework import WorkflowBuilder, WorkflowContext, FunctionExecutor, Case, Default
from shared.messages import TicketInput, IncidentRoute, RequestRoute, ResolutionProposal, EscalationRoute
from shared.client import get_client
from shared.mcp_tools import create_mcp_tool

import json
import re

CONFIDENCE_THRESHOLD = 0.80


def _parse_resolution_json(text: str) -> dict:
    """Extract the JSON block from an agent response."""
    match = re.search(r"```json\s*(.*?)\s*```", text, re.DOTALL)
    if match:
        return json.loads(match.group(1))
    # Fallback: try to find a raw JSON object
    match = re.search(r'\{[^{}]*"confidence"[^{}]*\}', text, re.DOTALL)
    if match:
        return json.loads(match.group(0))
    return {}


async def classify_ticket(
    msg: "Union[str, TicketInput]",
    ctx: WorkflowContext[Union[IncidentRoute, RequestRoute]],
) -> None:
    """Entry point: accept plain text (from DevUI) or TicketInput, classify the ticket."""
    ticket_number = msg.strip() if isinstance(msg, str) else msg.ticket_number

    from agent_framework import AgentContext
    from agents.classifier import agent as classifier_agent

    agent_ctx = AgentContext()
    result = await classifier_agent.run(
        f"Please classify ticket number: {ticket_number}", agent_ctx
    )
    response_text = str(result)

    # Extract ticket id and short description from agent response if available
    ticket_id = ticket_number  # fallback
    short_description = ticket_number

    if hasattr(result, "tool_results"):
        for tr in result.tool_results:
            try:
                data = json.loads(tr)
                if "id" in data:
                    ticket_id = data["id"]
                    short_description = data.get("shortDescription", ticket_number)
                    break
            except Exception:
                pass

    if "INCIDENT:" in response_text.upper() or "incident" in response_text.lower():
        await ctx.send_message(IncidentRoute(
            ticket_number=ticket_number,
            ticket_id=ticket_id,
            short_description=short_description,
        ))
    else:
        await ctx.send_message(RequestRoute(
            ticket_number=ticket_number,
            ticket_id=ticket_id,
            short_description=short_description,
        ))


async def resolve_incident(
    msg: IncidentRoute,
    ctx: WorkflowContext[ResolutionProposal],
) -> None:
    """Search KB for incident resolution and emit a ResolutionProposal."""
    from agent_framework import AgentContext
    from agents.incident import agent as incident_agent

    agent_ctx = AgentContext()
    result = await incident_agent.run(
        f"Find a resolution for incident ticket: {msg.ticket_number}", agent_ctx
    )
    response_text = str(result)

    parsed = _parse_resolution_json(response_text)
    confidence = float(parsed.get("confidence", 0.0))
    resolution_text = parsed.get("resolution_text", response_text)
    kb_source = parsed.get("kb_source")
    # Agent may return the ticket_id in JSON — use it if available (more reliable than classifier)
    ticket_id = parsed.get("ticket_id") or msg.ticket_id

    await ctx.send_message(ResolutionProposal(
        ticket_number=msg.ticket_number,
        ticket_id=ticket_id,
        short_description=msg.short_description,
        resolution_text=resolution_text,
        confidence=confidence,
        ticket_type="incident",
        kb_source=kb_source,
    ))


async def resolve_request(
    msg: RequestRoute,
    ctx: WorkflowContext[ResolutionProposal],
) -> None:
    """Search KB for request fulfillment and emit a ResolutionProposal."""
    from agent_framework import AgentContext
    from agents.request import agent as request_agent

    agent_ctx = AgentContext()
    result = await request_agent.run(
        f"Find a fulfillment procedure for service request: {msg.ticket_number}", agent_ctx
    )
    response_text = str(result)

    parsed = _parse_resolution_json(response_text)
    confidence = float(parsed.get("confidence", 0.0))
    resolution_text = parsed.get("resolution_text", response_text)
    kb_source = parsed.get("kb_source")
    ticket_id = parsed.get("ticket_id") or msg.ticket_id

    await ctx.send_message(ResolutionProposal(
        ticket_number=msg.ticket_number,
        ticket_id=ticket_id,
        short_description=msg.short_description,
        resolution_text=resolution_text,
        confidence=confidence,
        ticket_type="request",
        kb_source=kb_source,
    ))


async def apply_resolution(
    msg: ResolutionProposal,
    ctx: WorkflowContext[str],
) -> None:
    """Confidence >= threshold: ResolutionAgent marks the ticket complete."""
    from agent_framework import AgentContext
    from agents.resolution import agent as resolution_agent

    agent_ctx = AgentContext()
    result = await resolution_agent.run(
        f"Apply resolution to ticket {msg.ticket_number}. "
        f"Ticket ID (GUID): {msg.ticket_id}. "
        f"Confidence: {msg.confidence:.2f}. "
        f"KB source: {msg.kb_source or 'N/A'}. "
        f"Resolution: {msg.resolution_text}",
        agent_ctx,
    )
    await ctx.yield_output(str(result))


async def escalate_to_human(
    msg: ResolutionProposal,
    ctx: WorkflowContext[str],
) -> None:
    """Confidence < threshold: EscalationAgent assigns to a human specialist."""
    from agent_framework import AgentContext
    from agents.escalation import agent as escalation_agent

    agent_ctx = AgentContext()
    result = await escalation_agent.run(
        f"Escalate ticket {msg.ticket_number} to a human agent. "
        f"Ticket ID (GUID): {msg.ticket_id}. "
        f"Short description: {msg.short_description}. "
        f"Automated confidence was {msg.confidence:.2f} (below {CONFIDENCE_THRESHOLD} threshold). "
        f"Proposed resolution was: {msg.resolution_text[:300]}",
        agent_ctx,
    )
    await ctx.yield_output(str(result))


# Build the workflow
classifier_exec = FunctionExecutor(classify_ticket, id="ClassifierExecutor")
incident_exec = FunctionExecutor(resolve_incident, id="IncidentResolverExecutor")
request_exec = FunctionExecutor(resolve_request, id="RequestResolverExecutor")
resolution_exec = FunctionExecutor(apply_resolution, id="ResolutionExecutor")
escalation_exec = FunctionExecutor(escalate_to_human, id="EscalationExecutor")

builder = WorkflowBuilder(
    start_executor=classifier_exec,
    name="IT Ticket Resolution",
    description=(
        f"Classifier → Incident/Request Resolver → "
        f"confidence ≥{int(CONFIDENCE_THRESHOLD*100)}% → ResolutionAgent | "
        f"confidence <{int(CONFIDENCE_THRESHOLD*100)}% → EscalationAgent"
    ),
)

# Classifier → Incident or Request resolver
builder.add_switch_case_edge_group(
    classifier_exec,
    cases=[
        Case(condition=lambda m: isinstance(m, IncidentRoute), target=incident_exec),
        Default(target=request_exec),
    ],
)

# Both resolvers emit ResolutionProposal → route by confidence
builder.add_edge(incident_exec, resolution_exec,
                 condition=lambda m: isinstance(m, ResolutionProposal) and m.confidence >= CONFIDENCE_THRESHOLD)
builder.add_edge(incident_exec, escalation_exec,
                 condition=lambda m: isinstance(m, ResolutionProposal) and m.confidence < CONFIDENCE_THRESHOLD)

builder.add_edge(request_exec, resolution_exec,
                 condition=lambda m: isinstance(m, ResolutionProposal) and m.confidence >= CONFIDENCE_THRESHOLD)
builder.add_edge(request_exec, escalation_exec,
                 condition=lambda m: isinstance(m, ResolutionProposal) and m.confidence < CONFIDENCE_THRESHOLD)

workflow = builder.build()

