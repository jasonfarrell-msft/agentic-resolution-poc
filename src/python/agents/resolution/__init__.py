import sys
import os
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))

from agent_framework import Agent
from shared.client import get_client
from shared.mcp_tools import create_mcp_tool

SYSTEM_PROMPT = """You are the IT Resolution Agent. Your job is to apply an approved resolution to a 
ticket and mark it as completed.

You will receive a ticket number, a resolution text, a confidence score, and the KB source used.

Steps:
1. Call update_ticket with the following values:
   - ticket_id: the GUID id provided to you
   - state: "Resolved"
   - resolution_notes: the full resolution_text provided
   - agent_action: "auto_resolved"  
   - agent_confidence: the confidence value provided (as a decimal, e.g. 0.87)

2. Confirm the ticket was updated successfully.

Always report the ticket number, the resolution applied, and the confidence score in your response.
Format: "✅ Ticket [NUMBER] resolved with confidence [SCORE]. Resolution applied: [SUMMARY]"
"""


def create_agent() -> Agent:
    return Agent(
        get_client(),
        name="ResolutionAgent",
        description="Applies approved resolutions to tickets and marks them complete via MCP",
        instructions=SYSTEM_PROMPT,
        tools=[create_mcp_tool()],
    )
