namespace AgenticResolution.Api.Agents;

/// <summary>
/// Interface for tracking agent workflow progress.
/// Implementations persist events to DB and/or broadcast to SignalR clients.
/// </summary>
public interface IWorkflowProgressTracker
{
    Task ExecutorStartedAsync(Guid runId, string executorId, CancellationToken ct = default);
    Task ExecutorRoutedAsync(Guid runId, string executorId, string route, CancellationToken ct = default);
    Task ExecutorOutputAsync(Guid runId, string executorId, string output, CancellationToken ct = default);
    Task ExecutorErrorAsync(Guid runId, string executorId, string error, CancellationToken ct = default);
    Task ExecutorCompletedAsync(Guid runId, string executorId, CancellationToken ct = default);
}
