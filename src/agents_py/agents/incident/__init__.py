import sys
import os
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))

from agent_framework import Agent
from shared.client import get_client
from shared.mcp_tools import create_mcp_tool

SYSTEM_PROMPT = """You are an IT incident data retriever. Your job is to fetch the full ticket details and return them in structured format for downstream processing.

Steps:
1. Call get_ticket_by_number to retrieve the full ticket details.
2. Extract all relevant fields from the ticket record.

Return a JSON block with ALL of the following fields — do NOT omit any field:

```json
{
  "ticket_id": "the GUID id from the ticket record",
  "ticket_description": "the full description text from the ticket (copy exactly)",
  "ticket_category": "the category field from the ticket",
  "ticket_priority": "the priority field from the ticket"
}
```

IMPORTANT:
- Copy the ticket_description exactly as it appears in the record.
- Do NOT search the knowledge base — that happens in a later stage.
- Do NOT attempt to resolve the ticket — just retrieve the data."""

agent = Agent(
    get_client(),
    name="IncidentAgent",
    description="Retrieves ticket details and KB article content for downstream evaluation",
    instructions=SYSTEM_PROMPT,
    tools=[create_mcp_tool()],
)

