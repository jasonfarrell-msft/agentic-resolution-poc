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
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new { ticket_number = ticketNumber }, JsonOptions);
        var request = new HttpRequestMessage(HttpMethod.Post, "resolve")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.ParseAdd("text/event-stream");

        using var response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var detail = string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase : body;
            throw new InvalidOperationException($"Resolution API returned {(int)response.StatusCode}: {detail}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var data = new StringBuilder();
        string? eventType = null;

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (line.Length == 0)
            {
                if (TryReadEvent(data, eventType, out var sseEvent))
                {
                    yield return sseEvent;
                }

                data.Clear();
                eventType = null;
                continue;
            }

            if (line.StartsWith(':'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf(':', StringComparison.Ordinal);
            var field = separatorIndex >= 0 ? line[..separatorIndex] : line;
            var value = separatorIndex >= 0 ? line[(separatorIndex + 1)..] : string.Empty;
            if (value.StartsWith(' '))
            {
                value = value[1..];
            }

            if (field.Equals("event", StringComparison.OrdinalIgnoreCase))
            {
                eventType = value;
                continue;
            }

            if (!field.Equals("data", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (data.Length > 0)
            {
                data.AppendLine();
            }

            data.Append(value);
        }

        if (TryReadEvent(data, eventType, out var finalEvent))
        {
            yield return finalEvent;
        }
    }

    private static bool TryReadEvent(StringBuilder data, string? eventType, out ResolutionEvent sseEvent)
    {
        sseEvent = default!;
        if (data.Length == 0)
        {
            if (string.IsNullOrWhiteSpace(eventType))
            {
                return false;
            }

            sseEvent = new ResolutionEvent { Event = eventType };
            return true;
        }

        var json = data.ToString();
        if (string.IsNullOrWhiteSpace(json) || json.Equals("[DONE]", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            sseEvent = JsonSerializer.Deserialize<ResolutionEvent>(json, JsonOptions) ?? new ResolutionEvent();
            if (string.IsNullOrWhiteSpace(sseEvent.Event) && !string.IsNullOrWhiteSpace(eventType))
            {
                sseEvent = sseEvent with { Event = eventType };
            }

            return true;
        }
        catch (JsonException)
        {
            sseEvent = new ResolutionEvent
            {
                Stage = "stream",
                Status = "warning",
                Message = "The resolution stream sent an event the UI could not parse."
            };
            return true;
        }
    }
}

public sealed record ResolutionEvent
{
    public string? Stage { get; init; }
    public string? Status { get; init; }
    public string? State { get; init; }
    public string? Event { get; init; }
    public string? Timestamp { get; init; }
    public JsonElement? Result { get; init; }
    public string? Error { get; init; }
    public string? Message { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; init; }

    public string DisplayStage =>
        FirstText(Stage, AdditionalData, "step", "name", "phase") ?? "workflow";

    public string EffectiveStatus =>
        FirstText(Status, AdditionalData, "status", "state", "event", "type") ??
        FirstText(State, AdditionalData, "state") ??
        FirstText(Event, AdditionalData, "event") ??
        "received";

    public string? DisplayMessage =>
        Error ?? Message ?? FirstText(null, AdditionalData, "detail", "message", "error");

    public bool IsTerminal =>
        IsTerminalStatus(EffectiveStatus) ||
        (AdditionalData?.TryGetValue("terminal", out var terminal) == true &&
            terminal.ValueKind is JsonValueKind.True);

    public bool IsFailure => IsFailureStatus(EffectiveStatus);

    private static string? FirstText(string? preferred, IReadOnlyDictionary<string, JsonElement>? additionalData, params string[] names)
    {
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            return preferred;
        }

        if (additionalData is null)
        {
            return null;
        }

        foreach (var name in names)
        {
            if (additionalData.TryGetValue(name, out var value))
            {
                var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return null;
    }

    private static bool IsTerminalStatus(string status) =>
        Normalize(status) is "complete" or "completed" or "done" or "resolved" or "success" or "succeeded" or "escalated" or "failed" or "failure" or "error" or "finished" or "finish";

    private static bool IsFailureStatus(string status) =>
        Normalize(status) is "failed" or "failure" or "error";

    private static string Normalize(string value) =>
        value.Trim().Replace("-", "_", StringComparison.Ordinal).Replace(" ", "_", StringComparison.Ordinal).ToLowerInvariant();
}
