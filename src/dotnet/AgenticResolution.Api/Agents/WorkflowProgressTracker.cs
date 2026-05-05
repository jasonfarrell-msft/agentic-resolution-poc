using AgenticResolution.Api.Data;
using AgenticResolution.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AgenticResolution.Api.Agents;

/// <summary>
/// Persists workflow executor events to the database.
/// Future enhancement: broadcast to SignalR hub for live UI updates.
/// </summary>
public sealed class WorkflowProgressTracker : IWorkflowProgressTracker
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WorkflowProgressTracker> _logger;

    public WorkflowProgressTracker(IServiceScopeFactory scopeFactory, ILogger<WorkflowProgressTracker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task ExecutorStartedAsync(Guid runId, string executorId, CancellationToken ct = default)
        => AddEventAsync(runId, executorId, "Started", null, ct);

    public Task ExecutorRoutedAsync(Guid runId, string executorId, string route, CancellationToken ct = default)
        => AddEventAsync(runId, executorId, "Routed", JsonSerializer.Serialize(new { route }), ct);

    public Task ExecutorOutputAsync(Guid runId, string executorId, string output, CancellationToken ct = default)
        => AddEventAsync(runId, executorId, "Output", JsonSerializer.Serialize(new { output }), ct);

    public Task ExecutorErrorAsync(Guid runId, string executorId, string error, CancellationToken ct = default)
        => AddEventAsync(runId, executorId, "Error", JsonSerializer.Serialize(new { error }), ct);

    public Task ExecutorCompletedAsync(Guid runId, string executorId, CancellationToken ct = default)
        => AddEventAsync(runId, executorId, "Completed", null, ct);

    private async Task AddEventAsync(Guid runId, string executorId, string eventType, string? payload, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            int nextSeq = await db.WorkflowRunEvents
                .Where(e => e.RunId == runId)
                .Select(e => e.Sequence)
                .DefaultIfEmpty(0)
                .MaxAsync(ct) + 1;

            var evt = new WorkflowRunEvent
            {
                Id = Guid.NewGuid(),
                RunId = runId,
                Sequence = nextSeq,
                ExecutorId = executorId,
                EventType = eventType,
                Payload = payload,
                Timestamp = DateTime.UtcNow
            };

            db.WorkflowRunEvents.Add(evt);
            await db.SaveChangesAsync(ct);

            _logger.LogInformation("Workflow event recorded: runId={RunId} seq={Seq} executor={Executor} type={Type}",
                runId, nextSeq, executorId, eventType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record workflow event: runId={RunId} executor={Executor} type={Type}",
                runId, executorId, eventType);
        }
    }
}
