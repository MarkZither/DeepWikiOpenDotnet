namespace DeepWiki.ApiService.Models;

/// <summary>
/// Response model for batch document ingestion.
/// </summary>
public sealed record IngestResponse
{
    /// <summary>
    /// Number of documents successfully ingested.
    /// </summary>
    public int SuccessCount { get; init; }

    /// <summary>
    /// Number of documents that failed to ingest.
    /// </summary>
    public int FailureCount { get; init; }

    /// <summary>
    /// Total chunks created from ingested documents.
    /// </summary>
    public int TotalChunks { get; init; }

    /// <summary>
    /// Processing duration in milliseconds.
    /// </summary>
    public long DurationMs { get; init; }

    /// <summary>
    /// IDs of successfully ingested documents.
    /// </summary>
    public IReadOnlyList<Guid> IngestedDocumentIds { get; init; } = [];

    /// <summary>
    /// Details of any ingestion errors.
    /// </summary>
    public IReadOnlyList<IngestError> Errors { get; init; } = [];
}

/// <summary>
/// Error details for a failed document ingestion.
/// </summary>
public sealed record IngestError
{
    /// <summary>
    /// Identifier for the failed document (repoUrl:filePath).
    /// </summary>
    public required string DocumentIdentifier { get; init; }

    /// <summary>
    /// Error message describing the failure.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Processing stage where failure occurred.
    /// </summary>
    public required string Stage { get; init; }
}
