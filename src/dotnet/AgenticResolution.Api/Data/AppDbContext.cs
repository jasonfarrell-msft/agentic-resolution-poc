using AgenticResolution.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AgenticResolution.Api.Data;

public class AppDbContext : DbContext
{
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketNumberSequence> TicketNumberSequences => Set<TicketNumberSequence>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var ticket = modelBuilder.Entity<Ticket>();
        ticket.HasKey(t => t.Id);
        ticket.Property(t => t.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
        ticket.Property(t => t.Number).IsRequired().HasMaxLength(15);
        ticket.HasIndex(t => t.Number).IsUnique();
        ticket.HasIndex(t => t.State);
        ticket.HasIndex(t => t.CreatedAt);
        ticket.Property(t => t.ShortDescription).IsRequired().HasMaxLength(200);
        ticket.Property(t => t.Category).IsRequired().HasMaxLength(100);
        ticket.Property(t => t.Caller).IsRequired().HasMaxLength(100);
        ticket.Property(t => t.AssignedTo).HasMaxLength(100);
        ticket.Property(t => t.Priority).HasConversion<int>();
        ticket.Property(t => t.State).HasConversion<int>();
        ticket.Property(t => t.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        ticket.Property(t => t.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        ticket.Property(t => t.AgentAction).HasMaxLength(100);
        ticket.Property(t => t.MatchedTicketNumber).HasMaxLength(20);
        ticket.Property(t => t.Classification).HasMaxLength(20);

        var seq = modelBuilder.Entity<TicketNumberSequence>();
        seq.HasKey(s => s.Id);
        seq.Property(s => s.Id).ValueGeneratedNever();
        seq.HasData(new TicketNumberSequence { Id = 1, LastValue = 10000L });
    }
}

public interface ITicketNumberGenerator
{
    Task<string> NextAsync(CancellationToken ct = default);
}

public class TicketNumberGenerator : ITicketNumberGenerator
{
    private readonly AppDbContext _db;
    private static readonly SemaphoreSlim _gate = new(1, 1);

    public TicketNumberGenerator(AppDbContext db) => _db = db;

    public async Task<string> NextAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            long value = (await _db.Database
                .SqlQueryRaw<long>(
                    "UPDATE TicketNumberSequences SET LastValue = LastValue + 1 OUTPUT INSERTED.LastValue AS Value WHERE Id = 1",
                    Array.Empty<object>())
                .ToListAsync(ct))
                .FirstOrDefault();

            if (value == 0L)
            {
                var row = await _db.TicketNumberSequences.FirstAsync(s => s.Id == 1, ct);
                row.LastValue++;
                await _db.SaveChangesAsync(ct);
                value = row.LastValue;
            }
            return $"INC{value:D7}";
        }
        finally
        {
            _gate.Release();
        }
    }
}
