using AgenticResolution.Api.Data;
using AgenticResolution.Api.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace AgenticResolution.Api.Api;

public record HealthResponse(
    string Status,
    DateTime Timestamp,
    DatabaseHealth Database);

public record DatabaseHealth(
    string Status,
    Dictionary<string, int> TicketCounts,
    int TotalTickets,
    int TotalKbArticles);

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", GetHealthAsync).WithTags("Health");
        return app;
    }

    private static async Task<Ok<HealthResponse>> GetHealthAsync(
        AppDbContext db,
        CancellationToken ct)
    {
        try
        {
            var ticketCounts = await db.Tickets
                .GroupBy(t => t.State)
                .Select(g => new { State = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            var totalKbArticles = await db.KnowledgeArticles.CountAsync(ct);

            var countsDict = new Dictionary<string, int>
            {
                ["new"] = ticketCounts.FirstOrDefault(x => x.State == TicketState.New)?.Count ?? 0,
                ["inProgress"] = ticketCounts.FirstOrDefault(x => x.State == TicketState.InProgress)?.Count ?? 0,
                ["onHold"] = ticketCounts.FirstOrDefault(x => x.State == TicketState.OnHold)?.Count ?? 0,
                ["resolved"] = ticketCounts.FirstOrDefault(x => x.State == TicketState.Resolved)?.Count ?? 0,
                ["closed"] = ticketCounts.FirstOrDefault(x => x.State == TicketState.Closed)?.Count ?? 0,
                ["cancelled"] = ticketCounts.FirstOrDefault(x => x.State == TicketState.Cancelled)?.Count ?? 0,
                ["escalated"] = ticketCounts.FirstOrDefault(x => x.State == TicketState.Escalated)?.Count ?? 0
            };

            int totalTickets = countsDict.Values.Sum();

            var dbHealth = new DatabaseHealth(
                Status: "Connected",
                TicketCounts: countsDict,
                TotalTickets: totalTickets,
                TotalKbArticles: totalKbArticles);

            var response = new HealthResponse(
                Status: "Healthy",
                Timestamp: DateTime.UtcNow,
                Database: dbHealth);

            return TypedResults.Ok(response);
        }
        catch (Exception)
        {
            var dbHealth = new DatabaseHealth(
                Status: "Disconnected",
                TicketCounts: new Dictionary<string, int>(),
                TotalTickets: 0,
                TotalKbArticles: 0);

            var response = new HealthResponse(
                Status: "Unhealthy",
                Timestamp: DateTime.UtcNow,
                Database: dbHealth);

            return TypedResults.Ok(response);
        }
    }
}
