using System.ComponentModel.DataAnnotations;

namespace DeepWiki.ApiService.Models;

/// <summary>
/// Request model for semantic search queries.
/// </summary>
public sealed record QueryRequest
{
    /// <summary>
    /// Natural language search query text.
    /// </summary>
    /// <example>How do I implement authentication in ASP.NET Core?</example>
    [Required]
    [StringLength(10000, MinimumLength = 1)]
    public required string Query { get; init; }

    /// <summary>
    /// Maximum number of results to return (default: 10, max: 100).
    /// </summary>
    [Range(1, 100)]
    public int K { get; init; } = 10;

    /// <summary>
    /// Optional filters to narrow search results.
    /// </summary>
    public QueryFilters? Filters { get; init; }

    /// <summary>
    /// Whether to include full document text in results (default: true).
    /// </summary>
    public bool IncludeFullText { get; init; } = true;
}

/// <summary>
/// Filters for narrowing semantic search results.
/// </summary>
public sealed record QueryFilters
{
    /// <summary>
    /// Filter by repository URL (exact match or SQL LIKE pattern).
    /// </summary>
    public string? RepoUrl { get; init; }

    /// <summary>
    /// Filter by file path (SQL LIKE pattern supported).
    /// </summary>
    public string? FilePath { get; init; }
}
