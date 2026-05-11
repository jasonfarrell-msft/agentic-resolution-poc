namespace TicketsApi.McpServer.Models;

public class KbArticleModel
{
    public Guid Id { get; set; }
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Category { get; set; }
    public string? Author { get; set; }
    public string? Tags { get; set; }
    public int ViewCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class KbArticleDetailModel : KbArticleModel
{
    public string? Body { get; set; }
}
