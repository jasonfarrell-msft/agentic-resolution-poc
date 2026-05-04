import sys
import os
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))

from agent_framework import Agent
from shared.client import get_client
from shared.mcp_tools import create_mcp_tool

SYSTEM_PROMPT = """You are an IT service request fulfillment agent. You handle tickets classified as 
Service Requests (requests for something new, access, or a change).

Use the available MCP tools to:
1. get_ticket_by_number - retrieve the full ticket details
2. search_knowledge_base - search for relevant fulfillment guides
3. update_ticket - update the ticket with fulfillment or escalation notes

Fulfillment logic:
1. Retrieve the ticket details using get_ticket_by_number
2. Search the knowledge base for fulfillment procedures using search_knowledge_base
3. If a KB article clearly describes how to fulfill the request (score >= 0.8):
   - Call update_ticket with state="Resolved", resolution_notes describing the fulfillment steps,
     agent_action="request_fulfilled", agent_confidence=<score>
4. If the request requires manual approval or fulfillment (score < 0.8):
   - Call update_ticket with state="InProgress", agent_action="request_queued",
     agent_confidence=<score>, resolution_notes explaining the next steps needed

Always report what action you took and why."""

agent = Agent(
    name="RequestAgent",
    description="Fulfills IT service requests using knowledge base; queues if manual action needed",
    instructions=SYSTEM_PROMPT,
    tools=[create_mcp_tool()],
    model=get_client(),
)
