namespace DeepWiki.Data.Abstractions.Entities;

/// <summary>
/// Represents a directional "related page" link between two <see cref="WikiPageEntity"/> records.
/// The composite primary key is (SourcePageId, TargetPageId).
/// </summary>
public class WikiPageRelation
{
    /// <summary>
    /// FK to the page that declares the relation (part of composite PK).
    /// </summary>
    public Guid SourcePageId { get; set; }

    /// <summary>
    /// FK to the page being related to (part of composite PK).
    /// </summary>
    public Guid TargetPageId { get; set; }

    // Navigation
    public WikiPageEntity SourcePage { get; set; } = null!;
    public WikiPageEntity TargetPage { get; set; } = null!;
}
