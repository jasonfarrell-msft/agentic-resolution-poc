using AgenticResolution.Api.Data;
using AgenticResolution.Api.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace AgenticResolution.Api.Api;

public record KnowledgeArticleResponse(Guid Id, string Number, string Title, string Category,
    string Author, string? Tags, int ViewCount, DateTime CreatedAt, DateTime UpdatedAt)
{
    public static KnowledgeArticleResponse From(KnowledgeArticle a) =>
        new(a.Id, a.Number, a.Title, a.Category, a.Author, a.Tags, a.ViewCount, a.CreatedAt, a.UpdatedAt);
}

public record KnowledgeArticleDetailResponse(Guid Id, string Number, string Title, string Body,
    string Category, string Author, string? Tags, int ViewCount, DateTime CreatedAt, DateTime UpdatedAt)
{
    public static KnowledgeArticleDetailResponse From(KnowledgeArticle a) =>
        new(a.Id, a.Number, a.Title, a.Body, a.Category, a.Author, a.Tags, a.ViewCount, a.CreatedAt, a.UpdatedAt);
}

public static class KnowledgeBaseEndpoints
{
    public static IEndpointRouteBuilder MapKnowledgeBaseApi(this IEndpointRouteBuilder app)
    {
        var endpoints = app.MapGroup("/api/kb").WithTags("KnowledgeBase");
        endpoints.MapGet("/categories", GetCategoriesAsync);
        endpoints.MapGet("/{number}", GetByNumberAsync);
        endpoints.MapGet("/", ListAsync);
        return app;
    }

    private static async Task<Ok<PagedResponse<KnowledgeArticleResponse>>> ListAsync(
        AppDbContext db, string? q, string? category, int page = 1, int pageSize = 20,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        IQueryable<KnowledgeArticle> query = db.KnowledgeArticles.AsNoTracking()
            .Where(a => a.IsPublished);

        if (!string.IsNullOrWhiteSpace(q))
        {
            // Split into individual words so multi-word queries match articles
            // containing any of the words, not just the exact phrase
            var words = q.Trim().ToLower()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct()
                .ToArray();

            foreach (var word in words)
            {
                var w = word; // capture for lambda
                query = query.Where(a =>
                    a.Title.ToLower().Contains(w) ||
                    a.Body.ToLower().Contains(w) ||
                    (a.Tags != null && a.Tags.ToLower().Contains(w)));
            }
        }

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(a => a.Category == category);

        int total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(a => a.Number)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => KnowledgeArticleResponse.From(a))
            .ToListAsync(ct);

        return TypedResults.Ok(new PagedResponse<KnowledgeArticleResponse>(items, page, pageSize, total));
    }

    private static async Task<Results<Ok<KnowledgeArticleDetailResponse>, NotFound>> GetByNumberAsync(
        string number, AppDbContext db, CancellationToken ct)
    {
        var article = await db.KnowledgeArticles
            .FirstOrDefaultAsync(a => a.Number == number && a.IsPublished, ct);

        if (article is null) return TypedResults.NotFound();

        article.ViewCount++;
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(KnowledgeArticleDetailResponse.From(article));
    }

    private static async Task<Ok<string[]>> GetCategoriesAsync(
        AppDbContext db, CancellationToken ct)
    {
        var categories = await db.KnowledgeArticles.AsNoTracking()
            .Where(a => a.IsPublished)
            .Select(a => a.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToArrayAsync(ct);

        return TypedResults.Ok(categories);
    }
}
