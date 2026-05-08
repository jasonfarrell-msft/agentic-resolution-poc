# Skill: EF Core Bulk Operation Testing Pattern

## Context

When testing admin endpoints or data management operations that use EF Core bulk operations (ExecuteUpdateAsync, ExecuteDeleteAsync), in-memory DB providers do NOT support these methods. Tests must either:
1. Use substitutes (RemoveRange/manual updates) and document limitations
2. Use real SQL Server via testcontainers for production-fidelity coverage

## Pattern: Document In-Memory DB Limitations

### Problem

In-memory DB throws InvalidOperationException when code calls:
```csharp
await db.Tickets.ExecuteDeleteAsync(ct);
await db.Tickets.ExecuteUpdateAsync(...);
```

Error: The methods 'ExecuteDelete' and 'ExecuteDeleteAsync' are not supported by the current database provider.

### Solution: Test Intent with Substitute + Document Limitation

**Step 1:** Use in-memory DB substitute for bulk operations

```csharp
// PRODUCTION CODE:
// await db.Tickets.ExecuteDeleteAsync(ct);

// TEST SUBSTITUTE:
db.Tickets.RemoveRange(db.Tickets.ToList());
await db.SaveChangesAsync();
```

**Step 2:** Document limitation in test class header

```csharp
/// <summary>
/// Tests for reseed behavior (delete + insert + sequence reset).
/// 
/// CRITICAL LIMITATION: In-memory DB does NOT support ExecuteDeleteAsync/ExecuteUpdateAsync.
/// Tests simulate behavior using RemoveRange/manual updates to verify INTENT, but do NOT
/// test actual bulk operation code paths used in production.
/// 
/// Phase 2 MUST migrate to SQL testcontainers to validate:
/// - ExecuteDeleteAsync behavior against real SQL Server
/// - Transaction boundaries and rollback semantics
/// - Unique constraints on ticket numbers
/// </summary>
```

**Step 3:** Add Phase 2 requirement in decision artifact

Create .squad/decisions/inbox/{agent}-{topic}.md:
```markdown
## Phase 2 Requirements

| Requirement | Status | Notes |
|------------|--------|-------|
| Real DB tests (SQL testcontainers) | ❌ | **Blocks production deployment** |
```

### When to Use This Pattern

✅ **Use in-memory substitute + documentation when:**
- Testing admin/data management endpoints
- Production code uses ExecuteUpdateAsync / ExecuteDeleteAsync
- You need quick feedback on logic intent
- Real DB setup is not yet in place

❌ **Do NOT use in-memory DB for:**
- Transaction rollback testing
- Unique constraint validation
- Concurrency conflict resolution
- Production deployment validation

### Phase 2: Migrate to SQL Testcontainers

**Add package:**
```xml
<PackageReference Include="Testcontainers.MsSql" Version="3.10.0" />
```

**Create real DB fixture:**
```csharp
public class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync() => await _container.StartAsync();
    public async Task DisposeAsync() => await _container.DisposeAsync();
}
```

**Use in tests:**
```csharp
public class AdminReseedSqlIntegrationTests : IClassFixture<SqlServerFixture>
{
    private readonly SqlServerFixture _fixture;
    
    public AdminReseedSqlIntegrationTests(SqlServerFixture fixture) 
        => _fixture = fixture;

    [Fact]
    public async Task Reseed_DeletesAllExistingTickets_RealDb()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_fixture.ConnectionString)
            .Options;
        
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        
        // Act - REAL ExecuteDeleteAsync call
        await db.Tickets.ExecuteDeleteAsync();
        
        // Assert
        Assert.Equal(0, await db.Tickets.CountAsync());
    }
}
```

## Examples

### Example 1: Reseed Test with In-Memory Substitute

```csharp
[Fact]
public async Task Reseed_DeletesAllExistingTickets()
{
    // Arrange
    await using var db = CreateInMemoryContext();
    db.Tickets.AddRange(/* existing tickets */);
    await db.SaveChangesAsync();

    // Act - In-memory substitute (documented limitation)
    db.Tickets.RemoveRange(db.Tickets.ToList());
    await db.SaveChangesAsync();
    
    db.Tickets.Add(/* fresh ticket */);
    await db.SaveChangesAsync();

    // Assert
    Assert.Single(await db.Tickets.ToListAsync());
}
```

### Example 2: Real DB Test (Phase 2)

```csharp
[Fact]
public async Task Reseed_ExecuteDeleteAsync_RealDb()
{
    // Arrange
    await using var db = CreateSqlServerContext();
    db.Tickets.Add(/* ticket */);
    await db.SaveChangesAsync();

    // Act - REAL bulk operation
    int deleted = await db.Tickets.ExecuteDeleteAsync();

    // Assert
    Assert.Equal(1, deleted);
    Assert.Equal(0, await db.Tickets.CountAsync());
}
```

## Tradeoffs

### In-Memory DB Substitute

**Pros:**
- ✅ Fast (no Docker, no SQL Server)
- ✅ Tests logic intent
- ✅ Idempotency, sequence state, baseline insertion

**Cons:**
- ❌ Doesn't test actual bulk operation code path
- ❌ Missing transaction rollback semantics
- ❌ No unique constraint validation
- ❌ False confidence if not documented

### SQL Testcontainers

**Pros:**
- ✅ Production-fidelity testing
- ✅ Validates actual EF Core bulk operations
- ✅ Transaction rollback, constraints, concurrency

**Cons:**
- ❌ Slower (Docker + SQL Server startup ~10s)
- ❌ Requires Docker Desktop or Podman
- ❌ More complex test setup

## References

- **Implementation:** src/dotnet/AgenticResolution.Api.Tests/AdminReseedIntegrationTests.cs
- **Decision:** .squad/decisions/inbox/vasquez-reseed-tests.md
- **History:** .squad/agents/vasquez/history.md (2026-05-08 session)
- **Apone's design review:** .squad/decisions/inbox/apone-reseed-review.md

## Related Patterns

- Admin endpoint testing (API key auth, configuration gates)
- Data reset/seed contract design
- Test infrastructure migration (in-memory → testcontainers)

---

**Status:** ✅ Pattern established | Phase 2 migration path documented