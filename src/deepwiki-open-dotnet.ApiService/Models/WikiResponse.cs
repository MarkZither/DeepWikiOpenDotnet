using DeepWiki.Data.Abstractions.Entities;

namespace DeepWiki.ApiService.Models;

/// <summary>
/// Full wiki response including all pages.
/// Returned by POST /api/wiki and GET /api/wiki/{id}.
/// </summary>
public class WikiResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string CollectionId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public IReadOnlyList<WikiPageResponse> Pages { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Maps a <see cref="WikiEntity"/> and its related-page lookup to a response DTO.
    /// </summary>
    public static WikiResponse FromEntity(
        WikiEntity entity,
        IDictionary<Guid, IReadOnlyList<WikiPageEntity>>? relatedPagesMap = null)
    {
        return new WikiResponse
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            CollectionId = entity.CollectionId,
            Status = entity.Status.ToString(),
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            Pages = entity.Pages
                .OrderBy(p => p.SortOrder)
                .Select(p => WikiPageResponse.FromEntity(
                    p,
                    relatedPagesMap?.TryGetValue(p.Id, out var rp) == true ? rp : null))
                .ToList()
        };
    }
}
