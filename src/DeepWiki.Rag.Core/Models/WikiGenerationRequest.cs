using System.ComponentModel.DataAnnotations;

namespace DeepWiki.Rag.Core.Models;

/// <summary>
/// Input model for <see cref="Services.IWikiGenerationService.GenerateAsync"/>.
/// </summary>
public class WikiGenerationRequest
{
    /// <summary>
    /// The collection identifier used to scope RAG retrieval.
    /// Must match an ingested document collection.
    /// </summary>
    [Required]
    public string CollectionId { get; set; } = string.Empty;

    /// <summary>
    /// Display name for the generated wiki.
    /// Must be non-empty and at most 200 characters.
    /// </summary>
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional description for the generated wiki.</summary>
    public string? Description { get; set; }
}
