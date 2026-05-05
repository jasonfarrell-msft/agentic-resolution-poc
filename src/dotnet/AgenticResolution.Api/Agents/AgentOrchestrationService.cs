using AgenticResolution.Api.Models;
using System.ComponentModel.DataAnnotations;

namespace AgenticResolution.Api.Agents;

public sealed class AgentOrchestrationService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<AgentOrchestrationService> _logger;

    public AgentOrchestrationService(IHttpClientFactory httpClientFactory, IConfiguration config,
        ILogger<AgentOrchestrationService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("agents");
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Orchestrate agent pipeline for a ticket with progress tracking.
    /// Mirrors Python workflow structure: Classifier → Fetch → Decomposer → Evaluator → Resolution/Escalation.
    /// </summary>
    public async Task<AgentPipelineResult> ProcessTicketAsync(string ticketNumber, Guid? runId = null,
        IWorkflowProgressTracker? progress = null, CancellationToken ct = default)
    {
        const string ClassifierExecutor = "ClassifierExecutor";
        const string IncidentFetchExecutor = "IncidentFetchExecutor";
        const string IncidentDecomposerExecutor = "IncidentDecomposerExecutor";
        const string EvaluatorExecutor = "EvaluatorExecutor";
        const string ResolutionExecutor = "ResolutionExecutor";
        const string EscalationExecutor = "EscalationExecutor";

        string incidentUrl = _config["Agents:IncidentUrl"]
            ?? throw new InvalidOperationException("Agents:IncidentUrl not configured.");

        // Stage 1: Classification (simplified - all tickets routed to incident for now)
        if (progress is not null && runId.HasValue)
            await progress.ExecutorStartedAsync(runId.Value, ClassifierExecutor, ct);

        _logger.LogInformation("Routing ticket {TicketNumber} directly to incident agent", ticketNumber);

        if (progress is not null && runId.HasValue)
        {
            await progress.ExecutorRoutedAsync(runId.Value, ClassifierExecutor, "incident", ct);
            await progress.ExecutorCompletedAsync(runId.Value, ClassifierExecutor, ct);
        }

        // Stage 2: Incident fetch + decomposition (combined in current agent)
        if (progress is not null && runId.HasValue)
        {
            await progress.ExecutorStartedAsync(runId.Value, IncidentFetchExecutor, ct);
            await progress.ExecutorCompletedAsync(runId.Value, IncidentFetchExecutor, ct);
            await progress.ExecutorStartedAsync(runId.Value, IncidentDecomposerExecutor, ct);
        }

        var result = await CallAgentAsync<ResolutionResult>(
            incidentUrl.TrimEnd('/') + "/process", new { ticketNumber }, ct);

        if (progress is not null && runId.HasValue)
            await progress.ExecutorCompletedAsync(runId.Value, IncidentDecomposerExecutor, ct);

        // Stage 3: Evaluator (confidence check)
        if (progress is not null && runId.HasValue)
        {
            await progress.ExecutorStartedAsync(runId.Value, EvaluatorExecutor, ct);
            await progress.ExecutorOutputAsync(runId.Value, EvaluatorExecutor,
                $"Confidence: {result.Confidence:F2}, Action: {result.Action}", ct);
            await progress.ExecutorCompletedAsync(runId.Value, EvaluatorExecutor, ct);
        }

        // Stage 4: Resolution or Escalation
        string finalExecutor = result.Action?.Contains("escalate", StringComparison.OrdinalIgnoreCase) == true
            ? EscalationExecutor
            : ResolutionExecutor;

        if (progress is not null && runId.HasValue)
        {
            await progress.ExecutorStartedAsync(runId.Value, finalExecutor, ct);
            await progress.ExecutorOutputAsync(runId.Value, finalExecutor, result.Notes ?? string.Empty, ct);
            await progress.ExecutorCompletedAsync(runId.Value, finalExecutor, ct);
        }

        _logger.LogInformation(
            "Incident agent completed for {TicketNumber}: action={Action} confidence={Confidence:F2}",
            ticketNumber, result.Action, result.Confidence);

        return new AgentPipelineResult(
            Classification: "incident",
            ClassificationConfidence: 1.0,
            Action: result.Action ?? "unknown",
            ResolutionConfidence: result.Confidence,
            Notes: result.Notes ?? string.Empty,
            MatchedTicketNumber: result.MatchedTicketNumber);
    }

    private async Task<T> CallAgentAsync<T>(string url, object request, CancellationToken ct)
    {
        using var response = await _httpClient.PostAsJsonAsync(url, request, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<T>(ct);
        return result ?? throw new InvalidOperationException($"Agent at {url} returned empty response.");
    }
}

public sealed record ClassificationResult(
    [property: System.Text.Json.Serialization.JsonPropertyName("classification")] string? Classification,
    [property: System.Text.Json.Serialization.JsonPropertyName("confidence")] double Confidence,
    [property: System.Text.Json.Serialization.JsonPropertyName("rationale")] string? Rationale);

public sealed record ResolutionResult(
    [property: System.Text.Json.Serialization.JsonPropertyName("action")] string? Action,
    [property: System.Text.Json.Serialization.JsonPropertyName("confidence")] double Confidence,
    [property: System.Text.Json.Serialization.JsonPropertyName("notes")] string? Notes,
    [property: System.Text.Json.Serialization.JsonPropertyName("matchedTicketNumber")] string? MatchedTicketNumber);

public sealed record AgentPipelineResult(
    string Classification,
    double ClassificationConfidence,
    string Action,
    double ResolutionConfidence,
    string Notes,
    string? MatchedTicketNumber);

public record AgentRunResult(bool Success, string Action, float Confidence, string? Notes,
    string? MatchedTicketNumber, string? Classification = null);
