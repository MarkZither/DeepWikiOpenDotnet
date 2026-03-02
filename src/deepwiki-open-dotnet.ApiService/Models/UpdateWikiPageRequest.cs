namespace DeepWiki.ApiService.Models;

/// <summary>
/// Request body for PUT /api/wiki/{id}/pages/{pageId} — updates mutable fields of a wiki page.
/// All fields are optional; only non-null fields are applied.
/// </summary>
public class UpdateWikiPageRequest
{
    /// <summary>New page title. Leave null to keep existing value.</summary>
    public string? Title { get; set; }

    /// <summary>New Markdown content. Leave null to keep existing value.</summary>
    public string? Content { get; set; }

    /// <summary>New section path. Leave null to keep existing value.</summary>
    public string? SectionPath { get; set; }

    /// <summary>New sort order. Leave null to keep existing value.</summary>
    public int? SortOrder { get; set; }

    /// <summary>
    /// Replacement list of related page IDs.
    /// When provided, replaces all existing related-page links.
    /// Leave null to keep existing related pages unchanged.
    /// </summary>
    public IReadOnlyList<Guid>? RelatedPageIds { get; set; }
}
