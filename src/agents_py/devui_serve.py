import sys
import os

# Fix Python PATH on Windows before importing agent_framework packages
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from agent_framework_devui import serve

from agents.classifier import agent as classifier_agent
from agents.incident import agent as incident_agent
from agents.request import agent as request_agent
from agents.resolution import agent as resolution_agent
from agents.escalation import agent as escalation_agent
from workflow import workflow as ticket_workflow

if __name__ == "__main__":
    serve(
        entities=[
            classifier_agent,
            incident_agent,
            request_agent,
            resolution_agent,
            escalation_agent,
            ticket_workflow,
        ],
        port=8080,
        auto_open=True,
    )
