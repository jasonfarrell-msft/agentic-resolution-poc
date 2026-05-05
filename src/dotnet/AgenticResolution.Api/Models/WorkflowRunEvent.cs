using System.ComponentModel.DataAnnotations;

namespace AgenticResolution.Api.Models;

public class WorkflowRunEvent
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }

    public int Sequence { get; set; }

    [MaxLength(100)]
    public string? ExecutorId { get; set; }

    [MaxLength(50)]
    public string EventType { get; set; } = string.Empty;

    public string? Payload { get; set; }

    public DateTime Timestamp { get; set; }

    public WorkflowRun? Run { get; set; }
}
