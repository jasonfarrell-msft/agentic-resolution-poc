using AgenticResolution.Api.Agents;
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

public record CommentResponse(Guid Id, Guid TicketId, string Author, string Body, bool IsInternal, DateTime CreatedAt)
{
    public static CommentResponse From(TicketComment c) =>
        new(c.Id, c.TicketId, c.Author, c.Body, c.IsInternal, c.CreatedAt);
}

public record CreateCommentRequest(
    [property: Required, StringLength(100, MinimumLength = 1)] string Author,
    [property: Required, StringLength(4000, MinimumLength = 1)] string Body,
    bool IsInternal = false);

public record WorkflowRunResponse(Guid Id, Guid TicketId, WorkflowRunStatus Status, string? TriggeredBy,
    string? Note, DateTime StartedAt, DateTime? CompletedAt, string? FinalAction, double? FinalConfidence)
{
    public static WorkflowRunResponse From(WorkflowRun r) =>
        new(r.Id, r.TicketId, r.Status, r.TriggeredBy, r.Note, r.StartedAt, r.CompletedAt, r.FinalAction, r.FinalConfidence);
}

public record WorkflowRunEventResponse(Guid Id, Guid RunId, int Sequence, string? ExecutorId, string EventType, string? Payload, DateTime Timestamp)
{
    public static WorkflowRunEventResponse From(WorkflowRunEvent e) =>
        new(e.Id, e.RunId, e.Sequence, e.ExecutorId, e.EventType, e.Payload, e.Timestamp);
}

public record WorkflowRunDetailResponse(WorkflowRunResponse Run, IReadOnlyList<WorkflowRunEventResponse> Events);

public record TicketDetailResponse(TicketResponse Ticket, IReadOnlyList<CommentResponse> Comments, IReadOnlyList<WorkflowRunResponse> Runs);

public record StartResolveRequest(string? Note);

public record StartResolveResponse(Guid RunId, string TicketNumber, Guid TicketId, string StatusUrl, string EventsUrl);

public static class TicketsEndpoints
{
    public static IEndpointRouteBuilder MapTicketsApi(this IEndpointRouteBuilder app)
    {
        var endpoints = app.MapGroup("/api/tickets").WithTags("Tickets");
        endpoints.MapPost("/", CreateAsync).AddEndpointFilter<ValidationFilter<CreateTicketRequest>>();
        endpoints.MapGet("/search", SearchAsync);
        endpoints.MapGet("/{number}", GetByNumberAsync);
        endpoints.MapGet("/{number}/details", GetDetailsByNumberAsync);
        endpoints.MapGet("/{number}/comments", GetCommentsAsync);
        endpoints.MapPost("/{number}/comments", CreateCommentAsync).AddEndpointFilter<ValidationFilter<CreateCommentRequest>>();
        endpoints.MapGet("/{number}/runs", GetTicketRunsAsync);
        endpoints.MapPost("/{number}/resolve", StartResolveAsync);
        endpoints.MapGet("/", ListAsync);
        endpoints.MapPut("/{id:guid}", UpdateAsync).AddEndpointFilter<ValidationFilter<UpdateTicketRequest>>();

        var runs = app.MapGroup("/api/runs").WithTags("Runs");
        runs.MapGet("/{runId:guid}", GetRunAsync);
        runs.MapGet("/{runId:guid}/events", GetRunEventsAsync);

        return app;
    }

    private static async Task<Results<Created<TicketResponse>, ValidationProblem>> CreateAsync(
        CreateTicketRequest req, AppDbContext db, ITicketNumberGenerator numbers,
        IWebhookDispatcher dispatcher, IConfiguration config, CancellationToken ct)
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

        bool autoDispatch = config.GetValue("Webhook:AutoDispatchOnTicketWrite", false);
        if (autoDispatch)
            dispatcher.Enqueue(WebhookPayload.ForTicketCreated(ticket));

        return TypedResults.Created("/api/tickets/" + ticket.Number, TicketResponse.From(ticket));
    }

    private static async Task<Results<Ok<TicketResponse>, NotFound>> UpdateAsync(
        Guid id, UpdateTicketRequest req, AppDbContext db, IWebhookDispatcher dispatcher,
        IConfiguration config, CancellationToken ct)
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

        bool autoDispatch = config.GetValue("Webhook:AutoDispatchOnTicketWrite", false);
        if (autoDispatch)
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
        AppDbContext db, string? assignedTo, string? state, string? category, string? priority,
        string? q, string sort = "created", string dir = "desc", int page = 1, int pageSize = 25,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        IQueryable<Ticket> query = db.Tickets.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(assignedTo))
        {
            if (assignedTo.Equals("unassigned", StringComparison.OrdinalIgnoreCase))
                query = query.Where(t => t.AssignedTo == null);
            else
                query = query.Where(t => t.AssignedTo == assignedTo);
        }

        if (!string.IsNullOrWhiteSpace(state))
        {
            var states = state.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => Enum.TryParse<TicketState>(s, true, out var ts) ? (TicketState?)ts : null)
                .Where(s => s.HasValue)
                .Select(s => s!.Value)
                .ToList();
            if (states.Count > 0)
                query = query.Where(t => states.Contains(t.State));
        }

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(t => t.Category == category);

        if (!string.IsNullOrWhiteSpace(priority))
        {
            var priorities = priority.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(p => Enum.TryParse<TicketPriority>(p, true, out var tp) ? (TicketPriority?)tp : null)
                .Where(p => p.HasValue)
                .Select(p => p!.Value)
                .ToList();
            if (priorities.Count > 0)
                query = query.Where(t => priorities.Contains(t.Priority));
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            string term = q.Trim().ToLower();
            query = query.Where(t =>
                t.ShortDescription.ToLower().Contains(term) ||
                (t.Description != null && t.Description.ToLower().Contains(term)));
        }

        string sortField = sort.ToLowerInvariant();
        bool descending = dir.Equals("desc", StringComparison.OrdinalIgnoreCase);

        query = sortField switch
        {
            "modified" => descending ? query.OrderByDescending(t => t.UpdatedAt) : query.OrderBy(t => t.UpdatedAt),
            "created" => descending ? query.OrderByDescending(t => t.CreatedAt) : query.OrderBy(t => t.CreatedAt),
            _ => query.OrderByDescending(t => t.CreatedAt)
        };

        int total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
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

    private static async Task<Results<Ok<TicketDetailResponse>, NotFound>> GetDetailsByNumberAsync(
        string number, AppDbContext db, CancellationToken ct)
    {
        var ticket = await db.Tickets.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Number == number, ct);
        if (ticket is null) return TypedResults.NotFound();

        var comments = await db.Comments.AsNoTracking()
            .Where(c => c.TicketId == ticket.Id)
            .OrderBy(c => c.CreatedAt)
            .Select(c => CommentResponse.From(c))
            .ToListAsync(ct);

        var runs = await db.WorkflowRuns.AsNoTracking()
            .Where(r => r.TicketId == ticket.Id)
            .OrderByDescending(r => r.StartedAt)
            .Select(r => WorkflowRunResponse.From(r))
            .ToListAsync(ct);

        return TypedResults.Ok(new TicketDetailResponse(
            TicketResponse.From(ticket), comments, runs));
    }

    private static async Task<Results<Ok<IReadOnlyList<CommentResponse>>, NotFound>> GetCommentsAsync(
        string number, AppDbContext db, CancellationToken ct)
    {
        var ticket = await db.Tickets.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Number == number, ct);
        if (ticket is null) return TypedResults.NotFound();

        var comments = await db.Comments.AsNoTracking()
            .Where(c => c.TicketId == ticket.Id)
            .OrderBy(c => c.CreatedAt)
            .Select(c => CommentResponse.From(c))
            .ToListAsync(ct);

        return TypedResults.Ok<IReadOnlyList<CommentResponse>>(comments);
    }

    private static async Task<Results<Created<CommentResponse>, NotFound, ValidationProblem>> CreateCommentAsync(
        string number, CreateCommentRequest req, AppDbContext db, CancellationToken ct)
    {
        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Number == number, ct);
        if (ticket is null) return TypedResults.NotFound();

        var comment = new TicketComment
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Author = req.Author.Trim(),
            Body = req.Body.Trim(),
            IsInternal = req.IsInternal,
            CreatedAt = DateTime.UtcNow
        };

        db.Comments.Add(comment);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/tickets/{number}/comments", CommentResponse.From(comment));
    }

    private static async Task<Results<Ok<IReadOnlyList<WorkflowRunResponse>>, NotFound>> GetTicketRunsAsync(
        string number, AppDbContext db, CancellationToken ct)
    {
        var ticket = await db.Tickets.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Number == number, ct);
        if (ticket is null) return TypedResults.NotFound();

        var runs = await db.WorkflowRuns.AsNoTracking()
            .Where(r => r.TicketId == ticket.Id)
            .OrderByDescending(r => r.StartedAt)
            .Select(r => WorkflowRunResponse.From(r))
            .ToListAsync(ct);

        return TypedResults.Ok<IReadOnlyList<WorkflowRunResponse>>(runs);
    }

    private static async Task<Results<Accepted<StartResolveResponse>, Ok<StartResolveResponse>, NotFound>> StartResolveAsync(
        string number, StartResolveRequest? req, AppDbContext db,
        IWebhookDispatcher webhookDispatcher, CancellationToken ct)
    {
        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Number == number, ct);
        if (ticket is null) return TypedResults.NotFound();

        var existing = await db.WorkflowRuns
            .Where(r => r.TicketId == ticket.Id &&
                (r.Status == WorkflowRunStatus.Pending || r.Status == WorkflowRunStatus.Running))
            .OrderByDescending(r => r.StartedAt)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            var existingResponse = new StartResolveResponse(
                existing.Id,
                ticket.Number,
                ticket.Id,
                $"/api/runs/{existing.Id}",
                $"/api/runs/{existing.Id}/events");
            return TypedResults.Ok(existingResponse);
        }

        var run = new WorkflowRun
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Status = WorkflowRunStatus.Pending,
            TriggeredBy = "manual",
            Note = req?.Note,
            StartedAt = DateTime.UtcNow
        };

        db.WorkflowRuns.Add(run);
        await db.SaveChangesAsync(ct);

        // Fire webhook unconditionally - receiver handles orchestration
        webhookDispatcher.Enqueue(WebhookPayload.ForResolutionStarted(ticket, run.Id));

        var response = new StartResolveResponse(
            run.Id,
            ticket.Number,
            ticket.Id,
            $"/api/runs/{run.Id}",
            $"/api/runs/{run.Id}/events");

        return TypedResults.Accepted($"/api/runs/{run.Id}", response);
    }

    private static async Task<Results<Ok<WorkflowRunDetailResponse>, NotFound>> GetRunAsync(
        Guid runId, AppDbContext db, CancellationToken ct)
    {
        var run = await db.WorkflowRuns.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null) return TypedResults.NotFound();

        var events = await db.WorkflowRunEvents.AsNoTracking()
            .Where(e => e.RunId == runId)
            .OrderBy(e => e.Sequence)
            .Select(e => WorkflowRunEventResponse.From(e))
            .ToListAsync(ct);

        return TypedResults.Ok(new WorkflowRunDetailResponse(
            WorkflowRunResponse.From(run), events));
    }

    private static async Task<Results<Ok<IReadOnlyList<WorkflowRunEventResponse>>, NotFound>> GetRunEventsAsync(
        Guid runId, AppDbContext db, CancellationToken ct)
    {
        var exists = await db.WorkflowRuns.AsNoTracking()
            .AnyAsync(r => r.Id == runId, ct);
        if (!exists) return TypedResults.NotFound();

        var events = await db.WorkflowRunEvents.AsNoTracking()
            .Where(e => e.RunId == runId)
            .OrderBy(e => e.Sequence)
            .Select(e => WorkflowRunEventResponse.From(e))
            .ToListAsync(ct);

        return TypedResults.Ok<IReadOnlyList<WorkflowRunEventResponse>>(events);
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
