import sys
import os
import json

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))

from agent_framework import Agent
from shared.client import get_client
from shared.mcp_tools import create_mcp_tool

_MATRIX_PATH = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
                             "shared", "assignee_matrix.json")

def _build_assignee_table() -> str:
    with open(_MATRIX_PATH, encoding="utf-8") as f:
        matrix = json.load(f)
    rows = []
    for a in matrix["assignees"]:
        note = a.get("note", "")
        handles = ", ".join(a["handles"]) if not note else note
        priorities = ", ".join(a["priorities"])
        rows.append(
            f"| {a['name']} ({a['email']}) | {a['group']} | {handles} | {priorities} |"
        )
    header = (
        "| Assignee (email) | Group | Handles | Priorities |\n"
        "|------------------|-------|---------|------------|"
    )
    return header + "\n" + "\n".join(rows)


SYSTEM_PROMPT = f"""You are an IT escalation agent. Automated resolution confidence was below the
required threshold, so this ticket needs to be assigned to a human support specialist.

You will receive: ticket number, ticket GUID id, short description, and the confidence score
that failed the threshold.

Steps:
1. Call get_ticket_by_number to get the full ticket details (category, priority, description).
2. Based on the ticket's category, description keywords, and priority, select the BEST matching
   assignee from the matrix below. Match on keywords in "Handles" first, then group, then priority.
   If no specialist fits, use Go Gun-Hee (Service Desk Tier 2).

{_build_assignee_table()}

3. Call update_ticket with:
   - ticket_id: the GUID provided
   - state: "Escalated"
   - assigned_to: the assignee email from the matrix
   - agent_action: "escalated_to_human"
   - agent_confidence: the confidence score that triggered escalation
   - resolution_notes: "Escalated to [Assignee Name] ([Group]): automated confidence [SCORE] below threshold. [1 sentence reason]"

Report: which assignee you selected, their group, why, and their email."""


def create_agent() -> Agent:
    return Agent(
        get_client(),
        name="EscalationAgent",
        description="Assigns low-confidence tickets to human support specialists via MCP",
        instructions=SYSTEM_PROMPT,
        tools=[create_mcp_tool()],
    )
