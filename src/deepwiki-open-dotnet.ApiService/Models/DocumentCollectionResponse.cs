using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DeepWiki.ApiService.Models;

/// <summary>
/// Response from GET /api/documents/collections.
/// Each entry represents one repository with a count of its indexed files.
/// Matches the DocumentListResponse.schema.json contract expected by the Web UI.
/// </summary>
public sealed record DocumentCollectionResponse
{
    [JsonPropertyName("collections")]
    public required IReadOnlyList<DocumentCollectionSummary> Collections { get; init; }

    [JsonPropertyName("total_count")]
    public int TotalCount { get; init; }
}

/// <summary>
/// Summary of a single document collection (one repo URL).
/// </summary>
public sealed record DocumentCollectionSummary
{
    /// <summary>
    /// The repository URL used as the unique identifier for the collection.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// Display name derived from the repository URL (last path segment).
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// Number of indexed files (ChunkIndex == 0 rows) in this collection.
    /// </summary>
    [JsonPropertyName("document_count")]
    public int DocumentCount { get; init; }
}
