using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace DeepWiki.ApiService.Models;

/// <summary>
/// Request model for batch document ingestion.
/// </summary>
public sealed record IngestRequest
{
    /// <summary>
    /// Documents to ingest into the vector store.
    /// </summary>
    [Required]
    [MinLength(1)]
    [MaxLength(1000)]
    public required IReadOnlyList<IngestDocument> Documents { get; init; }

    /// <summary>
    /// Continue processing if individual documents fail (default: true).
    /// </summary>
    public bool ContinueOnError { get; init; } = true;

    /// <summary>
    /// Batch size for parallel processing (default: 10).
    /// </summary>
    [Range(1, 50)]
    public int BatchSize { get; init; } = 10;
}

/// <summary>
/// Single document for ingestion.
/// </summary>
public sealed record IngestDocument
{
    /// <summary>
    /// Source repository URL.
    /// </summary>
    [Required]
    public required string RepoUrl { get; init; }

    /// <summary>
    /// File path within repository.
    /// </summary>
    [Required]
    public required string FilePath { get; init; }

    /// <summary>
    /// Document title.
    /// </summary>
    [Required]
    public required string Title { get; init; }

    /// <summary>
    /// Full document text content.
    /// </summary>
    [Required]
    [StringLength(5_000_000)] // 5MB text limit per constitution
    public required string Text { get; init; }

    /// <summary>
    /// Optional metadata as JSON object.
    /// </summary>
    public JsonElement? Metadata { get; init; }
}
