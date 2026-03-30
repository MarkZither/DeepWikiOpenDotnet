using DeepWiki.Data.Abstractions.Entities;

namespace DeepWiki.ApiService.Models;

/// <summary>
/// Single wiki page response including related page summaries.
/// Returned by GET /api/wiki/{id}/pages/{pageId} and embedded in WikiResponse.
/// </summary>
public class WikiPageResponse
{
    public Guid Id { get; set; }
    public Guid WikiId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string SectionPath { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public Guid? ParentPageId { get; set; }
    public string Status { get; set; } = string.Empty;
    public IReadOnlyList<RelatedPageSummary> RelatedPages { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Maps a <see cref="WikiPageEntity"/> to a response DTO.</summary>
    public static WikiPageResponse FromEntity(
        WikiPageEntity entity,
        IReadOnlyList<WikiPageEntity>? relatedPages = null)
    {
        return new WikiPageResponse
        {
            Id = entity.Id,
            WikiId = entity.WikiId,
            Title = entity.Title,
            Content = entity.Content,
            SectionPath = entity.SectionPath,
            SortOrder = entity.SortOrder,
            ParentPageId = entity.ParentPageId,
            Status = entity.Status.ToString(),
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            RelatedPages = relatedPages?
                .Select(rp => new RelatedPageSummary { Id = rp.Id, Title = rp.Title })
                .ToList() ?? []
        };
    }
}

/// <summary>Lightweight related-page reference embedded in page responses.</summary>
public class RelatedPageSummary
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
}
