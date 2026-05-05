from dataclasses import dataclass, field
from typing import Optional


@dataclass
class TicketInput:
    ticket_number: str


@dataclass
class IncidentRoute:
    ticket_number: str
    ticket_id: str
    short_description: str


@dataclass
class RequestRoute:
    ticket_number: str
    ticket_id: str
    short_description: str


@dataclass
class TicketDetails:
    """Output from Incident/Request agent: fetched ticket data (no KB search yet)."""
    ticket_number: str
    ticket_id: str
    short_description: str
    ticket_description: str
    ticket_category: str
    ticket_priority: str
    ticket_type: str  # "incident" or "request"


@dataclass
class ResolutionQuestion:
    """A specific question that must be answered to resolve the ticket."""
    question: str          # "What causes VPN split tunneling to route cloud traffic incorrectly?"
    search_terms: str      # "VPN split tunneling configuration cloud routing"
    answer: str            # Synthesized answer from KB search results
    kb_sources: list       # ["VPN Not Connecting", "Cloud Access Best Practices"]


@dataclass
class ResolutionAnalysis:
    """Output from DecomposerAgent: problem breakdown + targeted KB retrieval + synthesis."""
    ticket_number: str
    ticket_id: str
    short_description: str
    ticket_description: str
    ticket_category: str
    ticket_priority: str
    ticket_type: str  # "incident" or "request"
    
    # Decomposition
    core_problem: str          # 1-sentence statement of the root issue
    questions: list            # list[ResolutionQuestion] — 2-4 specific questions with answers
    
    # Confidence (preliminary)
    preliminary_confidence: float  # Agent's initial assessment before formal evaluation


@dataclass
class ResolutionProposal:
    """Output from EvaluatorAgent — carries the final resolution + calibrated confidence."""
    ticket_number: str
    ticket_id: str
    short_description: str
    resolution_text: str
    confidence: float
    ticket_type: str          # "incident" or "request"
    kb_source: Optional[str] = None   # KB article title(s) that contributed


@dataclass
class EscalationRoute:
    ticket_number: str
    ticket_id: str
    reason: str
    confidence: float
    assigned_group: Optional[str] = None
    assigned_to: Optional[str] = None
