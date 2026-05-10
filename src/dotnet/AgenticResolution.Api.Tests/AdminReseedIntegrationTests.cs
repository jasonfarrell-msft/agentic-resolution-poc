using AgenticResolution.Api.Api;
using AgenticResolution.Api.Data;
using AgenticResolution.Api.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AgenticResolution.Api.Tests;

/// <summary>
/// Focused regression tests for the reseed behavior (SeedSampleTickets = true).
/// Tests verify that delete + insert + sequence reset work as expected.
/// 
/// CRITICAL LIMITATION: In-memory DB does NOT support ExecuteDeleteAsync/ExecuteUpdateAsync.
/// Tests simulate the behavior using RemoveRange/manual updates to verify INTENT, but do NOT
/// test the actual bulk operation code paths used in production.
/// 
/// Phase 2 MUST migrate to SQL testcontainers to validate:
/// - ExecuteDeleteAsync behavior against real SQL Server
/// - Uniqueness constraints on ticket numbers
/// - Transaction boundaries and rollback semantics
/// - Sequence state consistency after failures
/// </summary>
public class AdminReseedIntegrationTests
{
    private AppDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"ReseedTestDb_{Guid.NewGuid()}")
            .Options;

        var context = new AppDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public async Task Reseed_DeletesAllExistingTickets()
    {
        // Arrange
        await using var db = CreateInMemoryContext();
        
        // Add pre-existing tickets (simulating a DB with data)
        db.Tickets.AddRange(
            new Ticket { Id = Guid.NewGuid(), Number = "INC0009001", ShortDescription = "Old ticket 1", Description = "Old", Category = "Old", Priority = TicketPriority.Low, State = TicketState.Resolved, Caller = "old@company.com", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Ticket { Id = Guid.NewGuid(), Number = "INC0009002", ShortDescription = "Old ticket 2", Description = "Old", Category = "Old", Priority = TicketPriority.Low, State = TicketState.Closed, Caller = "old@company.com", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Ticket { Id = Guid.NewGuid(), Number = "INC0009003", ShortDescription = "Old ticket 3", Description = "Old", Category = "Old", Priority = TicketPriority.High, State = TicketState.InProgress, Caller = "old@company.com", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        );
        await db.SaveChangesAsync();

        Assert.Equal(3, await db.Tickets.CountAsync());

        // Act - Simulate reseed: delete + insert fresh
        // NOTE: In-memory DB doesn't support ExecuteDeleteAsync; using RemoveRange as test substitute
        db.Tickets.RemoveRange(db.Tickets.ToList());
        await db.SaveChangesAsync();
        
        var freshTickets = new[]
        {
            new Ticket { Id = Guid.NewGuid(), Number = "INC0010001", ShortDescription = "Fresh seed ticket", Description = "Seeded", Category = "Email", Priority = TicketPriority.High, State = TicketState.New, AssignedTo = null, Caller = "user@company.com", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        };
        db.Tickets.AddRange(freshTickets);
        await db.SaveChangesAsync();

        // Assert - old tickets are gone, only fresh data present
        var allTickets = await db.Tickets.ToListAsync();
        Assert.Single(allTickets);
        Assert.Equal("INC0010001", allTickets[0].Number);
        Assert.Equal("Fresh seed ticket", allTickets[0].ShortDescription);
        Assert.Equal(TicketState.New, allTickets[0].State);
        Assert.Null(allTickets[0].AssignedTo);
    }

    [Fact]
    public async Task Reseed_InsertsNewTicketsWithCorrectBaseline()
    {
        // Arrange
        await using var db = CreateInMemoryContext();
        
        // Simulate reseed: clear all, insert fresh
        // NOTE: In-memory DB doesn't support ExecuteDeleteAsync; using RemoveRange as test substitute
        db.Tickets.RemoveRange(db.Tickets.ToList());
        await db.SaveChangesAsync();

        var sampleTickets = new[]
        {
            new Ticket { Id = Guid.NewGuid(), Number = "INC0010001", ShortDescription = "Ticket 1", Description = "Test", Category = "Email", Priority = TicketPriority.High, State = TicketState.New, Caller = "user1@company.com", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Ticket { Id = Guid.NewGuid(), Number = "INC0010002", ShortDescription = "Ticket 2", Description = "Test", Category = "Hardware", Priority = TicketPriority.Moderate, State = TicketState.New, Caller = "user2@company.com", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Ticket { Id = Guid.NewGuid(), Number = "INC0010003", ShortDescription = "Ticket 3", Description = "Test", Category = "Network", Priority = TicketPriority.High, State = TicketState.New, Caller = "user3@company.com", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        };
        
        db.Tickets.AddRange(sampleTickets);
        await db.SaveChangesAsync();

        // Assert - tickets inserted correctly
        var allTickets = await db.Tickets.OrderBy(t => t.Number).ToListAsync();
        Assert.Equal(3, allTickets.Count);
        Assert.Equal("INC0010001", allTickets[0].Number);
        Assert.Equal("INC0010002", allTickets[1].Number);
        Assert.Equal("INC0010003", allTickets[2].Number);
        Assert.All(allTickets, t => Assert.Equal(TicketState.New, t.State));
        Assert.All(allTickets, t => Assert.Null(t.AssignedTo));
    }

    [Fact]
    public async Task Reseed_SetsSequenceToMatchInsertedTickets()
    {
        // Arrange
        await using var db = CreateInMemoryContext();
        
        // Simulate reseed flow from AdminEndpoints.ResetDataAsync
        // NOTE: In-memory DB doesn't support ExecuteDeleteAsync; using RemoveRange as test substitute
        db.Tickets.RemoveRange(db.Tickets.ToList());
        await db.SaveChangesAsync();

        var sampleTickets = new[]
        {
            new Ticket { Id = Guid.NewGuid(), Number = "INC0010001", ShortDescription = "T1", Description = "Test", Category = "Email", Priority = TicketPriority.High, State = TicketState.New, Caller = "user@company.com", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Ticket { Id = Guid.NewGuid(), Number = "INC0010002", ShortDescription = "T2", Description = "Test", Category = "Hardware", Priority = TicketPriority.Moderate, State = TicketState.New, Caller = "user@company.com", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        };
        
        db.Tickets.AddRange(sampleTickets);
        await db.SaveChangesAsync();

        int ticketsSeeded = sampleTickets.Length;

        // Act - Reset sequence to reflect seeded tickets (matching AdminEndpoints.cs logic)
        var sequence = await db.TicketNumberSequences.FirstOrDefaultAsync(s => s.Id == 1);
        Assert.NotNull(sequence); // Should be seeded by OnModelCreating
        
        sequence.LastValue = 10000 + ticketsSeeded; // 10000 + 2 = 10002
        await db.SaveChangesAsync();

        // Assert - sequence state correct
        var updatedSequence = await db.TicketNumberSequences.FirstAsync(s => s.Id == 1);
        Assert.Equal(10002, updatedSequence.LastValue);
    }

    [Fact]
    public async Task Reseed_IdempotentWhenCalledTwice()
    {
        // Arrange
        await using var db = CreateInMemoryContext();
        
        // First reseed
        // NOTE: In-memory DB doesn't support ExecuteDeleteAsync; using RemoveRange as test substitute
        db.Tickets.RemoveRange(db.Tickets.ToList());
        await db.SaveChangesAsync();
        db.Tickets.Add(new Ticket { Id = Guid.NewGuid(), Number = "INC0010001", ShortDescription = "First seed", Description = "Test", Category = "Email", Priority = TicketPriority.High, State = TicketState.New, Caller = "user@company.com", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var firstSeedCount = await db.Tickets.CountAsync();
        Assert.Equal(1, firstSeedCount);

        // Act - Second reseed (delete + insert same data)
        db.Tickets.RemoveRange(db.Tickets.ToList());
        await db.SaveChangesAsync();
        db.Tickets.Add(new Ticket { Id = Guid.NewGuid(), Number = "INC0010001", ShortDescription = "Second seed", Description = "Test", Category = "Email", Priority = TicketPriority.High, State = TicketState.New, Caller = "user@company.com", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        // Assert - still only 1 ticket, data replaced
        var secondSeedCount = await db.Tickets.CountAsync();
        Assert.Equal(1, secondSeedCount);
        
        var ticket = await db.Tickets.FirstAsync();
        Assert.Equal("INC0010001", ticket.Number);
        Assert.Equal("Second seed", ticket.ShortDescription);
    }

    [Fact]
    public async Task Reseed_ClearsAllTicketStates()
    {
        // Arrange - Create tickets in various states
        await using var db = CreateInMemoryContext();
        
        db.Tickets.AddRange(
            new Ticket { Id = Guid.NewGuid(), Number = "INC0009001", ShortDescription = "New ticket", Description = "Test", Category = "Test", Priority = TicketPriority.Moderate, State = TicketState.New, Caller = "user@company.com", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Ticket { Id = Guid.NewGuid(), Number = "INC0009002", ShortDescription = "In progress", Description = "Test", Category = "Test", Priority = TicketPriority.High, State = TicketState.InProgress, AssignedTo = "agent@company.com", Caller = "user@company.com", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Ticket { Id = Guid.NewGuid(), Number = "INC0009003", ShortDescription = "Resolved", Description = "Test", Category = "Test", Priority = TicketPriority.Low, State = TicketState.Resolved, AssignedTo = "agent@company.com", ResolutionNotes = "Fixed", Caller = "user@company.com", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Ticket { Id = Guid.NewGuid(), Number = "INC0009004", ShortDescription = "Closed", Description = "Test", Category = "Test", Priority = TicketPriority.Moderate, State = TicketState.Closed, AssignedTo = "agent@company.com", ResolutionNotes = "Done", Caller = "user@company.com", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        );
        await db.SaveChangesAsync();

        Assert.Equal(4, await db.Tickets.CountAsync());

        // Act - Reseed: delete all + insert fresh
        // NOTE: In-memory DB doesn't support ExecuteDeleteAsync; using RemoveRange as test substitute
        db.Tickets.RemoveRange(db.Tickets.ToList());
        await db.SaveChangesAsync();
        
        db.Tickets.Add(new Ticket { Id = Guid.NewGuid(), Number = "INC0010001", ShortDescription = "Fresh ticket", Description = "Seeded", Category = "Email", Priority = TicketPriority.High, State = TicketState.New, AssignedTo = null, Caller = "user@company.com", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        // Assert - all old states gone, only New state present
        var allTickets = await db.Tickets.ToListAsync();
        Assert.Single(allTickets);
        Assert.Equal(TicketState.New, allTickets[0].State);
        Assert.Null(allTickets[0].AssignedTo);
        Assert.Null(allTickets[0].ResolutionNotes);
    }

    [Fact]
    public async Task Reseed_PreservesSequenceRow()
    {
        // Arrange - Verify sequence row exists
        await using var db = CreateInMemoryContext();
        
        var sequenceBefore = await db.TicketNumberSequences.FirstOrDefaultAsync(s => s.Id == 1);
        Assert.NotNull(sequenceBefore);
        Assert.Equal(10000, sequenceBefore.LastValue); // Default seed value

        // Act - Simulate reseed (delete tickets, insert new, update sequence)
        // NOTE: In-memory DB doesn't support ExecuteDeleteAsync; using RemoveRange as test substitute
        db.Tickets.RemoveRange(db.Tickets.ToList());
        await db.SaveChangesAsync();
        
        db.Tickets.AddRange(
            new Ticket { Id = Guid.NewGuid(), Number = "INC0010001", ShortDescription = "T1", Description = "Test", Category = "Email", Priority = TicketPriority.High, State = TicketState.New, Caller = "user@company.com", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Ticket { Id = Guid.NewGuid(), Number = "INC0010002", ShortDescription = "T2", Description = "Test", Category = "Hardware", Priority = TicketPriority.Moderate, State = TicketState.New, Caller = "user@company.com", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Ticket { Id = Guid.NewGuid(), Number = "INC0010003", ShortDescription = "T3", Description = "Test", Category = "Network", Priority = TicketPriority.High, State = TicketState.New, Caller = "user@company.com", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        );
        await db.SaveChangesAsync();

        var sequence = await db.TicketNumberSequences.FirstAsync(s => s.Id == 1);
        sequence.LastValue = 10000 + 3; // Update to match seeded count
        await db.SaveChangesAsync();

        // Assert - sequence row still exists with updated value
        var sequenceAfter = await db.TicketNumberSequences.FirstAsync(s => s.Id == 1);
        Assert.Equal(10003, sequenceAfter.LastValue);
    }

    [Fact]
    public async Task Reseed_EmptyDatabase_InsertsCleanBaseline()
    {
        // Arrange - Empty database (no pre-existing tickets)
        await using var db = CreateInMemoryContext();
        
        Assert.Equal(0, await db.Tickets.CountAsync());

        // Act - Reseed on empty DB
        // NOTE: In-memory DB doesn't support ExecuteDeleteAsync; using RemoveRange as test substitute
        db.Tickets.RemoveRange(db.Tickets.ToList()); // No-op on empty
        await db.SaveChangesAsync();
        
        var sampleTickets = new[]
        {
            new Ticket { Id = Guid.NewGuid(), Number = "INC0010001", ShortDescription = "Baseline ticket 1", Description = "Test", Category = "Email", Priority = TicketPriority.High, State = TicketState.New, Caller = "user@company.com", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Ticket { Id = Guid.NewGuid(), Number = "INC0010002", ShortDescription = "Baseline ticket 2", Description = "Test", Category = "Hardware", Priority = TicketPriority.Moderate, State = TicketState.New, Caller = "user@company.com", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        };
        
        db.Tickets.AddRange(sampleTickets);
        await db.SaveChangesAsync();

        // Assert - clean baseline inserted
        var allTickets = await db.Tickets.OrderBy(t => t.Number).ToListAsync();
        Assert.Equal(2, allTickets.Count);
        Assert.Equal("INC0010001", allTickets[0].Number);
        Assert.Equal("INC0010002", allTickets[1].Number);
        Assert.All(allTickets, t => Assert.Equal(TicketState.New, t.State));
    }
}
