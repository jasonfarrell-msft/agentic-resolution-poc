using System.Net.Http.Json;
using System.Text.Json;
using TicketsApi.McpServer.Models;

namespace TicketsApi.McpServer.Services;

public interface ITicketApiClient
{
    Task<TicketModel?> GetByNumberAsync(string number, CancellationToken cancellationToken);
    Task<PagedResult<TicketModel>> ListAsync(string? state, int page, int pageSize, CancellationToken cancellationToken);
    Task<PagedResult<TicketModel>> SearchAsync(string query, string? state, int pageSize, CancellationToken cancellationToken);
    Task<TicketModel?> UpdateAsync(Guid id, UpdateTicketRequest request, CancellationToken cancellationToken);
}

public class TicketApiClientImpl : ITicketApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;

    public TicketApiClientImpl(HttpClient http) => _http = http;

    public async Task<TicketModel?> GetByNumberAsync(string number, CancellationToken cancellationToken)
    {
        var response = await _http.GetAsync($"/api/tickets/{number}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<TicketModel>(JsonOptions, cancellationToken);
    }

    public async Task<PagedResult<TicketModel>> ListAsync(string? state, int page, int pageSize, CancellationToken cancellationToken)
    {
        var url = $"/api/tickets?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrEmpty(state)) url += $"&state={Uri.EscapeDataString(state)}";
        var response = await _http.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<PagedResult<TicketModel>>(JsonOptions, cancellationToken)
            ?? new PagedResult<TicketModel>();
    }

    public async Task<PagedResult<TicketModel>> SearchAsync(string query, string? state, int pageSize, CancellationToken cancellationToken)
    {
        var url = $"/api/tickets/search?q={Uri.EscapeDataString(query)}&pageSize={pageSize}";
        if (!string.IsNullOrEmpty(state)) url += $"&state={Uri.EscapeDataString(state)}";
        var response = await _http.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<PagedResult<TicketModel>>(JsonOptions, cancellationToken)
            ?? new PagedResult<TicketModel>();
    }

    public async Task<TicketModel?> UpdateAsync(Guid id, UpdateTicketRequest request, CancellationToken cancellationToken)
    {
        var response = await _http.PutAsJsonAsync($"/api/tickets/{id}", request, JsonOptions, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<TicketModel>(JsonOptions, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync();
        throw new TicketApiException($"API returned {(int)response.StatusCode}: {body}", (int)response.StatusCode);
    }
}
