using System.ComponentModel.DataAnnotations;

namespace AgenticResolution.Api.Models;

public enum WorkflowRunStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Escalated = 4
}

public class WorkflowRun
{
    public Guid Id { get; set; }
    public Guid TicketId { get; set; }

    public WorkflowRunStatus Status { get; set; }

    [MaxLength(100)]
    public string? TriggeredBy { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    [MaxLength(100)]
    public string? FinalAction { get; set; }

    public double? FinalConfidence { get; set; }

    public Ticket? Ticket { get; set; }
    public List<WorkflowRunEvent> Events { get; set; } = [];
}
