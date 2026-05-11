using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using TicketsApi.McpServer.Models;
using TicketsApi.McpServer.Services;

namespace TicketsApi.McpServer.Tools;

[McpServerToolType]
public sealed class KnowledgeBaseTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static string Serialize(object value) =>
        JsonSerializer.Serialize(value, JsonOptions);

    [McpServerTool(Name = "search_kb")]
    [Description("Searches the IT knowledge base for articles matching the given keywords. Returns a list of matching articles with titles, categories, and tags — but NOT the full body text. Use get_kb_article to retrieve the full content of a specific article.")]
    public static async Task<string> SearchKbAsync(
        [Description("Search query, e.g. 'password reset' or 'VPN timeout'.")]
        string query,
        IKbApiClient kb,
        ILogger<KnowledgeBaseTools> logger,
        [Description("Optional category filter, e.g. 'Network', 'Email', 'Security'.")]
        string? category = null,
        [Description("Max results to return (1–20). Default: 10.")]
        int page_size = 10,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "Error: query is required.";

        page_size = page_size == 0 ? 10 : Math.Clamp(page_size, 1, 20);

        try
        {
            var result = await kb.SearchAsync(query.Trim(), category, page_size, cancellationToken);
            return Serialize(new { result.Items, result.Total });
        }
        catch (TicketApiException ex)
        {
            logger.LogError(ex, "Failed to search KB articles");
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool(Name = "get_kb_article")]
    [Description("Retrieves the full content of a single KB knowledge base article by its number (e.g. KB0001001). Returns the complete article including the body text with step-by-step instructions. Use this after search_kb identifies a relevant article.")]
    public static async Task<string> GetKbArticleAsync(
        [Description("The KB article number to retrieve, e.g. KB0001001.")]
        string article_number,
        IKbApiClient kb,
        ILogger<KnowledgeBaseTools> logger,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(article_number))
            return "Error: article_number is required.";

        try
        {
            var article = await kb.GetByNumberAsync(article_number.Trim(), cancellationToken);
            return article is null
                ? $"Error: KB article '{article_number}' not found."
                : Serialize(article);
        }
        catch (TicketApiException ex)
        {
            logger.LogError(ex, "Failed to get KB article {Number}", article_number);
            return $"Error: {ex.Message}";
        }
    }
}
