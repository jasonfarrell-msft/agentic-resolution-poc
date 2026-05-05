using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;

namespace AgenticResolution.Web.Services;

public sealed class TicketApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _httpClient;

    public TicketApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public bool IsConfigured => _httpClient.BaseAddress != null;

    public async Task<PagedResponse<TicketResponse>> GetTicketsAsync(
        TicketFilter filter,
        CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["page"] = filter.Page.ToString(CultureInfo.InvariantCulture),
            ["pageSize"] = filter.PageSize.ToString(CultureInfo.InvariantCulture)
        };

        if (!string.IsNullOrWhiteSpace(filter.AssignedTo))
        {
            query["assignedTo"] = filter.AssignedTo;
        }

        if (filter.States.Count > 0)
        {
            query["state"] = string.Join(',', filter.States.Select(ToEnumValue));
        }

        if (!string.IsNullOrWhiteSpace(filter.Category))
        {
            query["category"] = filter.Category;
        }

        if (filter.Priorities.Count > 0)
        {
            query["priority"] = string.Join(',', filter.Priorities.Select(ToEnumValue));
        }

        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            query["q"] = filter.Query;
        }

        query["sort"] = filter.Sort == TicketSortField.Modified ? "modified" : "created";
        query["dir"] = filter.Direction == SortDirection.Asc ? "asc" : "desc";

        var url = QueryHelpers.AddQueryString("api/tickets", query);
        var response = await _httpClient.GetAsync(url, cancellationToken);
        return await ReadRequiredAsync<PagedResponse<TicketResponse>>(response, cancellationToken);
    }

    public async Task<TicketDetailResponse> GetTicketDetailsAsync(
        string number,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"api/tickets/{number}/details", cancellationToken);
        return await ReadRequiredAsync<TicketDetailResponse>(response, cancellationToken);
    }

    public async Task<StartResolveResponse> ResolveTicketAsync(
        string number,
        string? note,
        CancellationToken cancellationToken = default)
    {
        var payload = new ResolveTicketRequest(note);
        var response = await _httpClient.PostAsJsonAsync($"api/tickets/{number}/resolve", payload, JsonOptions, cancellationToken);
        return await ReadRequiredAsync<StartResolveResponse>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowRunEventResponse>> GetRunEventsAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"api/runs/{runId}/events", cancellationToken);
        var events = await ReadRequiredAsync<List<WorkflowRunEventResponse>>(response, cancellationToken);
        return events;
    }

    public async Task<WorkflowRunResponse> GetRunAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"api/runs/{runId}", cancellationToken);
        return await ReadRequiredAsync<WorkflowRunResponse>(response, cancellationToken);
    }

    public async Task<PagedResponse<KnowledgeArticleResponse>> GetArticlesAsync(
        string? query = null, string? category = null, int page = 1, int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var qs = new Dictionary<string, string?>
        {
            ["page"] = page.ToString(CultureInfo.InvariantCulture),
            ["pageSize"] = pageSize.ToString(CultureInfo.InvariantCulture)
        };
        if (!string.IsNullOrWhiteSpace(query)) qs["q"] = query;
        if (!string.IsNullOrWhiteSpace(category)) qs["category"] = category;
        var url = QueryHelpers.AddQueryString("api/kb", qs);
        var response = await _httpClient.GetAsync(url, cancellationToken);
        return await ReadRequiredAsync<PagedResponse<KnowledgeArticleResponse>>(response, cancellationToken);
    }

    public async Task<KnowledgeArticleDetailResponse> GetArticleAsync(
        string number, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"api/kb/{number}", cancellationToken);
        return await ReadRequiredAsync<KnowledgeArticleDetailResponse>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetArticleCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync("api/kb/categories", cancellationToken);
        return await ReadRequiredAsync<List<string>>(response, cancellationToken);
    }

    private static async Task<T> ReadRequiredAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        if (result is null)
        {
            throw new InvalidOperationException("API response was empty.");
        }

        return result;
    }

    private static string ToEnumValue<T>(T value) where T : struct, Enum => value.ToString();
}

public sealed class TicketFilter
{
    public string? AssignedTo { get; set; }

    public HashSet<TicketState> States { get; } = new();

    public string? Category { get; set; }

    public HashSet<TicketPriority> Priorities { get; } = new();

    public string? Query { get; set; }

    public TicketSortField Sort { get; set; } = TicketSortField.Created;

    public SortDirection Direction { get; set; } = SortDirection.Desc;

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 12;
}

public sealed record PagedResponse<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int Total { get; init; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)Total / PageSize);
}

public sealed record TicketDetailResponse
{
    public TicketResponse Ticket { get; init; } = new();
    public IReadOnlyList<CommentResponse> Comments { get; init; } = Array.Empty<CommentResponse>();
    public IReadOnlyList<WorkflowRunResponse> Runs { get; init; } = Array.Empty<WorkflowRunResponse>();
}

public sealed record TicketResponse
{
    public Guid Id { get; init; }
    public string Number { get; init; } = string.Empty;
    public string ShortDescription { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Category { get; init; }
    public TicketPriority Priority { get; init; }
    public TicketState State { get; init; }
    public string? AssignedTo { get; init; }
    public string? Caller { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? ModifiedAt { get; init; }
}

public sealed record CommentResponse
{
    public Guid Id { get; init; }
    public string Author { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public bool IsInternal { get; init; }
}

public sealed record StartResolveResponse
{
    public Guid RunId { get; init; }
    public string TicketNumber { get; init; } = string.Empty;
    public Guid TicketId { get; init; }
    public string StatusUrl { get; init; } = string.Empty;
    public string EventsUrl { get; init; } = string.Empty;
}

public sealed record ResolveTicketRequest(string? Note);

public sealed record WorkflowRunResponse
{
    public Guid Id { get; init; }
    public Guid TicketId { get; init; }
    public string TicketNumber { get; init; } = string.Empty;
    public WorkflowRunStatus Status { get; init; }
    public string? TriggeredBy { get; init; }
    public string? Note { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? FinalAction { get; init; }
    public double? FinalConfidence { get; init; }
    public IReadOnlyList<WorkflowRunEventResponse> Events { get; init; } = Array.Empty<WorkflowRunEventResponse>();
}

public sealed record WorkflowRunEventResponse
{
    public Guid Id { get; init; }
    public int Sequence { get; init; }
    public string? ExecutorId { get; init; }
    public string EventType { get; init; } = string.Empty;
    public string? Payload { get; init; }
    public DateTime Timestamp { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TicketPriority
{
    Critical = 1,
    High = 2,
    Moderate = 3,
    Low = 4
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TicketState
{
    New = 0,
    InProgress = 1,
    Resolved = 2,
    Closed = 3,
    Cancelled = 4
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WorkflowRunStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Escalated = 4
}

public enum TicketSortField
{
    Created = 0,
    Modified = 1
}

public enum SortDirection
{
    Asc = 0,
    Desc = 1
}

public sealed record KnowledgeArticleResponse
{
    public Guid Id { get; init; }
    public string Number { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public string? Tags { get; init; }
    public int ViewCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public sealed record KnowledgeArticleDetailResponse
{
    public Guid Id { get; init; }
    public string Number { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public string? Tags { get; init; }
    public int ViewCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}
