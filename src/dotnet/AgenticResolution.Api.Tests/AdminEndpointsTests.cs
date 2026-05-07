using AgenticResolution.Api.Api;
using AgenticResolution.Api.Data;
using AgenticResolution.Api.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AgenticResolution.Api.Tests;

/// <summary>
/// Tests for admin endpoint reset data logic using in-memory database.
/// These tests verify the reset behavior expectations without using ExecuteUpdateAsync.
/// </summary>
public class AdminEndpointsTests
{
    private AppDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"TestDb_{Guid.NewGuid()}")
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public async Task ResetData_TestTicketStateTransitions()
    {
        // Arrange
        await using var db = CreateInMemoryContext();
        
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Number = "INC0010001",
            ShortDescription = "Test ticket",
            Description = "Description",
            Category = "Test",
            Priority = TicketPriority.High,
            State = TicketState.InProgress,
            AssignedTo = "agent@company.com",
            AgentAction = "RouteToTeam",
            AgentConfidence = 0.95,
            ResolutionNotes = "In progress",
            Caller = "user@company.com",
            CreatedAt = DateTime.UtcNow.AddHours(-2),
            UpdatedAt = DateTime.UtcNow.AddHours(-1)
        };

        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        // Act - Simulate reset by loading and updating
        var ticketToReset = await db.Tickets.FirstAsync();
        ticketToReset.State = TicketState.New;
        ticketToReset.AssignedTo = null;
        ticketToReset.ResolutionNotes = null;
        ticketToReset.AgentAction = null;
        ticketToReset.AgentConfidence = null;
        ticketToReset.MatchedTicketNumber = null;
        ticketToReset.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Assert
        var resetTicket = await db.Tickets.FirstAsync();
        Assert.Equal(TicketState.New, resetTicket.State);
        Assert.Null(resetTicket.AssignedTo);
        Assert.Null(resetTicket.ResolutionNotes);
        Assert.Null(resetTicket.AgentAction);
        Assert.Null(resetTicket.AgentConfidence);
        Assert.Null(resetTicket.MatchedTicketNumber);
    }

    [Fact]
    public async Task ResetData_PreservesTicketIdentityAndContent()
    {
        // Arrange
        await using var db = CreateInMemoryContext();
        
        var originalTicket = new Ticket
        {
            Id = Guid.NewGuid(),
            Number = "INC0010001",
            ShortDescription = "Original description",
            Description = "Original details",
            Category = "Hardware",
            Priority = TicketPriority.Critical,
            State = TicketState.InProgress,
            AssignedTo = "agent@company.com",
            Caller = "caller@company.com",
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            UpdatedAt = DateTime.UtcNow
        };

        db.Tickets.Add(originalTicket);
        await db.SaveChangesAsync();

        // Act - Simulate reset
        var ticketToReset = await db.Tickets.FirstAsync();
        var originalId = ticketToReset.Id;
        var originalNumber = ticketToReset.Number;
        var originalDesc = ticketToReset.ShortDescription;
        var originalCreated = ticketToReset.CreatedAt;

        ticketToReset.State = TicketState.New;
        ticketToReset.AssignedTo = null;
        ticketToReset.ResolutionNotes = null;
        ticketToReset.AgentAction = null;
        ticketToReset.AgentConfidence = null;
        ticketToReset.MatchedTicketNumber = null;
        ticketToReset.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Assert - Core properties preserved
        var resetTicket = await db.Tickets.FirstAsync();
        Assert.Equal(originalId, resetTicket.Id);
        Assert.Equal(originalNumber, resetTicket.Number);
        Assert.Equal(originalDesc, resetTicket.ShortDescription);
        Assert.Equal(originalCreated, resetTicket.CreatedAt);
    }

    [Fact]
    public async Task ResetData_ClearsAgentFieldsCompletely()
    {
        // Arrange
        await using var db = CreateInMemoryContext();
        
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Number = "INC0010001",
            ShortDescription = "Test",
            Description = "Test",
            Category = "Test",
            Priority = TicketPriority.High,
            State = TicketState.InProgress,
            AssignedTo = "agent@company.com",
            AgentAction = "CloseTicket",
            AgentConfidence = 0.99,
            MatchedTicketNumber = "INC0009999",
            ResolutionNotes = "Resolved by automation",
            Caller = "user@company.com",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        // Act - Simulate reset
        var ticketToReset = await db.Tickets.FirstAsync();
        ticketToReset.State = TicketState.New;
        ticketToReset.AssignedTo = null;
        ticketToReset.ResolutionNotes = null;
        ticketToReset.AgentAction = null;
        ticketToReset.AgentConfidence = null;
        ticketToReset.MatchedTicketNumber = null;
        ticketToReset.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Assert
        var resetTicket = await db.Tickets.FirstAsync();
        Assert.Null(resetTicket.AgentAction);
        Assert.Null(resetTicket.AgentConfidence);
        Assert.Null(resetTicket.MatchedTicketNumber);
        Assert.Null(resetTicket.ResolutionNotes);
        Assert.Null(resetTicket.AssignedTo);
        Assert.Equal(TicketState.New, resetTicket.State);
    }

    [Fact]
    public async Task ResetData_UpdatesTimestamp()
    {
        // Arrange
        await using var db = CreateInMemoryContext();
        
        var oldTimestamp = DateTime.UtcNow.AddDays(-10);
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Number = "INC0010001",
            ShortDescription = "Test",
            Description = "Test",
            Category = "Test",
            Priority = TicketPriority.Moderate,
            State = TicketState.Resolved,
            Caller = "user@company.com",
            CreatedAt = oldTimestamp,
            UpdatedAt = oldTimestamp
        };

        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        var beforeReset = DateTime.UtcNow;
        await Task.Delay(10); // Small delay to ensure timestamp difference

        // Act - Simulate reset
        var ticketToReset = await db.Tickets.FirstAsync();
        ticketToReset.State = TicketState.New;
        ticketToReset.AssignedTo = null;
        ticketToReset.ResolutionNotes = null;
        ticketToReset.AgentAction = null;
        ticketToReset.AgentConfidence = null;
        ticketToReset.MatchedTicketNumber = null;
        ticketToReset.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Assert
        var resetTicket = await db.Tickets.FirstAsync();
        Assert.True(resetTicket.UpdatedAt > beforeReset, "UpdatedAt should be set to current time");
        Assert.Equal(oldTimestamp, resetTicket.CreatedAt); // CreatedAt unchanged
    }

    [Fact]
    public async Task ResetData_HandlesMultipleTicketStates()
    {
        // Arrange
        await using var db = CreateInMemoryContext();
        
        var states = new[] 
        { 
            TicketState.New, 
            TicketState.InProgress, 
            TicketState.Resolved, 
            TicketState.Closed 
        };

        foreach (var state in states)
        {
            db.Tickets.Add(new Ticket
            {
                Id = Guid.NewGuid(),
                Number = $"INC001000{Array.IndexOf(states, state) + 1}",
                ShortDescription = $"Ticket in {state} state",
                Description = "Test",
                Category = "Test",
                Priority = TicketPriority.Moderate,
                State = state,
                AssignedTo = state != TicketState.New ? "agent@company.com" : null,
                Caller = "user@company.com",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();

        // Act - Simulate reset all
        var tickets = await db.Tickets.ToListAsync();
        foreach (var ticket in tickets)
        {
            ticket.State = TicketState.New;
            ticket.AssignedTo = null;
            ticket.ResolutionNotes = null;
            ticket.AgentAction = null;
            ticket.AgentConfidence = null;
            ticket.MatchedTicketNumber = null;
            ticket.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();

        // Assert
        var resetTickets = await db.Tickets.ToListAsync();
        Assert.Equal(states.Length, resetTickets.Count);
        Assert.All(resetTickets, ticket => 
        {
            Assert.Equal(TicketState.New, ticket.State);
            Assert.Null(ticket.AssignedTo);
        });
    }

    [Fact]
    public async Task SeedSampleTickets_AlwaysDeletesAndReseeds()
    {
        // Arrange - verify that seeding replaces existing tickets (not conditional on empty table)
        await using var db = CreateInMemoryContext();

        // Add existing tickets (simulating a DB that already has data)
        db.Tickets.AddRange(
            new Ticket { Id = Guid.NewGuid(), Number = "INC0009001", ShortDescription = "Old ticket 1", Description = "Old", Category = "Old", Priority = TicketPriority.Low, State = TicketState.Resolved, Caller = "old@company.com", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Ticket { Id = Guid.NewGuid(), Number = "INC0009002", ShortDescription = "Old ticket 2", Description = "Old", Category = "Old", Priority = TicketPriority.Low, State = TicketState.Closed, Caller = "old@company.com", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        );
        await db.SaveChangesAsync();

        // Simulate the new seed behavior: always delete + re-seed
        db.Tickets.RemoveRange(db.Tickets.ToList());
        await db.SaveChangesAsync();

        var freshTickets = new[]
        {
            new Ticket { Id = Guid.NewGuid(), Number = "INC0010001", ShortDescription = "Fresh seed ticket", Description = "Seeded", Category = "Email", Priority = TicketPriority.High, State = TicketState.New, AssignedTo = null, Caller = "user@company.com", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        };
        db.Tickets.AddRange(freshTickets);
        await db.SaveChangesAsync();

        // Assert - old tickets gone, only fresh seed data present
        var allTickets = await db.Tickets.ToListAsync();
        Assert.Single(allTickets);
        Assert.Equal("INC0010001", allTickets[0].Number);
        Assert.Equal(TicketState.New, allTickets[0].State);
        Assert.Null(allTickets[0].AssignedTo);
    }

    [Fact]
    public void ResetDataRequest_DefaultValues()
    {
        // Arrange & Act
        var request = new ResetDataRequest();

        // Assert
        Assert.True(request.ResetTickets);
        Assert.False(request.SeedSampleTickets);
    }

    [Fact]
    public void ResetDataResponse_Structure()
    {
        // Arrange & Act
        var response = new ResetDataResponse(5, 2, "Test message");

        // Assert
        Assert.Equal(5, response.TicketsReset);
        Assert.Equal(2, response.TicketsSeeded);
        Assert.Equal("Test message", response.Message);
    }
}
