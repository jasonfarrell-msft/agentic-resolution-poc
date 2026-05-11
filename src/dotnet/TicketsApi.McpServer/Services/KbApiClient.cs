using System.Net.Http.Json;
using System.Text.Json;
using TicketsApi.McpServer.Models;

namespace TicketsApi.McpServer.Services;

public interface IKbApiClient
{
    Task<PagedResult<KbArticleModel>> SearchAsync(string query, string? category, int pageSize, CancellationToken cancellationToken);
    Task<KbArticleDetailModel?> GetByNumberAsync(string number, CancellationToken cancellationToken);
}

public class KbApiClientImpl : IKbApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;

    public KbApiClientImpl(HttpClient http) => _http = http;

    public async Task<PagedResult<KbArticleModel>> SearchAsync(string query, string? category, int pageSize, CancellationToken cancellationToken)
    {
        var url = $"/api/kb?q={Uri.EscapeDataString(query)}&pageSize={pageSize}";
        if (!string.IsNullOrEmpty(category)) url += $"&category={Uri.EscapeDataString(category)}";
        var response = await _http.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<PagedResult<KbArticleModel>>(JsonOptions, cancellationToken)
            ?? new PagedResult<KbArticleModel>();
    }

    public async Task<KbArticleDetailModel?> GetByNumberAsync(string number, CancellationToken cancellationToken)
    {
        var response = await _http.GetAsync($"/api/kb/{number}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<KbArticleDetailModel>(JsonOptions, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync();
        throw new TicketApiException($"API returned {(int)response.StatusCode}: {body}", (int)response.StatusCode);
    }
}
