using AgenticResolution.Api.Data;
using AgenticResolution.Api.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace AgenticResolution.Api.Api;

public record ResetDataRequest(bool ResetTickets = true, bool SeedSampleTickets = false);
public record ResetDataResponse(int TicketsReset, int TicketsSeeded, string Message);

public record CreateKbArticleRequest(
    string Title,
    string Body,
    string Category,
    string Author,
    string? Tags = null);

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminApi(this IEndpointRouteBuilder app)
    {
        var endpoints = app.MapGroup("/api/admin").WithTags("Admin");
        endpoints.MapPost("/reset-data", ResetDataAsync);
        endpoints.MapPost("/kb", CreateKbArticleAsync);
        endpoints.MapGet("/health", HealthAsync);
        return app;
    }

    private static async Task<Ok<ResetDataResponse>> ResetDataAsync(
        ResetDataRequest? request,
        AppDbContext db,
        CancellationToken ct)
    {
        request ??= new ResetDataRequest();
        int ticketsReset = 0;
        int ticketsSeeded = 0;

        if (request.ResetTickets)
        {
            // Reset all existing tickets to New status and unassigned
            ticketsReset = await db.Tickets
                .ExecuteUpdateAsync(t => t
                    .SetProperty(x => x.State, TicketState.New)
                    .SetProperty(x => x.AssignedTo, (string?)null)
                    .SetProperty(x => x.ResolutionNotes, (string?)null)
                    .SetProperty(x => x.AgentAction, (string?)null)
                    .SetProperty(x => x.AgentConfidence, (double?)null)
                    .SetProperty(x => x.MatchedTicketNumber, (string?)null)
                    .SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct);

            // Reset ticket number sequence to baseline
            var sequence = await db.TicketNumberSequences.FirstOrDefaultAsync(s => s.Id == 1, ct);
            if (sequence != null)
            {
                sequence.LastValue = 10000;
                await db.SaveChangesAsync(ct);
            }
        }

        if (request.SeedSampleTickets)
        {
            // Delete all existing tickets for a clean re-seed
            await db.Tickets.ExecuteDeleteAsync(ct);

            var sampleTickets = GetSampleTickets();
            db.Tickets.AddRange(sampleTickets);
            await db.SaveChangesAsync(ct);
            ticketsSeeded = sampleTickets.Length;

            // Reset sequence to reflect seeded tickets
            var sequence = await db.TicketNumberSequences.FirstOrDefaultAsync(s => s.Id == 1, ct);
            if (sequence != null)
            {
                sequence.LastValue = 10000 + ticketsSeeded;
                await db.SaveChangesAsync(ct);
            }
        }

        string message = ticketsReset > 0
            ? $"Reset {ticketsReset} ticket(s) to New/unassigned state."
            : request.SeedSampleTickets
                ? "Cleared existing tickets for re-seed."
                : "No tickets to reset.";
        
        if (ticketsSeeded > 0)
            message += $" Seeded {ticketsSeeded} sample ticket(s).";

        return TypedResults.Ok(new ResetDataResponse(ticketsReset, ticketsSeeded, message));
    }

    private static async Task<IResult> HealthAsync(AppDbContext db, CancellationToken ct)
    {
        bool canConnect = await db.Database.CanConnectAsync(ct);
        return TypedResults.Ok(new
        {
            Status = canConnect ? "Healthy" : "Unhealthy",
            Database = canConnect ? "Connected" : "Disconnected",
            Timestamp = DateTime.UtcNow
        });
    }

    private static async Task<Created<KnowledgeArticleDetailResponse>> CreateKbArticleAsync(
        CreateKbArticleRequest req,
        AppDbContext db,
        CancellationToken ct)
    {
        var lastArticle = await db.KnowledgeArticles
            .OrderByDescending(a => a.Number)
            .FirstOrDefaultAsync(ct);

        int nextNumber = 1001;
        if (lastArticle != null && lastArticle.Number.StartsWith("KB"))
        {
            if (int.TryParse(lastArticle.Number.Substring(2), out int lastNum))
            {
                nextNumber = lastNum + 1;
            }
        }

        var now = DateTime.UtcNow;
        var article = new KnowledgeArticle
        {
            Id = Guid.NewGuid(),
            Number = $"KB{nextNumber:D7}",
            Title = req.Title.Trim(),
            Body = req.Body.Trim(),
            Category = req.Category.Trim(),
            Author = req.Author.Trim(),
            Tags = req.Tags?.Trim(),
            ViewCount = 0,
            IsPublished = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.KnowledgeArticles.Add(article);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/kb/{article.Number}", KnowledgeArticleDetailResponse.From(article));
    }

    private static Ticket[] GetSampleTickets()
    {
        var now = DateTime.UtcNow;
        return
        [
            new Ticket
            {
                Id = Guid.NewGuid(),
                Number = "INC0010001",
                ShortDescription = "Unable to access email on mobile device",
                Description = "User reports they cannot sync their corporate email on their iPhone. They receive an authentication error when adding the account.",
                Category = "Email",
                Priority = TicketPriority.High,
                State = TicketState.New,
                AssignedTo = null,
                Caller = "john.smith@company.com",
                CreatedAt = now.AddHours(-8),
                UpdatedAt = now.AddHours(-8)
            },
            new Ticket
            {
                Id = Guid.NewGuid(),
                Number = "INC0010002",
                ShortDescription = "Printer not responding in conference room B",
                Description = "The network printer in conference room B is not responding. Print jobs are stuck in queue. Multiple users affected.",
                Category = "Hardware",
                Priority = TicketPriority.Moderate,
                State = TicketState.New,
                AssignedTo = null,
                Caller = "sarah.jones@company.com",
                CreatedAt = now.AddHours(-6),
                UpdatedAt = now.AddHours(-6)
            },
            new Ticket
            {
                Id = Guid.NewGuid(),
                Number = "INC0010003",
                ShortDescription = "VPN connection drops intermittently",
                Description = "VPN connection to corporate network drops every 10-15 minutes when working from home. Using GlobalProtect client version 5.2.9.",
                Category = "Network",
                Priority = TicketPriority.High,
                State = TicketState.New,
                AssignedTo = null,
                Caller = "mike.wilson@company.com",
                CreatedAt = now.AddHours(-5),
                UpdatedAt = now.AddHours(-5)
            },
            new Ticket
            {
                Id = Guid.NewGuid(),
                Number = "INC0010004",
                ShortDescription = "Request editor access to Engineering SharePoint site",
                Description = "Need editor access to the Engineering SharePoint site for the Q2 project documentation. Manager has verbally approved.",
                Category = "Software",
                Priority = TicketPriority.Low,
                State = TicketState.New,
                AssignedTo = null,
                Caller = "lisa.chen@company.com",
                CreatedAt = now.AddHours(-4),
                UpdatedAt = now.AddHours(-4)
            },
            new Ticket
            {
                Id = Guid.NewGuid(),
                Number = "INC0010005",
                ShortDescription = "Laptop running very slow after Windows update",
                Description = "Laptop has been running extremely slow after last week's Windows update. Takes 5+ minutes to start up and applications frequently freeze.",
                Category = "Hardware",
                Priority = TicketPriority.Moderate,
                State = TicketState.New,
                AssignedTo = null,
                Caller = "robert.taylor@company.com",
                CreatedAt = now.AddHours(-3),
                UpdatedAt = now.AddHours(-3)
            },
            new Ticket
            {
                Id = Guid.NewGuid(),
                Number = "INC0010006",
                ShortDescription = "Password expired - cannot log in to workstation",
                Description = "Password has expired and the user is locked out of their workstation. Self-service reset portal also requires MFA which they cannot access.",
                Category = "Account Management",
                Priority = TicketPriority.Critical,
                State = TicketState.New,
                AssignedTo = null,
                Caller = "amanda.garcia@company.com",
                CreatedAt = now.AddHours(-2),
                UpdatedAt = now.AddHours(-2)
            },
            new Ticket
            {
                Id = Guid.NewGuid(),
                Number = "INC0010007",
                ShortDescription = "Need help setting up MFA on new phone",
                Description = "User got a new phone and needs help re-registering for multi-factor authentication. Old phone is no longer available.",
                Category = "Security",
                Priority = TicketPriority.High,
                State = TicketState.New,
                AssignedTo = null,
                Caller = "david.kim@company.com",
                CreatedAt = now.AddHours(-2),
                UpdatedAt = now.AddHours(-2)
            },
            new Ticket
            {
                Id = Guid.NewGuid(),
                Number = "INC0010008",
                ShortDescription = "Outlook keeps showing Disconnected status",
                Description = "Outlook shows 'Disconnected' in the status bar all day. Restarting Outlook doesn't help. Internet connection is working fine.",
                Category = "Email",
                Priority = TicketPriority.High,
                State = TicketState.New,
                AssignedTo = null,
                Caller = "emily.nguyen@company.com",
                CreatedAt = now.AddMinutes(-90),
                UpdatedAt = now.AddMinutes(-90)
            },
            new Ticket
            {
                Id = Guid.NewGuid(),
                Number = "INC0010009",
                ShortDescription = "OneDrive not syncing - files stuck",
                Description = "OneDrive has not synced for 3 days. The sync icon shows a red X on several files. No error message is shown when clicking the icon.",
                Category = "Cloud Storage",
                Priority = TicketPriority.Moderate,
                State = TicketState.New,
                AssignedTo = null,
                Caller = "james.patel@company.com",
                CreatedAt = now.AddMinutes(-75),
                UpdatedAt = now.AddMinutes(-75)
            },
            new Ticket
            {
                Id = Guid.NewGuid(),
                Number = "INC0010010",
                ShortDescription = "Request installation of Adobe Acrobat Pro",
                Description = "Need Adobe Acrobat Pro installed for creating and editing PDF contracts. This is a standard approved application.",
                Category = "Software",
                Priority = TicketPriority.Low,
                State = TicketState.New,
                AssignedTo = null,
                Caller = "rachel.brown@company.com",
                CreatedAt = now.AddMinutes(-60),
                UpdatedAt = now.AddMinutes(-60)
            },
            new Ticket
            {
                Id = Guid.NewGuid(),
                Number = "INC0010011",
                ShortDescription = "Cannot connect to VPN from hotel network",
                Description = "Unable to connect to GlobalProtect VPN while traveling. The connection attempt times out after 30 seconds. Works fine at home and office.",
                Category = "Network",
                Priority = TicketPriority.High,
                State = TicketState.New,
                AssignedTo = null,
                Caller = "carlos.mendez@company.com",
                CreatedAt = now.AddMinutes(-50),
                UpdatedAt = now.AddMinutes(-50)
            },
            new Ticket
            {
                Id = Guid.NewGuid(),
                Number = "INC0010012",
                ShortDescription = "New hire needs corporate email account created",
                Description = "New employee Jennifer Walsh starting Monday needs corporate email, Teams, and SharePoint access provisioned. Department: Marketing. Manager: tom.harris@company.com.",
                Category = "Account Management",
                Priority = TicketPriority.Moderate,
                State = TicketState.New,
                AssignedTo = null,
                Caller = "tom.harris@company.com",
                CreatedAt = now.AddMinutes(-40),
                UpdatedAt = now.AddMinutes(-40)
            },
            new Ticket
            {
                Id = Guid.NewGuid(),
                Number = "INC0010013",
                ShortDescription = "External monitor not detected by laptop",
                Description = "When connecting the Dell 27\" monitor via USB-C dock, the laptop does not detect the external display. Tried rebooting. Display settings only shows one screen.",
                Category = "Hardware",
                Priority = TicketPriority.Moderate,
                State = TicketState.New,
                AssignedTo = null,
                Caller = "stephanie.lee@company.com",
                CreatedAt = now.AddMinutes(-30),
                UpdatedAt = now.AddMinutes(-30)
            },
            new Ticket
            {
                Id = Guid.NewGuid(),
                Number = "INC0010014",
                ShortDescription = "Teams video calls keep freezing and dropping",
                Description = "Microsoft Teams video calls freeze every few minutes and sometimes drop entirely. Audio continues but video stops. Issue started after the last Teams update.",
                Category = "Software",
                Priority = TicketPriority.High,
                State = TicketState.New,
                AssignedTo = null,
                Caller = "brian.walker@company.com",
                CreatedAt = now.AddMinutes(-20),
                UpdatedAt = now.AddMinutes(-20)
            },
            new Ticket
            {
                Id = Guid.NewGuid(),
                Number = "INC0010015",
                ShortDescription = "Forgot password and need immediate reset",
                Description = "User has forgotten their password and cannot log in to any corporate systems. They have an important presentation in 2 hours and need urgent help.",
                Category = "Account Management",
                Priority = TicketPriority.Critical,
                State = TicketState.New,
                AssignedTo = null,
                Caller = "olivia.martinez@company.com",
                CreatedAt = now.AddMinutes(-10),
                UpdatedAt = now.AddMinutes(-10)
            }
        ];
    }
}
