import sys
import os
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from typing import Union
from agent_framework import WorkflowBuilder, WorkflowContext, FunctionExecutor, Case, Default
from shared.messages import (
    TicketInput, IncidentRoute, RequestRoute, TicketDetails, 
    ResolutionAnalysis, ResolutionProposal, EscalationRoute, ResolutionQuestion
)
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
    for key in ("confidence", "ticket_id", "core_problem", "preliminary_confidence", "questions"):
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


async def fetch_incident_details(
    msg: IncidentRoute,
    ctx: WorkflowContext[TicketDetails],
) -> None:
    """Fetch ticket details for incident (no KB search)."""
    from agents.incident import agent as incident_agent

    result = await _agent_retry(incident_agent.run)(
        f"Retrieve ticket details for incident: {msg.ticket_number}"
    )
    response_text = str(result)

    parsed = _parse_json_block(response_text)
    await ctx.send_message(TicketDetails(
        ticket_number=msg.ticket_number,
        ticket_id=parsed.get("ticket_id") or msg.ticket_id,
        short_description=msg.short_description,
        ticket_description=parsed.get("ticket_description", ""),
        ticket_category=parsed.get("ticket_category", ""),
        ticket_priority=parsed.get("ticket_priority", ""),
        ticket_type="incident",
    ))


async def fetch_request_details(
    msg: RequestRoute,
    ctx: WorkflowContext[TicketDetails],
) -> None:
    """Fetch ticket details for service request (no KB search)."""
    from agents.request import agent as request_agent

    result = await _agent_retry(request_agent.run)(
        f"Retrieve ticket details for service request: {msg.ticket_number}"
    )
    response_text = str(result)

    parsed = _parse_json_block(response_text)
    await ctx.send_message(TicketDetails(
        ticket_number=msg.ticket_number,
        ticket_id=parsed.get("ticket_id") or msg.ticket_id,
        short_description=msg.short_description,
        ticket_description=parsed.get("ticket_description", ""),
        ticket_category=parsed.get("ticket_category", ""),
        ticket_priority=parsed.get("ticket_priority", ""),
        ticket_type="request",
    ))


def _build_decomposer_prompt(msg: TicketDetails, ticket_label: str) -> str:
    return (
        f"Analyze this {ticket_label} ticket and perform targeted knowledge base searches.\n\n"
        f"TICKET NUMBER: {msg.ticket_number}\n"
        f"TICKET ID (GUID): {msg.ticket_id}\n"
        f"SHORT DESCRIPTION: {msg.short_description}\n"
        f"FULL DESCRIPTION: {msg.ticket_description or '(not available)'}\n"
        f"CATEGORY: {msg.ticket_category}\n"
        f"PRIORITY: {msg.ticket_priority}\n\n"
        f"Follow the workflow in your instructions and return the JSON block with "
        f"ticket_id, core_problem, questions (array), and preliminary_confidence."
    )


def _parse_questions(parsed: dict) -> list:
    questions = []
    for q in parsed.get("questions", []):
        if isinstance(q, dict):
            questions.append(ResolutionQuestion(
                question=q.get("question", ""),
                search_terms=q.get("search_terms", ""),
                answer=q.get("answer", ""),
                kb_sources=q.get("kb_sources", []),
            ))
    return questions


async def decompose_incident(
    msg: TicketDetails,
    ctx: WorkflowContext[ResolutionAnalysis],
) -> None:
    """IncidentDecomposer: diagnosis-oriented KB search for incidents."""
    from agents.incident_decomposer import agent as incident_decomposer_agent

    result = await _agent_retry(incident_decomposer_agent.run)(
        _build_decomposer_prompt(msg, "incident")
    )
    parsed = _parse_json_block(str(result))

    await ctx.send_message(ResolutionAnalysis(
        ticket_number=msg.ticket_number,
        ticket_id=parsed.get("ticket_id") or msg.ticket_id,
        short_description=msg.short_description,
        ticket_description=msg.ticket_description,
        ticket_category=msg.ticket_category,
        ticket_priority=msg.ticket_priority,
        ticket_type=msg.ticket_type,
        core_problem=parsed.get("core_problem", "Problem analysis not available"),
        questions=_parse_questions(parsed),
        preliminary_confidence=float(parsed.get("preliminary_confidence", 0.0)),
    ))


async def decompose_request(
    msg: TicketDetails,
    ctx: WorkflowContext[ResolutionAnalysis],
) -> None:
    """RequestDecomposer: fulfillment-oriented KB search for service requests."""
    from agents.request_decomposer import agent as request_decomposer_agent

    result = await _agent_retry(request_decomposer_agent.run)(
        _build_decomposer_prompt(msg, "service request")
    )
    parsed = _parse_json_block(str(result))

    await ctx.send_message(ResolutionAnalysis(
        ticket_number=msg.ticket_number,
        ticket_id=parsed.get("ticket_id") or msg.ticket_id,
        short_description=msg.short_description,
        ticket_description=msg.ticket_description,
        ticket_category=msg.ticket_category,
        ticket_priority=msg.ticket_priority,
        ticket_type=msg.ticket_type,
        core_problem=parsed.get("core_problem", "Problem analysis not available"),
        questions=_parse_questions(parsed),
        preliminary_confidence=float(parsed.get("preliminary_confidence", 0.0)),
    ))


async def evaluate_resolution(
    msg: ResolutionAnalysis,
    ctx: WorkflowContext[ResolutionProposal],
) -> None:
    """EvaluatorAgent receives ResolutionAnalysis and assigns calibrated confidence."""
    from agents.evaluator import agent as evaluator_agent

    # Format questions + answers for the evaluator
    qa_text = "\n\n".join([
        f"Q{i+1}: {q.question}\nSearch terms used: {q.search_terms}\nAnswer: {q.answer}\nKB sources: {', '.join(q.kb_sources) if q.kb_sources else 'None'}"
        for i, q in enumerate(msg.questions)
    ])

    prompt = (
        f"Evaluate whether the synthesized answers resolve this ticket.\n\n"
        f"TICKET NUMBER: {msg.ticket_number}\n"
        f"TICKET ID (GUID): {msg.ticket_id}\n"
        f"SHORT DESCRIPTION: {msg.short_description}\n"
        f"FULL DESCRIPTION: {msg.ticket_description or '(not available)'}\n"
        f"CATEGORY: {msg.ticket_category}\n"
        f"PRIORITY: {msg.ticket_priority}\n\n"
        f"CORE PROBLEM (from decomposer):\n{msg.core_problem}\n\n"
        f"QUESTIONS & ANSWERS:\n{qa_text}\n\n"
        f"PRELIMINARY CONFIDENCE (from decomposer): {msg.preliminary_confidence:.2f}\n\n"
        f"Review the analysis and return your calibrated confidence + consolidated resolution_text."
    )

    result = await _agent_retry(evaluator_agent.run)(prompt)
    response_text = str(result)

    parsed = _parse_json_block(response_text)
    confidence = float(parsed.get("confidence", 0.0))
    resolution_text = parsed.get("resolution_text", response_text)
    kb_source = parsed.get("kb_source", "; ".join([
        src for q in msg.questions for src in q.kb_sources
    ]) or "No KB source")
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
incident_exec = FunctionExecutor(fetch_incident_details, id="IncidentFetchExecutor")
request_exec = FunctionExecutor(fetch_request_details, id="RequestFetchExecutor")
incident_decomposer_exec = FunctionExecutor(decompose_incident, id="IncidentDecomposerExecutor")
request_decomposer_exec = FunctionExecutor(decompose_request, id="RequestDecomposerExecutor")
evaluator_exec = FunctionExecutor(evaluate_resolution, id="EvaluatorExecutor")
resolution_exec = FunctionExecutor(apply_resolution, id="ResolutionExecutor")
escalation_exec = FunctionExecutor(escalate_to_human, id="EscalationExecutor")

builder = WorkflowBuilder(
    start_executor=classifier_exec,
    name="IT Ticket Resolution",
    description=(
        f"Classifier → Incident Fetch → IncidentDecomposer | Request Fetch → RequestDecomposer → "
        f"Evaluator → confidence ≥{int(CONFIDENCE_THRESHOLD*100)}% → ResolutionAgent | "
        f"confidence <{int(CONFIDENCE_THRESHOLD*100)}% → EscalationAgent"
    ),
)

# Classifier → Incident or Request fetcher
builder.add_switch_case_edge_group(
    classifier_exec,
    cases=[
        Case(condition=lambda m: isinstance(m, IncidentRoute), target=incident_exec),
        Default(target=request_exec),
    ],
)

# Incident fetcher → IncidentDecomposer; Request fetcher → RequestDecomposer
builder.add_edge(incident_exec, incident_decomposer_exec,
                 condition=lambda m: isinstance(m, TicketDetails))
builder.add_edge(request_exec, request_decomposer_exec,
                 condition=lambda m: isinstance(m, TicketDetails))

# Both decomposers → Evaluator
builder.add_edge(incident_decomposer_exec, evaluator_exec,
                 condition=lambda m: isinstance(m, ResolutionAnalysis))
builder.add_edge(request_decomposer_exec, evaluator_exec,
                 condition=lambda m: isinstance(m, ResolutionAnalysis))

# Evaluator → Resolution or Escalation based on calibrated confidence
builder.add_edge(evaluator_exec, resolution_exec,
                 condition=lambda m: isinstance(m, ResolutionProposal) and m.confidence >= CONFIDENCE_THRESHOLD)
builder.add_edge(evaluator_exec, escalation_exec,
                 condition=lambda m: isinstance(m, ResolutionProposal) and m.confidence < CONFIDENCE_THRESHOLD)

workflow = builder.build()

