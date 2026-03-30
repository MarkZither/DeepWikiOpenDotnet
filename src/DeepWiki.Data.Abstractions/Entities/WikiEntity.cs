namespace DeepWiki.Data.Abstractions.Entities;

/// <summary>
/// Represents a generated wiki project associated with a document collection.
/// </summary>
public class WikiEntity
{
    public Guid Id { get; set; }

    /// <summary>
    /// The identifier of the document collection this wiki was generated from.
    /// </summary>
    public string CollectionId { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the wiki (immutable after creation).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional description of the wiki's purpose or contents.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Current lifecycle/generation status of this wiki.
    /// </summary>
    public WikiStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public ICollection<WikiPageEntity> Pages { get; set; } = new List<WikiPageEntity>();
}
