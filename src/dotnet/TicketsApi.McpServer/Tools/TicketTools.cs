using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using TicketsApi.McpServer.Models;
using TicketsApi.McpServer.Services;

namespace TicketsApi.McpServer.Tools;

[McpServerToolType]
public sealed class TicketTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static string Serialize(object value) =>
        JsonSerializer.Serialize(value, JsonOptions);

    // -------------------------------------------------------------------------
    // get_ticket_by_number
    // -------------------------------------------------------------------------
    [McpServerTool(Name = "get_ticket_by_number")]
    [Description(
        "Retrieves the full details of a single IT support ticket by its ticket number " +
        "(e.g. INC0010001). Use this when you have a specific ticket number and need its " +
        "current state, description, priority, caller, resolution notes, or agent action. " +
        "Returns a JSON object with all ticket fields, or an error message if not found.")]
    public static async Task<string> GetTicketByNumberAsync(
        [Description("The ticket number to look up, e.g. INC0010001. Must start with 'INC' followed by digits.")]
        string ticket_number,
        ITicketApiClient api,
        ILogger<TicketTools> logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ticket_number))
            return "Error: ticket_number is required.";

        try
        {
            var ticket = await api.GetByNumberAsync(ticket_number.Trim(), cancellationToken);
            return ticket is null
                ? $"Error: Ticket '{ticket_number}' not found."
                : Serialize(ticket);
        }
        catch (TicketApiException ex)
        {
            logger.LogError(ex, "Failed to get ticket {Number}", ticket_number);
            return $"Error: {ex.Message}";
        }
    }

    // -------------------------------------------------------------------------
    // list_tickets
    // -------------------------------------------------------------------------
    [McpServerTool(Name = "list_tickets")]
    [Description(
        "Lists IT support tickets, optionally filtered by state. Returns a paged result with items, " +
        "total count, and pagination info. " +
        "Valid states: New, InProgress, OnHold, Resolved, Closed, Cancelled, Escalated. " +
        "Use this to find tickets needing triage, or to get a summary of open work.")]
    public static async Task<string> ListTicketsAsync(
        ITicketApiClient api,
        ILogger<TicketTools> logger,
        [Description("Filter by ticket state. One of: New, InProgress, OnHold, Resolved, Closed, Cancelled, Escalated. Omit for all states.")]
        string? state = null,
        [Description("Page number (1-based). Default: 1.")]
        int page = 1,
        [Description("Results per page (1–100). Default: 25.")]
        int page_size = 25,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page == 0 ? 1 : page);
        page_size = page_size == 0 ? 25 : Math.Clamp(page_size, 1, 100);

        try
        {
            var result = await api.ListAsync(state, page, page_size, cancellationToken);
            return Serialize(new
            {
                result.Items,
                result.Page,
                result.PageSize,
                result.Total,
                result.TotalPages
            });
        }
        catch (TicketApiException ex)
        {
            logger.LogError(ex, "Failed to list tickets");
            return $"Error: {ex.Message}";
        }
    }

    // -------------------------------------------------------------------------
    // search_tickets
    // -------------------------------------------------------------------------
    [McpServerTool(Name = "search_tickets")]
    [Description(
        "Searches tickets by keyword against short description and description text. " +
        "Use when you have a symptom or phrase but not a ticket number. " +
        "Returns matching tickets sorted by most recent first.")]
    public static async Task<string> SearchTicketsAsync(
        [Description("Free-text search query, e.g. 'VPN not connecting' or 'password expired'.")]
        string query,
        ITicketApiClient api,
        ILogger<TicketTools> logger,
        [Description("Optional state filter to narrow results. One of: New, InProgress, OnHold, Resolved, Closed, Cancelled, Escalated.")]
        string? state = null,
        [Description("Max results to return (1–50). Default: 10.")]
        int page_size = 10,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "Error: query is required.";

        page_size = page_size == 0 ? 10 : Math.Clamp(page_size, 1, 50);

        try
        {
            var result = await api.SearchAsync(query.Trim(), state, page_size, cancellationToken);
            return Serialize(new { result.Items, result.Total });
        }
        catch (TicketApiException ex)
        {
            logger.LogError(ex, "Failed to search tickets");
            return $"Error: {ex.Message}";
        }
    }

    // -------------------------------------------------------------------------
    // update_ticket
    // -------------------------------------------------------------------------
    [McpServerTool(Name = "update_ticket")]
    [Description(
        "Updates the state and resolution details of a ticket. Use this after completing analysis " +
        "or remediation of an issue. The agent_action field should describe what action was taken " +
        "(e.g. 'password_reset_guided', 'escalated_to_level2'). " +
        "agent_confidence is a float from 0.0 to 1.0 indicating how certain the agent is in its resolution. " +
        "IMPORTANT: ticket_id must be the 'id' GUID field from the ticket JSON (e.g. '3be5a8cf-2b59-48b6-bf7f-86611869cf8c'), " +
        "NOT the ticket number like 'INC0010022'. Call get_ticket_by_number first and use the 'id' field.")]
    public static async Task<string> UpdateTicketAsync(
        [Description("The ticket's unique GUID id — use the 'id' field from get_ticket_by_number output (format: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx). Do NOT use the ticket number like INC0010001.")]
        string ticket_id,
        [Description("New state. One of: New, InProgress, OnHold, Resolved, Closed, Cancelled, Escalated.")]
        string state,
        ITicketApiClient api,
        ILogger<TicketTools> logger,
        [Description("Human-readable notes explaining what was done or recommended.")]
        string? resolution_notes = null,
        [Description("Assignee username or display name, if changing assignment.")]
        string? assigned_to = null,
        [Description("Short action code describing what the agent did, e.g. 'password_reset_guided'.")]
        string? agent_action = null,
        [Description("Confidence score 0.0–1.0. Use 1.0 only for definitive resolutions.")]
        double? agent_confidence = null,
        [Description("Ticket number of the historical match used for resolution, e.g. INC0009234.")]
        string? matched_ticket_number = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ticket_id))
            return "Error: ticket_id is required.";
        if (string.IsNullOrWhiteSpace(state))
            return "Error: state is required.";
        if (!Guid.TryParse(ticket_id, out var id))
        {
            logger.LogWarning("update_ticket: invalid GUID '{TicketId}'", ticket_id);
            return $"Error: '{ticket_id}' is not a valid ticket ID (expected GUID format).";
        }

        logger.LogInformation("update_ticket: id={Id} state={State} action={Action} confidence={Confidence}",
            id, state, agent_action, agent_confidence);

        var request = new UpdateTicketRequest
        {
            State = state,
            ResolutionNotes = resolution_notes,
            AssignedTo = assigned_to,
            AgentAction = agent_action,
            AgentConfidence = agent_confidence,
            MatchedTicketNumber = matched_ticket_number
        };

        try
        {
            var ticket = await api.UpdateAsync(id, request, cancellationToken);
            if (ticket is null)
            {
                logger.LogWarning("update_ticket: ticket {Id} not found (404)", id);
                return $"Error: Ticket with id '{ticket_id}' not found.";
            }
            logger.LogInformation("update_ticket: ticket {Id} updated successfully. State={State}", id, ticket.State);
            return Serialize(ticket);
        }
        catch (TicketApiException ex)
        {
            logger.LogError(ex, "update_ticket: API error for ticket {Id}: {Message}", id, ex.Message);
            return $"Error: {ex.Message}";
        }
    }
}
