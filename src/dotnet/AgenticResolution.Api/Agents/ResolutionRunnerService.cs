using AgenticResolution.Api.Data;
using AgenticResolution.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Threading.Channels;

namespace AgenticResolution.Api.Agents;

public record ResolutionRunRequest(Guid RunId, string TicketNumber);

public interface IResolutionQueue
{
    ChannelReader<ResolutionRunRequest> Reader { get; }
    bool Enqueue(ResolutionRunRequest request);
}

/// <summary>
/// In-memory queue for manual resolution run requests.
/// Decoupled from webhook auto-dispatch path - only populated via POST /api/tickets/{number}/resolve.
/// </summary>
public sealed class ResolutionQueue : IResolutionQueue
{
    private readonly Channel<ResolutionRunRequest> _channel;
    private readonly ILogger<ResolutionQueue> _logger;

    public ChannelReader<ResolutionRunRequest> Reader => _channel.Reader;

    public ResolutionQueue(ILogger<ResolutionQueue> logger)
    {
        _logger = logger;
        _channel = Channel.CreateBounded<ResolutionRunRequest>(new BoundedChannelOptions(512)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public bool Enqueue(ResolutionRunRequest request)
    {
        bool ok = _channel.Writer.TryWrite(request);
        if (!ok)
            _logger.LogWarning("Resolution queue rejected runId={RunId}", request.RunId);
        else
            _logger.LogInformation("Resolution run enqueued: runId={RunId} ticket={Ticket}",
                request.RunId, request.TicketNumber);
        return ok;
    }
}

/// <summary>
/// Background service that processes manual resolution runs.
/// Each run invokes AgentOrchestrationService with progress tracking.
/// Optionally fires webhooks on workflow state transitions.
/// </summary>
public sealed class ResolutionRunnerService : BackgroundService
{
    private readonly IResolutionQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ResolutionRunnerService> _logger;

    public ResolutionRunnerService(IResolutionQueue queue, IServiceScopeFactory scopeFactory,
        ILogger<ResolutionRunnerService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Resolution runner service started");

        await foreach (var request in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessRunAsync(request, stoppingToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Resolution run failed: runId={RunId}", request.RunId);
                await MarkRunFailedAsync(request.RunId, ex.Message, stoppingToken);
            }
        }

        _logger.LogInformation("Resolution runner service stopping");
    }

    private async Task ProcessRunAsync(ResolutionRunRequest request, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var orchestrator = scope.ServiceProvider.GetRequiredService<AgentOrchestrationService>();
        var progress = scope.ServiceProvider.GetRequiredService<IWorkflowProgressTracker>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var webhookDispatcher = scope.ServiceProvider.GetRequiredService<Webhooks.IWebhookDispatcher>();

        var run = await db.WorkflowRuns.FindAsync([request.RunId], ct);
        if (run is null)
        {
            _logger.LogWarning("WorkflowRun {RunId} not found - skipping", request.RunId);
            return;
        }

        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Number == request.TicketNumber, ct);
        if (ticket is null)
        {
            _logger.LogWarning("Ticket {TicketNumber} not found for runId={RunId} - skipping",
                request.TicketNumber, request.RunId);
            return;
        }

        _logger.LogInformation("Starting resolution run: runId={RunId} ticket={Ticket}",
            request.RunId, request.TicketNumber);

        run.Status = WorkflowRunStatus.Running;
        await db.SaveChangesAsync(ct);

        // Fire webhook on workflow start (if enabled)
        bool fireProgressWebhooks = config.GetValue("Webhook:FireOnWorkflowProgress", false);
        if (fireProgressWebhooks)
            webhookDispatcher.Enqueue(Webhooks.WebhookPayload.ForWorkflowRunning(ticket, run.Id));

        try
        {
            var result = await orchestrator.ProcessTicketAsync(
                request.TicketNumber, request.RunId, progress, ct);

            run.Status = result.Action.Contains("escalate", StringComparison.OrdinalIgnoreCase)
                ? WorkflowRunStatus.Escalated
                : WorkflowRunStatus.Completed;
            run.FinalAction = result.Action;
            run.FinalConfidence = result.ResolutionConfidence;
            run.CompletedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);

            // Fire webhook on workflow completion (if enabled)
            if (fireProgressWebhooks)
            {
                var payload = run.Status == WorkflowRunStatus.Escalated
                    ? Webhooks.WebhookPayload.ForWorkflowEscalated(ticket, run.Id)
                    : Webhooks.WebhookPayload.ForWorkflowCompleted(ticket, run.Id);
                webhookDispatcher.Enqueue(payload);
            }

            _logger.LogInformation("Resolution run completed: runId={RunId} status={Status} action={Action}",
                request.RunId, run.Status, result.Action);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Resolution run failed during orchestration: runId={RunId}", request.RunId);
            await progress.ExecutorErrorAsync(request.RunId, "OrchestrationError", ex.Message, ct);
            
            // Fire webhook on workflow failure (if enabled)
            if (fireProgressWebhooks)
                webhookDispatcher.Enqueue(Webhooks.WebhookPayload.ForWorkflowFailed(ticket, run.Id, ex.Message));
            
            throw;
        }
    }

    private async Task MarkRunFailedAsync(Guid runId, string error, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var run = await db.WorkflowRuns.FindAsync([runId], ct);
            if (run is null) return;

            run.Status = WorkflowRunStatus.Failed;
            run.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark run as failed: runId={RunId}", runId);
        }
    }
}
