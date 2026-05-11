namespace TicketsApi.McpServer.Models;

public class TicketModel
{
    public Guid Id { get; set; }
    public string Number { get; set; } = "";
    public string ShortDescription { get; set; } = "";
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? Priority { get; set; }
    public string State { get; set; } = "";
    public string? AssignedTo { get; set; }
    public string? Caller { get; set; }
    public string? ResolutionNotes { get; set; }
    public string? AgentAction { get; set; }
    public double? AgentConfidence { get; set; }
    public string? MatchedTicketNumber { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
