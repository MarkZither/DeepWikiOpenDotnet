using System.ComponentModel.DataAnnotations;

namespace DeepWiki.ApiService.Models;

/// <summary>
/// Request body for POST /api/wiki/generate.
/// Maps to <see cref="DeepWiki.Rag.Core.Models.WikiGenerationRequest"/> after validation.
/// </summary>
public class GenerateWikiRequest
{
    /// <summary>Collection (repo scope) to generate the wiki from.</summary>
    [Required]
    public string CollectionId { get; set; } = string.Empty;

    /// <summary>Display name for the new wiki (max 200 chars).</summary>
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional description for the wiki.</summary>
    public string? Description { get; set; }
}
