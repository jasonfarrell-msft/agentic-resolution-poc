using System.Reflection;
using AgenticResolution.Api.Api;
using AgenticResolution.Api.Data;
using AgenticResolution.Api.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AgenticResolution.Api.Tests;

/// <summary>
/// Tests for abandon workflow endpoint.
/// Validates behavior for InProgress tickets (can abandon) vs. other states (validation error).
/// </summary>
public class AbandonWorkflowTests
{
    private AppDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"TestDb_{Guid.NewGuid()}")
            .Options;

        return new AppDbContext(options);
    }

    private static async Task<Results<Ok<TicketResponse>, NotFound, ProblemHttpResult>> CallAbandonWorkflowAsync(
        string number, AppDbContext db, CancellationToken ct)
    {
        var method = typeof(TicketsEndpoints).GetMethod("AbandonWorkflowAsync",
            BindingFlags.NonPublic | BindingFlags.Static);
        
        if (method is null)
            throw new InvalidOperationException("Could not find AbandonWorkflowAsync method");

        var task = method.Invoke(null, new object[] { number, db, ct });
        if (task is null)
            throw new InvalidOperationException("Method invocation returned null");

        var result = await (dynamic)task;
        return result;
    }

    [Fact]
    public async Task AbandonWorkflow_InProgressTicket_ResetsToNew()
    {
        // Arrange
        await using var db = CreateInMemoryContext();
        
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Number = "INC0010018",
            ShortDescription = "Test ticket in progress",
            Description = "Testing abandon workflow",
            Category = "Software",
            Priority = TicketPriority.High,
            State = TicketState.InProgress,
            AssignedTo = "agent@company.com",
            AgentAction = "RouteToTeam",
            AgentConfidence = 0.85,
            Caller = "user@company.com",
            CreatedAt = DateTime.UtcNow.AddHours(-2),
            UpdatedAt = DateTime.UtcNow.AddHours(-1)
        };

        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        var originalUpdateTime = ticket.UpdatedAt;

        // Act
        var result = await CallAbandonWorkflowAsync(ticket.Number, db, CancellationToken.None);

        // Assert - Result is Ok
        var okResult = Assert.IsType<Ok<TicketResponse>>(result.Result);
        Assert.NotNull(okResult.Value);
        Assert.Equal(TicketState.New, okResult.Value.State);
        Assert.Equal(ticket.Number, okResult.Value.Number);

        // Assert - Database state changed
        var updatedTicket = await db.Tickets.FirstOrDefaultAsync(t => t.Number == ticket.Number);
        Assert.NotNull(updatedTicket);
        Assert.Equal(TicketState.New, updatedTicket.State);
        Assert.True(updatedTicket.UpdatedAt >= originalUpdateTime);
    }

    [Fact]
    public async Task AbandonWorkflow_InProgressTicket_CreatesSystemComment()
    {
        // Arrange
        await using var db = CreateInMemoryContext();
        
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Number = "INC0010019",
            ShortDescription = "Another test",
            Category = "Hardware",
            Priority = TicketPriority.Moderate,
            State = TicketState.InProgress,
            Caller = "user@company.com",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        // Act
        await CallAbandonWorkflowAsync(ticket.Number, db, CancellationToken.None);

        // Assert - System comment created
        var comments = await db.Comments
            .Where(c => c.TicketId == ticket.Id)
            .ToListAsync();

        Assert.Single(comments);
        var comment = comments[0];
        Assert.Equal("System", comment.Author);
        Assert.Contains("Workflow abandoned", comment.Body);
        Assert.Contains("reset to New", comment.Body);
        Assert.True(comment.IsInternal);
    }

    [Fact]
    public async Task AbandonWorkflow_NewTicket_Returns400WithValidationMessage()
    {
        // Arrange
        await using var db = CreateInMemoryContext();
        
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Number = "INC0010020",
            ShortDescription = "New ticket",
            Category = "Software",
            Priority = TicketPriority.Low,
            State = TicketState.New,
            Caller = "user@company.com",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        // Act
        var result = await CallAbandonWorkflowAsync(ticket.Number, db, CancellationToken.None);

        // Assert
        var problemResult = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(400, problemResult.StatusCode);
        Assert.Contains("not in InProgress state", problemResult.ProblemDetails.Detail);
        Assert.Contains(ticket.Number, problemResult.ProblemDetails.Detail);
        Assert.Contains("Current state: New", problemResult.ProblemDetails.Detail);
    }

    [Fact]
    public async Task AbandonWorkflow_ResolvedTicket_Returns400WithValidationMessage()
    {
        // Arrange
        await using var db = CreateInMemoryContext();
        
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Number = "INC0010021",
            ShortDescription = "Resolved ticket",
            Category = "Hardware",
            Priority = TicketPriority.Moderate,
            State = TicketState.Resolved,
            ResolutionNotes = "Already resolved",
            Caller = "user@company.com",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        // Act
        var result = await CallAbandonWorkflowAsync(ticket.Number, db, CancellationToken.None);

        // Assert
        var problemResult = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(400, problemResult.StatusCode);
        Assert.Contains("not in InProgress state", problemResult.ProblemDetails.Detail);
        Assert.Contains("Current state: Resolved", problemResult.ProblemDetails.Detail);
    }

    [Fact]
    public async Task AbandonWorkflow_ClosedTicket_Returns400WithValidationMessage()
    {
        // Arrange
        await using var db = CreateInMemoryContext();
        
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Number = "INC0010022",
            ShortDescription = "Closed ticket",
            Category = "Network",
            Priority = TicketPriority.High,
            State = TicketState.Closed,
            Caller = "user@company.com",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        // Act
        var result = await CallAbandonWorkflowAsync(ticket.Number, db, CancellationToken.None);

        // Assert
        var problemResult = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(400, problemResult.StatusCode);
        Assert.Contains("Current state: Closed", problemResult.ProblemDetails.Detail);
    }

    [Fact]
    public async Task AbandonWorkflow_OnHoldTicket_Returns400WithValidationMessage()
    {
        // Arrange
        await using var db = CreateInMemoryContext();
        
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Number = "INC0010023",
            ShortDescription = "On hold ticket",
            Category = "Software",
            Priority = TicketPriority.Moderate,
            State = TicketState.OnHold,
            Caller = "user@company.com",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        // Act
        var result = await CallAbandonWorkflowAsync(ticket.Number, db, CancellationToken.None);

        // Assert
        var problemResult = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(400, problemResult.StatusCode);
        Assert.Contains("Current state: OnHold", problemResult.ProblemDetails.Detail);
    }

    [Fact]
    public async Task AbandonWorkflow_NonExistentTicket_Returns404()
    {
        // Arrange
        await using var db = CreateInMemoryContext();
        const string nonExistentNumber = "INC9999999";

        // Act
        var result = await CallAbandonWorkflowAsync(nonExistentNumber, db, CancellationToken.None);

        // Assert
        Assert.IsType<NotFound>(result.Result);
    }

    [Fact]
    public async Task AbandonWorkflow_PreservesTicketContent()
    {
        // Arrange
        await using var db = CreateInMemoryContext();
        
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Number = "INC0010024",
            ShortDescription = "Original description",
            Description = "Detailed description that should not change",
            Category = "Hardware",
            Priority = TicketPriority.Critical,
            State = TicketState.InProgress,
            AssignedTo = "original@agent.com",
            AgentAction = "SomeAction",
            AgentConfidence = 0.90,
            MatchedTicketNumber = "KB0001001",
            ResolutionNotes = "Some notes",
            Caller = "user@company.com",
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            UpdatedAt = DateTime.UtcNow.AddHours(-1)
        };

        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        var originalUpdateTime = ticket.UpdatedAt;

        // Act
        await CallAbandonWorkflowAsync(ticket.Number, db, CancellationToken.None);

        // Assert - Content preserved, only state changed
        var updatedTicket = await db.Tickets.FirstAsync(t => t.Number == ticket.Number);
        Assert.Equal(ticket.Id, updatedTicket.Id);
        Assert.Equal(ticket.Number, updatedTicket.Number);
        Assert.Equal(ticket.ShortDescription, updatedTicket.ShortDescription);
        Assert.Equal(ticket.Description, updatedTicket.Description);
        Assert.Equal(ticket.Category, updatedTicket.Category);
        Assert.Equal(ticket.Priority, updatedTicket.Priority);
        Assert.Equal(ticket.AssignedTo, updatedTicket.AssignedTo);
        Assert.Equal(ticket.AgentAction, updatedTicket.AgentAction);
        Assert.Equal(ticket.AgentConfidence, updatedTicket.AgentConfidence);
        Assert.Equal(ticket.MatchedTicketNumber, updatedTicket.MatchedTicketNumber);
        Assert.Equal(ticket.ResolutionNotes, updatedTicket.ResolutionNotes);
        Assert.Equal(ticket.Caller, updatedTicket.Caller);
        Assert.Equal(ticket.CreatedAt, updatedTicket.CreatedAt);
        
        // Only State and UpdatedAt should change
        Assert.Equal(TicketState.New, updatedTicket.State);
        Assert.True(updatedTicket.UpdatedAt >= originalUpdateTime);
    }

    [Fact]
    public async Task AbandonWorkflow_AllowsSubsequentResolutionAttempt()
    {
        // Arrange - Simulate scenario where workflow stalled, needs retry
        await using var db = CreateInMemoryContext();
        
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Number = "INC0010025",
            ShortDescription = "Stuck workflow",
            Category = "Software",
            Priority = TicketPriority.High,
            State = TicketState.InProgress,
            AgentAction = "InProgress",
            Caller = "user@company.com",
            CreatedAt = DateTime.UtcNow.AddHours(-3),
            UpdatedAt = DateTime.UtcNow.AddHours(-2)
        };

        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        // Act - Abandon workflow
        await CallAbandonWorkflowAsync(ticket.Number, db, CancellationToken.None);

        // Assert - Ticket is now New and can be picked up again for resolution
        var updatedTicket = await db.Tickets.FirstAsync(t => t.Number == ticket.Number);
        Assert.Equal(TicketState.New, updatedTicket.State);
        
        // Verify no blocking conditions for retry
        // (In the actual workflow, a New ticket is eligible for resolution attempt)
        Assert.NotEqual(TicketState.InProgress, updatedTicket.State);
        Assert.NotEqual(TicketState.Resolved, updatedTicket.State);
        Assert.NotEqual(TicketState.Closed, updatedTicket.State);
    }
}
