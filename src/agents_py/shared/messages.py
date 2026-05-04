from dataclasses import dataclass
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
class EscalationRoute:
    ticket_number: str
    ticket_id: str
    reason: str
    confidence: float
    assigned_group: Optional[str] = None
    assigned_to: Optional[str] = None
