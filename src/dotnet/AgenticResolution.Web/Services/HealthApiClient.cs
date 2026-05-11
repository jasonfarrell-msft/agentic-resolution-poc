using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgenticResolution.Web.Services;

public sealed class HealthApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _httpClient;

    public HealthApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public bool IsConfigured => _httpClient.BaseAddress != null;

    public async Task<HealthResponse?> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("api/health", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<HealthResponse>(JsonOptions, cancellationToken);
        }
        catch
        {
            return null;
        }
    }
}

public sealed record HealthResponse
{
    public string Status { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; }
    public DatabaseHealth? Database { get; init; }
}

public sealed record DatabaseHealth
{
    public string Status { get; init; } = string.Empty;
    public TicketCounts? TicketCounts { get; init; }
    public int TotalTickets { get; init; }
    public int TotalKbArticles { get; init; }
}

public sealed record TicketCounts
{
    public int New { get; init; }
    public int InProgress { get; init; }
    public int OnHold { get; init; }
    public int Resolved { get; init; }
    public int Closed { get; init; }
    public int Cancelled { get; init; }
    public int Escalated { get; init; }
}
