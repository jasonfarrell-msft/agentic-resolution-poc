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
        var encodedNumber = Uri.EscapeDataString(number);
        var response = await _httpClient.GetAsync($"api/tickets/{encodedNumber}/details", cancellationToken);
        return await ReadRequiredAsync<TicketDetailResponse>(response, cancellationToken);
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

    public async Task<TicketResponse> AbandonWorkflowAsync(
        string number, CancellationToken cancellationToken = default)
    {
        var encodedNumber = Uri.EscapeDataString(number);
        var response = await _httpClient.PostAsync($"api/tickets/{encodedNumber}/abandon", null, cancellationToken);
        return await ReadRequiredAsync<TicketResponse>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetArticleCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync("api/kb/categories", cancellationToken);
        return await ReadRequiredAsync<List<string>>(response, cancellationToken);
    }

    private static async Task<T> ReadRequiredAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var detail = string.IsNullOrWhiteSpace(body)
                ? response.ReasonPhrase
                : body;
            throw new HttpRequestException(
                $"Tickets API returned {(int)response.StatusCode} for {response.RequestMessage?.RequestUri}: {detail}");
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (!string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var preview = body.Length > 300 ? body[..300] + "..." : body;
            throw new HttpRequestException(
                $"Tickets API returned {(int)response.StatusCode} with content type '{mediaType ?? "<none>"}' for {response.RequestMessage?.RequestUri}; expected application/json. Response preview: {preview}");
        }

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
    public string? Status { get; init; }
    public string? AssignedTo { get; init; }
    public string? Assignee { get; init; }
    public string? Caller { get; init; }
    public string? ResolutionNotes { get; init; }
    public string? AgentAction { get; init; }
    public double? AgentConfidence { get; init; }
    public string? MatchedTicketNumber { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

public sealed record CommentResponse
{
    public Guid Id { get; init; }
    public string Author { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public bool IsInternal { get; init; }
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
    OnHold = 2,
    Resolved = 3,
    Closed = 4,
    Cancelled = 5,
    Escalated = 6
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
