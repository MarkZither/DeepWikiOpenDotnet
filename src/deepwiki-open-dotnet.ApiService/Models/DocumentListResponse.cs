namespace DeepWiki.ApiService.Models;

/// <summary>
/// Paginated list of documents.
/// </summary>
public sealed record DocumentListResponse
{
    /// <summary>
    /// Documents in current page.
    /// </summary>
    public required IReadOnlyList<DocumentSummary> Items { get; init; }

    /// <summary>
    /// Total number of documents matching filters.
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// Current page number (1-based).
    /// </summary>
    public int Page { get; init; }

    /// <summary>
    /// Items per page.
    /// </summary>
    public int PageSize { get; init; }

    /// <summary>
    /// Total number of pages.
    /// </summary>
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}

/// <summary>
/// Document summary for list views (excludes full text and embedding).
/// </summary>
public sealed record DocumentSummary
{
    public Guid Id { get; init; }
    public required string RepoUrl { get; init; }
    public required string FilePath { get; init; }
    public required string Title { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public int TokenCount { get; init; }
    public string? FileType { get; init; }
    public bool IsCode { get; init; }
}
