namespace TicketsApi.McpServer.Models;

public class UpdateTicketRequest
{
    public string State { get; set; } = "";
    public string? ResolutionNotes { get; set; }
    public string? AssignedTo { get; set; }
    public string? AgentAction { get; set; }
    public double? AgentConfidence { get; set; }
    public string? MatchedTicketNumber { get; set; }
}
