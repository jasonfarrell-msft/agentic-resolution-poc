import sys
import os
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from typing import Union
from agent_framework import WorkflowBuilder, WorkflowContext, FunctionExecutor, Case, Default
from shared.messages import TicketInput, IncidentRoute, RequestRoute, KBSearchResult, ResolutionProposal, EscalationRoute
from tenacity import retry, wait_exponential, stop_after_attempt, retry_if_exception_message

import json
import re


def _is_rate_limit(exc: BaseException) -> bool:
    return "429" in str(exc) or "too_many_requests" in str(exc).lower()


_agent_retry = retry(
    retry=retry_if_exception_message(match=r".*(429|too_many_requests|Too Many Requests).*"),
    wait=wait_exponential(multiplier=2, min=5, max=60),
    stop=stop_after_attempt(5),
    reraise=True,
)

CONFIDENCE_THRESHOLD = 0.80


def _parse_json_block(text: str) -> dict:
    """Extract the JSON block from an agent response."""
    match = re.search(r"```json\s*(.*?)\s*```", text, re.DOTALL)
    if match:
        try:
            return json.loads(match.group(1))
        except json.JSONDecodeError:
            pass
    # Fallback: find a raw JSON object containing known keys
    for key in ("confidence", "ticket_id", "kb_title"):
        match = re.search(r'\{[^{}]*"' + key + r'"[^{}]*\}', text, re.DOTALL)
        if match:
            try:
                return json.loads(match.group(0))
            except json.JSONDecodeError:
                continue
    return {}


async def classify_ticket(
    msg: object,
    ctx: WorkflowContext[Union[IncidentRoute, RequestRoute]],
) -> None:
    """Entry point: accept any message type (str, dict, TicketInput) from DevUI or code."""
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

    result = await _agent_retry(classifier_agent.run)(
        f"Please classify ticket number: {ticket_number}"
    )
    response_text = str(result)

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
    ctx: WorkflowContext[KBSearchResult],
) -> None:
    """Fetch ticket details + KB article content; emit KBSearchResult for evaluation."""
    from agents.incident import agent as incident_agent

    result = await _agent_retry(incident_agent.run)(
        f"Retrieve ticket details and best-matching KB article for incident: {msg.ticket_number}"
    )
    response_text = str(result)

    parsed = _parse_json_block(response_text)
    await ctx.send_message(KBSearchResult(
        ticket_number=msg.ticket_number,
        ticket_id=parsed.get("ticket_id") or msg.ticket_id,
        short_description=msg.short_description,
        ticket_description=parsed.get("ticket_description", ""),
        ticket_category=parsed.get("ticket_category", ""),
        ticket_priority=parsed.get("ticket_priority", ""),
        kb_title=parsed.get("kb_title", "No match found"),
        kb_content=parsed.get("kb_content", "No KB article content available."),
        kb_search_score=float(parsed.get("kb_search_score", 0.0)),
        ticket_type="incident",
    ))


async def resolve_request(
    msg: RequestRoute,
    ctx: WorkflowContext[KBSearchResult],
) -> None:
    """Fetch ticket details + KB article content; emit KBSearchResult for evaluation."""
    from agents.request import agent as request_agent

    result = await _agent_retry(request_agent.run)(
        f"Retrieve ticket details and best-matching KB article for service request: {msg.ticket_number}"
    )
    response_text = str(result)

    parsed = _parse_json_block(response_text)
    await ctx.send_message(KBSearchResult(
        ticket_number=msg.ticket_number,
        ticket_id=parsed.get("ticket_id") or msg.ticket_id,
        short_description=msg.short_description,
        ticket_description=parsed.get("ticket_description", ""),
        ticket_category=parsed.get("ticket_category", ""),
        ticket_priority=parsed.get("ticket_priority", ""),
        kb_title=parsed.get("kb_title", "No match found"),
        kb_content=parsed.get("kb_content", "No KB article content available."),
        kb_search_score=float(parsed.get("kb_search_score", 0.0)),
        ticket_type="request",
    ))


async def evaluate_resolution(
    msg: KBSearchResult,
    ctx: WorkflowContext[ResolutionProposal],
) -> None:
    """EvaluatorAgent reasons about KB-to-ticket fit and assigns calibrated confidence."""
    from agents.evaluator import agent as evaluator_agent

    prompt = (
        f"Evaluate whether the following KB article resolves this ticket.\n\n"
        f"TICKET NUMBER: {msg.ticket_number}\n"
        f"TICKET ID (GUID): {msg.ticket_id}\n"
        f"SHORT DESCRIPTION: {msg.short_description}\n"
        f"FULL DESCRIPTION: {msg.ticket_description or '(not available)'}\n"
        f"CATEGORY: {msg.ticket_category}\n"
        f"PRIORITY: {msg.ticket_priority}\n\n"
        f"KB ARTICLE TITLE: {msg.kb_title}\n"
        f"KB ARTICLE CONTENT:\n{msg.kb_content}\n\n"
        f"Search relevance score (for reference only): {msg.kb_search_score:.2f}\n\n"
        f"Reason through Steps 1–4 and return the JSON block."
    )

    result = await _agent_retry(evaluator_agent.run)(prompt)
    response_text = str(result)

    parsed = _parse_json_block(response_text)
    confidence = float(parsed.get("confidence", 0.0))
    resolution_text = parsed.get("resolution_text", response_text)
    kb_source = parsed.get("kb_source", msg.kb_title)
    ticket_id = parsed.get("ticket_id") or msg.ticket_id

    await ctx.send_message(ResolutionProposal(
        ticket_number=msg.ticket_number,
        ticket_id=ticket_id,
        short_description=msg.short_description,
        resolution_text=resolution_text,
        confidence=confidence,
        ticket_type=msg.ticket_type,
        kb_source=kb_source,
    ))


async def apply_resolution(
    msg: ResolutionProposal,
    ctx: WorkflowContext[str],
) -> None:
    """Confidence >= threshold: ResolutionAgent marks the ticket complete."""
    from agents.resolution import agent as resolution_agent

    result = await _agent_retry(resolution_agent.run)(
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

    result = await _agent_retry(escalation_agent.run)(
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
evaluator_exec = FunctionExecutor(evaluate_resolution, id="EvaluatorExecutor")
resolution_exec = FunctionExecutor(apply_resolution, id="ResolutionExecutor")
escalation_exec = FunctionExecutor(escalate_to_human, id="EscalationExecutor")

builder = WorkflowBuilder(
    start_executor=classifier_exec,
    name="IT Ticket Resolution",
    description=(
        f"Classifier → Incident/Request Retriever → Evaluator → "
        f"confidence ≥{int(CONFIDENCE_THRESHOLD*100)}% → ResolutionAgent | "
        f"confidence <{int(CONFIDENCE_THRESHOLD*100)}% → EscalationAgent"
    ),
)

# Classifier → Incident or Request retriever
builder.add_switch_case_edge_group(
    classifier_exec,
    cases=[
        Case(condition=lambda m: isinstance(m, IncidentRoute), target=incident_exec),
        Default(target=request_exec),
    ],
)

# Both retrievers → Evaluator
builder.add_edge(incident_exec, evaluator_exec,
                 condition=lambda m: isinstance(m, KBSearchResult))
builder.add_edge(request_exec, evaluator_exec,
                 condition=lambda m: isinstance(m, KBSearchResult))

# Evaluator → Resolution or Escalation based on calibrated confidence
builder.add_edge(evaluator_exec, resolution_exec,
                 condition=lambda m: isinstance(m, ResolutionProposal) and m.confidence >= CONFIDENCE_THRESHOLD)
builder.add_edge(evaluator_exec, escalation_exec,
                 condition=lambda m: isinstance(m, ResolutionProposal) and m.confidence < CONFIDENCE_THRESHOLD)

workflow = builder.build()


