using AgenticResolution.Api.Models;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace AgenticResolution.Api.Models;

public enum TicketPriority
{
    Critical = 1,
    High,
    Moderate,
    Low
}

public enum TicketState
{
    New = 0,
    InProgress = 1,
    OnHold = 2,
    Resolved = 3,
    Closed = 4,
    Cancelled = 5,
    Escalated = 6
}

public class Ticket
{
    public Guid Id { get; set; }

    [MaxLength(15)]
    public string Number { get; set; } = string.Empty;

    [MaxLength(200)]
    public string ShortDescription { get; set; } = string.Empty;

    public string? Description { get; set; }

    [MaxLength(100)]
    public string Category { get; set; } = string.Empty;

    public TicketPriority Priority { get; set; } = TicketPriority.Moderate;

    public TicketState State { get; set; }

    [MaxLength(100)]
    public string? AssignedTo { get; set; }

    [MaxLength(100)]
    public string Caller { get; set; } = string.Empty;

    public string? ResolutionNotes { get; set; }

    [MaxLength(100)]
    public string? AgentAction { get; set; }

    public double? AgentConfidence { get; set; }

    [MaxLength(20)]
    public string? MatchedTicketNumber { get; set; }

    [MaxLength(20)]
    public string? Classification { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

public class TicketNumberSequence
{
    public int Id { get; set; }
    public long LastValue { get; set; }
}
