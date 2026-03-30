using DeepWiki.Data.Abstractions.Entities;

namespace DeepWiki.ApiService.Models;

/// <summary>
/// Lightweight wiki summary for list views.
/// Returned by GET /api/wiki/projects and PUT /api/wiki/{id}.
/// </summary>
public class WikiSummaryResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>The raw CollectionId value (source of the wiki's content).</summary>
    public string CollectionSource { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int PageCount { get; set; }
    public DateTime LastModified { get; set; }

    /// <summary>Maps a <see cref="WikiEntity"/> to a summary response DTO.</summary>
    public static WikiSummaryResponse FromEntity(WikiEntity entity)
    {
        return new WikiSummaryResponse
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            CollectionSource = entity.CollectionId,
            Status = entity.Status.ToString(),
            PageCount = entity.Pages.Count,
            LastModified = entity.UpdatedAt
        };
    }
}
