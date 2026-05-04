import sys
import os
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from typing import Union, Any
from agent_framework import WorkflowBuilder, WorkflowContext, FunctionExecutor, Case, Default
from shared.messages import TicketInput, IncidentRoute, RequestRoute, ResolutionProposal, EscalationRoute

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
    msg: Any,
    ctx: WorkflowContext[Union[IncidentRoute, RequestRoute]],
) -> None:
    """Entry point: accept any message type (str, dict, TicketInput) from DevUI or code."""
    # DevUI sends initial input as a dict e.g. {"content": "INC0010001", "role": "user"}
    if isinstance(msg, str):
        ticket_number = msg.strip()
    elif isinstance(msg, dict):
        ticket_number = (
            msg.get("content") or msg.get("ticket_number") or msg.get("text") or str(msg)
        ).strip()
    elif isinstance(msg, TicketInput):
        ticket_number = msg.ticket_number
    else:
        ticket_number = str(msg).strip()

    from agents.classifier import agent as classifier_agent

    result = await classifier_agent.run(
        f"Please classify ticket number: {ticket_number}"
    )
    response_text = str(result)

    # Ticket id defaults to number — downstream agents fetch the full record via MCP
    ticket_id = ticket_number
    short_description = ticket_number

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
    from agents.incident import agent as incident_agent

    result = await incident_agent.run(
        f"Find a resolution for incident ticket: {msg.ticket_number}"
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
    from agents.request import agent as request_agent

    result = await request_agent.run(
        f"Find a fulfillment procedure for service request: {msg.ticket_number}"
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
    from agents.resolution import agent as resolution_agent

    result = await resolution_agent.run(
        f"Apply resolution to ticket {msg.ticket_number}. "
        f"Ticket ID (GUID): {msg.ticket_id}. "
        f"Confidence: {msg.confidence:.2f}. "
        f"KB source: {msg.kb_source or 'N/A'}. "
        f"Resolution: {msg.resolution_text}"
    )
    await ctx.yield_output(str(result))


async def escalate_to_human(
    msg: ResolutionProposal,
    ctx: WorkflowContext[str],
) -> None:
    """Confidence < threshold: EscalationAgent assigns to a human specialist."""
    from agents.escalation import agent as escalation_agent

    result = await escalation_agent.run(
        f"Escalate ticket {msg.ticket_number} to a human agent. "
        f"Ticket ID (GUID): {msg.ticket_id}. "
        f"Short description: {msg.short_description}. "
        f"Automated confidence was {msg.confidence:.2f} (below {CONFIDENCE_THRESHOLD} threshold). "
        f"Proposed resolution was: {msg.resolution_text[:300]}"
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

