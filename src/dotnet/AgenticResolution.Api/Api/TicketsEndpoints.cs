using AgenticResolution.Api.Data;
using AgenticResolution.Api.Models;
using AgenticResolution.Api.Webhooks;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace AgenticResolution.Api.Api;

public record CreateTicketRequest(
    [property: Required, StringLength(200, MinimumLength = 1)] string ShortDescription,
    string? Description,
    [property: Required, StringLength(100, MinimumLength = 1)] string Category,
    TicketPriority Priority,
    [property: Required, StringLength(100, MinimumLength = 1)] string Caller,
    string? AssignedTo);

public record UpdateTicketRequest(
    [property: Required] TicketState State,
    string? ResolutionNotes,
    string? AssignedTo,
    string? AgentAction,
    double? AgentConfidence,
    string? MatchedTicketNumber);

public record TicketResponse(Guid Id, string Number, string ShortDescription, string? Description,
    string Category, TicketPriority Priority, TicketState State, string? AssignedTo, string Caller,
    string? ResolutionNotes, string? AgentAction, double? AgentConfidence, string? MatchedTicketNumber,
    DateTime CreatedAt, DateTime UpdatedAt)
{
    public static TicketResponse From(Ticket t) =>
        new(t.Id, t.Number, t.ShortDescription, t.Description, t.Category, t.Priority,
            t.State, t.AssignedTo, t.Caller, t.ResolutionNotes, t.AgentAction,
            t.AgentConfidence, t.MatchedTicketNumber, t.CreatedAt, t.UpdatedAt);
}

public record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

public static class TicketsEndpoints
{
    public static IEndpointRouteBuilder MapTicketsApi(this IEndpointRouteBuilder app)
    {
        var endpoints = app.MapGroup("/api/tickets").WithTags("Tickets");
        endpoints.MapPost("/", CreateAsync).AddEndpointFilter<ValidationFilter<CreateTicketRequest>>();
        endpoints.MapGet("/search", SearchAsync);
        endpoints.MapGet("/{number}", GetByNumberAsync);
        endpoints.MapGet("/", ListAsync);
        endpoints.MapPut("/{id:guid}", UpdateAsync).AddEndpointFilter<ValidationFilter<UpdateTicketRequest>>();
        return app;
    }

    private static async Task<Results<Created<TicketResponse>, ValidationProblem>> CreateAsync(
        CreateTicketRequest req, AppDbContext db, ITicketNumberGenerator numbers,
        IWebhookDispatcher dispatcher, CancellationToken ct)
    {
        string number = await numbers.NextAsync(ct);
        var now = DateTime.UtcNow;
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Number = number,
            ShortDescription = req.ShortDescription.Trim(),
            Description = req.Description,
            Category = req.Category.Trim(),
            Priority = req.Priority == 0 ? TicketPriority.Moderate : req.Priority,
            State = TicketState.New,
            AssignedTo = req.AssignedTo,
            Caller = req.Caller.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync(ct);
        dispatcher.Enqueue(WebhookPayload.ForTicketCreated(ticket));
        return TypedResults.Created("/api/tickets/" + ticket.Number, TicketResponse.From(ticket));
    }

    private static async Task<Results<Ok<TicketResponse>, NotFound>> UpdateAsync(
        Guid id, UpdateTicketRequest req, AppDbContext db, IWebhookDispatcher dispatcher, CancellationToken ct)
    {
        var ticket = await db.Tickets.FindAsync([id], ct);
        if (ticket is null) return TypedResults.NotFound();

        ticket.State = req.State;
        if (req.ResolutionNotes != null) ticket.ResolutionNotes = req.ResolutionNotes;
        if (req.AssignedTo != null) ticket.AssignedTo = req.AssignedTo;
        if (req.AgentAction != null) ticket.AgentAction = req.AgentAction;
        if (req.AgentConfidence.HasValue) ticket.AgentConfidence = req.AgentConfidence;
        if (req.MatchedTicketNumber != null) ticket.MatchedTicketNumber = req.MatchedTicketNumber;
        ticket.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        dispatcher.Enqueue(WebhookPayload.ForTicketUpdated(ticket));
        return TypedResults.Ok(TicketResponse.From(ticket));
    }

    private static async Task<Results<Ok<TicketResponse>, NotFound>> GetByNumberAsync(
        string number, AppDbContext db, CancellationToken ct)
    {
        var ticket = await db.Tickets.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Number == number, ct);
        return ticket is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(TicketResponse.From(ticket));
    }

    private static async Task<Ok<PagedResponse<TicketResponse>>> ListAsync(
        AppDbContext db, TicketState? state, int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        IQueryable<Ticket> q = db.Tickets.AsNoTracking();
        if (state.HasValue) q = q.Where(t => (int)t.State == (int)state.Value);
        int total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(t => TicketResponse.From(t)).ToListAsync(ct);
        return TypedResults.Ok(new PagedResponse<TicketResponse>(items, page, pageSize, total));
    }

    private static async Task<Ok<PagedResponse<TicketResponse>>> SearchAsync(
        AppDbContext db, string q, TicketState? state, int page = 1, int pageSize = 25,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);
        IQueryable<Ticket> query = db.Tickets.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            string term = q.Trim().ToLower();
            query = query.Where(t =>
                t.ShortDescription.ToLower().Contains(term) ||
                (t.Description != null && t.Description.ToLower().Contains(term)));
        }
        if (state.HasValue) query = query.Where(t => (int)t.State == (int)state.Value);
        int total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(t => TicketResponse.From(t)).ToListAsync(ct);
        return TypedResults.Ok(new PagedResponse<TicketResponse>(items, page, pageSize, total));
    }
}

public class ValidationFilter<T> : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var val = ctx.Arguments.OfType<T>().FirstOrDefault();
        if (val is null) return TypedResults.BadRequest();

        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(val, new ValidationContext(val), results, validateAllProperties: true))
        {
            var errors = results
                .SelectMany(r => (r.MemberNames.Any() ? r.MemberNames : [""]).Select(m => (m, r.ErrorMessage ?? "Invalid")))
                .GroupBy(x => x.m)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Item2).ToArray());
            return TypedResults.ValidationProblem(errors);
        }
        return await next(ctx);
    }
}
