namespace AgenticResolution.Api.Models;

public class KnowledgeArticle
{
    public Guid Id { get; set; }
    public string Number { get; set; } = string.Empty;   // e.g. KB0001001
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;     // plain text / markdown
    public string Category { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string? Tags { get; set; }                    // comma-separated
    public int ViewCount { get; set; }
    public bool IsPublished { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
