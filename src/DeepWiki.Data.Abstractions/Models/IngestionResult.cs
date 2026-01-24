using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DeepWiki.Data.Abstractions.Models;

/// <summary>
/// Result model for document ingestion operations. JSON-serializable for agent context passing.
/// </summary>
public sealed class IngestionResult
{
    /// <summary>
    /// Gets or sets the number of successfully ingested documents.
    /// </summary>
    [JsonPropertyName("successCount")]
    public int SuccessCount { get; init; }

    /// <summary>
    /// Gets or sets the number of failed document ingestions.
    /// </summary>
    [JsonPropertyName("failureCount")]
    public int FailureCount { get; init; }

    /// <summary>
    /// Gets or sets the total number of chunks created across all documents.
    /// </summary>
    [JsonPropertyName("totalChunks")]
    public int TotalChunks { get; init; }

    /// <summary>
    /// Gets or sets the list of errors encountered during ingestion.
    /// </summary>
    [JsonPropertyName("errors")]
    public IReadOnlyList<IngestionError> Errors { get; init; } = [];

    /// <summary>
    /// Gets or sets the total duration of the ingestion operation in milliseconds.
    /// </summary>
    [JsonPropertyName("durationMs")]
    public long DurationMs { get; init; }

    /// <summary>
    /// Gets or sets the IDs of successfully ingested documents.
    /// </summary>
    [JsonPropertyName("ingestedDocumentIds")]
    public IReadOnlyList<Guid> IngestedDocumentIds { get; init; } = [];

    /// <summary>
    /// Gets the total number of documents processed (success + failure).
    /// </summary>
    [JsonIgnore]
    public int TotalProcessed => SuccessCount + FailureCount;

    /// <summary>
    /// Gets whether all documents were successfully ingested.
    /// </summary>
    [JsonIgnore]
    public bool IsFullySuccessful => FailureCount == 0 && SuccessCount > 0;

    /// <summary>
    /// Gets whether all documents failed to ingest.
    /// </summary>
    [JsonIgnore]
    public bool IsFullyFailed => SuccessCount == 0 && FailureCount > 0;

    /// <summary>
    /// Gets the ingestion rate in documents per second.
    /// </summary>
    [JsonIgnore]
    public double DocumentsPerSecond => DurationMs > 0 ? SuccessCount / (DurationMs / 1000.0) : 0;

    /// <summary>
    /// Creates a successful result for a single document.
    /// </summary>
    public static IngestionResult Success(Guid documentId, int chunkCount, long durationMs) =>
        new()
        {
            SuccessCount = 1,
            FailureCount = 0,
            TotalChunks = chunkCount,
            DurationMs = durationMs,
            IngestedDocumentIds = [documentId]
        };

    /// <summary>
    /// Creates a failed result for a single document.
    /// </summary>
    public static IngestionResult Failure(string documentIdentifier, string errorMessage, long durationMs) =>
        new()
        {
            SuccessCount = 0,
            FailureCount = 1,
            DurationMs = durationMs,
            Errors = [new IngestionError { DocumentIdentifier = documentIdentifier, ErrorMessage = errorMessage }]
        };

    /// <summary>
    /// Creates an empty result.
    /// </summary>
    public static IngestionResult Empty => new();
}

/// <summary>
/// Represents an error encountered during document ingestion. JSON-serializable for agent context.
/// </summary>
public sealed class IngestionError
{
    /// <summary>
    /// Gets or sets the identifier of the document that failed (RepoUrl:FilePath or ID).
    /// </summary>
    [JsonPropertyName("documentIdentifier")]
    public string DocumentIdentifier { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the error message describing what went wrong.
    /// </summary>
    [JsonPropertyName("errorMessage")]
    public string ErrorMessage { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the exception type if an exception was thrown.
    /// </summary>
    [JsonPropertyName("exceptionType")]
    public string? ExceptionType { get; init; }

    /// <summary>
    /// Gets or sets the stack trace if available (only in development).
    /// </summary>
    [JsonPropertyName("stackTrace")]
    public string? StackTrace { get; init; }

    /// <summary>
    /// Gets or sets the stage at which the error occurred (chunking, embedding, upsert).
    /// </summary>
    [JsonPropertyName("stage")]
    public IngestionStage Stage { get; init; }

    /// <summary>
    /// Gets or sets whether this error is retryable.
    /// </summary>
    [JsonPropertyName("isRetryable")]
    public bool IsRetryable { get; init; }

    /// <summary>
    /// Creates an error from an exception.
    /// </summary>
    public static IngestionError FromException(string documentIdentifier, Exception exception, IngestionStage stage, bool includeStackTrace = false) =>
        new()
        {
            DocumentIdentifier = documentIdentifier,
            ErrorMessage = exception.Message,
            ExceptionType = exception.GetType().Name,
            StackTrace = includeStackTrace ? exception.StackTrace : null,
            Stage = stage,
            IsRetryable = IsExceptionRetryable(exception)
        };

    private static bool IsExceptionRetryable(Exception ex) =>
        ex is TimeoutException or HttpRequestException or OperationCanceledException;
}

/// <summary>
/// The stage at which an ingestion error occurred.
/// </summary>
public enum IngestionStage
{
    /// <summary>
    /// Error during document validation.
    /// </summary>
    Validation,

    /// <summary>
    /// Error during text chunking.
    /// </summary>
    Chunking,

    /// <summary>
    /// Error during embedding generation.
    /// </summary>
    Embedding,

    /// <summary>
    /// Error during vector store upsert.
    /// </summary>
    Upsert,

    /// <summary>
    /// Unknown or unspecified stage.
    /// </summary>
    Unknown
}
