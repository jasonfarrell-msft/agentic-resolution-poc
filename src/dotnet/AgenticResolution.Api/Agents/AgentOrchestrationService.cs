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

    public async Task<AgentPipelineResult> ProcessTicketAsync(string ticketNumber,
        CancellationToken ct = default)
    {
        string incidentUrl = _config["Agents:IncidentUrl"]
            ?? throw new InvalidOperationException("Agents:IncidentUrl not configured.");

        _logger.LogInformation("Routing ticket {TicketNumber} directly to incident agent", ticketNumber);

        var result = await CallAgentAsync<ResolutionResult>(
            incidentUrl.TrimEnd('/') + "/process", new { ticketNumber }, ct);

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
