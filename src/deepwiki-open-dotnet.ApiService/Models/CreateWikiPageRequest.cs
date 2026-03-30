using System.ComponentModel.DataAnnotations;

namespace DeepWiki.ApiService.Models;

/// <summary>
/// Request body for POST /api/wiki/{id}/pages — adds a new page to an existing wiki.
/// </summary>
public class CreateWikiPageRequest
{
    /// <summary>Page title (required, displayed in sidebar navigation).</summary>
    [Required]
    [MaxLength(500)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Full Markdown content of the page (required).</summary>
    [Required]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Hierarchical section path, e.g. "Architecture/Data Model".
    /// Used to organise pages in the sidebar tree.
    /// </summary>
    [Required]
    public string SectionPath { get; set; } = string.Empty;

    /// <summary>
    /// Zero-based ordering within the wiki.
    /// Defaults to 0 if not specified; caller should provide sequential values.
    /// </summary>
    public int SortOrder { get; set; } = 0;

    /// <summary>Optional FK to a parent page enabling tree structures within a section.</summary>
    public Guid? ParentPageId { get; set; }

    /// <summary>IDs of related pages to link to this page on creation.</summary>
    public IReadOnlyList<Guid> RelatedPageIds { get; set; } = [];
}
