import sys
import os
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from typing import Union
from agent_framework import WorkflowBuilder, WorkflowContext, FunctionExecutor, Case, Default
from shared.messages import TicketInput, IncidentRoute, RequestRoute, EscalationRoute
from shared.client import get_client
from shared.mcp_tools import create_mcp_tool

import json


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

    # Parse classification from agent response
    ticket_id = None
    short_description = ticket_number

    # Try to extract ticket details from agent's tool calls if available
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

    if ticket_id is None:
        ticket_id = ticket_number  # fallback

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


async def handle_incident(
    msg: IncidentRoute,
    ctx: WorkflowContext[EscalationRoute],
) -> None:
    """Attempt to auto-resolve an incident; escalate if confidence < 0.8."""
    from agent_framework import AgentContext
    from agents.incident import agent as incident_agent

    agent_ctx = AgentContext()
    result = await incident_agent.run(
        f"Process incident ticket: {msg.ticket_number}", agent_ctx
    )
    response_text = str(result)

    # If the agent escalated, route to escalation
    if "escalate" in response_text.lower() or "escalation" in response_text.lower():
        await ctx.send_message(EscalationRoute(
            ticket_number=msg.ticket_number,
            ticket_id=msg.ticket_id,
            reason=f"Incident agent escalated: low confidence or no KB match",
            confidence=0.0,
        ))
    else:
        await ctx.yield_output(response_text)


async def handle_request(
    msg: RequestRoute,
    ctx: WorkflowContext[EscalationRoute],
) -> None:
    """Attempt to fulfill a service request; escalate if approval needed."""
    from agent_framework import AgentContext
    from agents.request import agent as request_agent

    agent_ctx = AgentContext()
    result = await request_agent.run(
        f"Process service request ticket: {msg.ticket_number}", agent_ctx
    )
    response_text = str(result)

    if "escalat" in response_text.lower() or "approval" in response_text.lower():
        await ctx.send_message(EscalationRoute(
            ticket_number=msg.ticket_number,
            ticket_id=msg.ticket_id,
            reason=f"Request agent: requires manual approval or fulfillment",
            confidence=0.0,
        ))
    else:
        await ctx.yield_output(response_text)


async def handle_escalation(
    msg: EscalationRoute,
    ctx: WorkflowContext[str],
) -> None:
    """Route an escalated ticket to the appropriate support group."""
    from agent_framework import AgentContext
    from agents.escalation import agent as escalation_agent

    agent_ctx = AgentContext()
    result = await escalation_agent.run(
        f"Route escalated ticket: {msg.ticket_number}. Reason: {msg.reason}", agent_ctx
    )
    await ctx.yield_output(str(result))


# Build the workflow
classifier_exec = FunctionExecutor(classify_ticket, id="ClassifierExecutor")
incident_exec = FunctionExecutor(handle_incident, id="IncidentExecutor")
request_exec = FunctionExecutor(handle_request, id="RequestExecutor")
escalation_exec = FunctionExecutor(handle_escalation, id="EscalationExecutor")

builder = WorkflowBuilder(
    start_executor=classifier_exec,
    name="IT Ticket Resolution",
    description="Multi-agent workflow: Classifier → Incident/Request → Escalation",
)

# Classifier routes to Incident or Request
builder.add_switch_case_edge_group(
    classifier_exec,
    cases=[
        Case(condition=lambda m: isinstance(m, IncidentRoute), target=incident_exec),
        Default(target=request_exec),
    ],
)

# Incident and Request both route to Escalation if needed
builder.add_edge(incident_exec, escalation_exec, condition=lambda m: isinstance(m, EscalationRoute))
builder.add_edge(request_exec, escalation_exec, condition=lambda m: isinstance(m, EscalationRoute))

workflow = builder.build()
