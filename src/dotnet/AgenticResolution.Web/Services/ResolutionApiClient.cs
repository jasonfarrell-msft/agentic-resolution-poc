using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgenticResolution.Web.Services;

/// <summary>
/// SSE client for the Python Resolution API.
/// Streams stage progress events from POST /resolve.
/// </summary>
public sealed class ResolutionApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _httpClient;

    public ResolutionApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public bool IsConfigured => _httpClient.BaseAddress != null;

    /// <summary>
    /// Calls POST /resolve and yields SSE events as they arrive.
    /// </summary>
    public async IAsyncEnumerable<ResolutionEvent> StreamResolutionAsync(
        string ticketNumber,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new { ticket_number = ticketNumber }, JsonOptions);
        var request = new HttpRequestMessage(HttpMethod.Post, "resolve")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.ParseAdd("text/event-stream");

        using var response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (!line.StartsWith("data: ", StringComparison.Ordinal))
            {
                continue;
            }

            var json = line["data: ".Length..];
            if (string.IsNullOrWhiteSpace(json))
            {
                continue;
            }

            ResolutionEvent? evt;
            try
            {
                evt = JsonSerializer.Deserialize<ResolutionEvent>(json, JsonOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            if (evt is not null)
            {
                yield return evt;
            }
        }
    }
}

public sealed record ResolutionEvent
{
    public string Stage { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? Timestamp { get; init; }
    public JsonElement? Result { get; init; }
    public string? Error { get; init; }
}
