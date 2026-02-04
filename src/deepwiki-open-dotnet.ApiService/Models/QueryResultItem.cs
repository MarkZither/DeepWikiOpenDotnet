using System.Text.Json;

namespace DeepWiki.ApiService.Models;

/// <summary>
/// Response model for semantic search queries.
/// Returns array of results directly (Python API parity).
/// </summary>
public sealed record QueryResultItem
{
    /// <summary>
    /// Unique document identifier.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Source repository URL.
    /// </summary>
    public required string RepoUrl { get; init; }

    /// <summary>
    /// File path within repository.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Document title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Full document text (included when includeFullText=true).
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// Cosine similarity score (0.0 to 1.0, higher is more similar).
    /// </summary>
    public float SimilarityScore { get; init; }

    /// <summary>
    /// Document metadata as JSON object.
    /// </summary>
    public JsonElement? Metadata { get; init; }
}
