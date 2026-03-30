namespace DeepWiki.Data.Abstractions.Entities;

/// <summary>
/// Represents a single page within a <see cref="WikiEntity"/>.
/// </summary>
public class WikiPageEntity
{
    public Guid Id { get; set; }

    /// <summary>
    /// Foreign key to the owning <see cref="WikiEntity"/>.
    /// </summary>
    public Guid WikiId { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Full Markdown content of the page.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Hierarchical section path, e.g. "Introduction/Overview".
    /// </summary>
    public string SectionPath { get; set; } = string.Empty;

    /// <summary>
    /// Zero-based ordering within the wiki (lower values appear first).
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Optional FK to a parent page enabling tree structures within a section.
    /// </summary>
    public Guid? ParentPageId { get; set; }

    /// <summary>
    /// Current status of this page.
    /// </summary>
    public PageStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public WikiEntity Wiki { get; set; } = null!;
    public WikiPageEntity? ParentPage { get; set; }
    public ICollection<WikiPageEntity> ChildPages { get; set; } = new List<WikiPageEntity>();
    public ICollection<WikiPageRelation> SourceRelations { get; set; } = new List<WikiPageRelation>();
    public ICollection<WikiPageRelation> TargetRelations { get; set; } = new List<WikiPageRelation>();
}
