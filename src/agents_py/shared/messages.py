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
class ResolutionProposal:
    """Output from Incident/Request resolver — carries the proposed fix + confidence."""
    ticket_number: str
    ticket_id: str
    short_description: str
    resolution_text: str
    confidence: float
    ticket_type: str          # "incident" or "request"
    kb_source: Optional[str] = None   # KB article title that matched


@dataclass
class EscalationRoute:
    ticket_number: str
    ticket_id: str
    reason: str
    confidence: float
    assigned_group: Optional[str] = None
    assigned_to: Optional[str] = None
