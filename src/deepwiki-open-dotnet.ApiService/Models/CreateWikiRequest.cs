using System.ComponentModel.DataAnnotations;

namespace DeepWiki.ApiService.Models;

/// <summary>
/// Request body for POST /api/wiki — creates a new wiki with optional initial pages.
/// </summary>
public class CreateWikiRequest
{
    /// <summary>Display name of the wiki. Immutable after creation.</summary>
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Identifier of the document collection this wiki is based on.</summary>
    [Required]
    public string CollectionId { get; set; } = string.Empty;

    /// <summary>Optional description of the wiki's purpose or contents.</summary>
    public string? Description { get; set; }

    /// <summary>Optional initial pages to create along with the wiki.</summary>
    public IReadOnlyList<CreateWikiPageRequest> Pages { get; set; } = [];
}
