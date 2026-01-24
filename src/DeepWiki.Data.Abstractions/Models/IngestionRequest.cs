using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DeepWiki.Data.Abstractions.Models;

/// <summary>
/// Request model for document ingestion operations. JSON-serializable for agent context passing.
/// </summary>
public sealed class IngestionRequest
{
    /// <summary>
    /// Gets or sets the documents to ingest.
    /// </summary>
    [JsonPropertyName("documents")]
    public IReadOnlyList<IngestionDocument> Documents { get; init; } = [];

    /// <summary>
    /// Gets or sets the batch size for embedding operations (default: 10, max: 100).
    /// </summary>
    [JsonPropertyName("batchSize")]
    public int BatchSize { get; init; } = 10;

    /// <summary>
    /// Gets or sets the maximum retry count for failed operations.
    /// </summary>
    [JsonPropertyName("maxRetries")]
    public int MaxRetries { get; init; } = 3;

    /// <summary>
    /// Gets or sets default metadata to apply to all documents if not specified per-document.
    /// </summary>
    [JsonPropertyName("metadataDefaults")]
    public Dictionary<string, string>? MetadataDefaults { get; init; }

    /// <summary>
    /// Gets or sets whether to continue processing remaining documents if one fails.
    /// Defaults to true for batch resilience.
    /// </summary>
    [JsonPropertyName("continueOnError")]
    public bool ContinueOnError { get; init; } = true;

    /// <summary>
    /// Gets or sets the maximum tokens per chunk (default: 8192 for embedding models).
    /// </summary>
    [JsonPropertyName("maxTokensPerChunk")]
    public int MaxTokensPerChunk { get; init; } = 8192;

    /// <summary>
    /// Gets or sets whether to skip embedding and only store text (for testing).
    /// </summary>
    [JsonPropertyName("skipEmbedding")]
    public bool SkipEmbedding { get; init; }

    /// <summary>
    /// Creates an empty ingestion request.
    /// </summary>
    public static IngestionRequest Empty => new();

    /// <summary>
    /// Creates an ingestion request for the specified documents.
    /// </summary>
    /// <param name="documents">The documents to ingest.</param>
    /// <returns>A new IngestionRequest instance.</returns>
    public static IngestionRequest Create(IEnumerable<IngestionDocument> documents) =>
        new() { Documents = [.. documents] };

    /// <summary>
    /// Creates an ingestion request for a single document.
    /// </summary>
    /// <param name="repoUrl">The repository URL.</param>
    /// <param name="filePath">The file path within the repository.</param>
    /// <param name="text">The document text content.</param>
    /// <param name="title">Optional document title.</param>
    /// <returns>A new IngestionRequest instance.</returns>
    public static IngestionRequest Create(string repoUrl, string filePath, string text, string? title = null) =>
        new()
        {
            Documents =
            [
                new IngestionDocument
                {
                    RepoUrl = repoUrl,
                    FilePath = filePath,
                    Text = text,
                    Title = title ?? System.IO.Path.GetFileName(filePath)
                }
            ]
        };
}

/// <summary>
/// Represents a document to be ingested. JSON-serializable for agent context passing.
/// </summary>
public sealed class IngestionDocument
{
    /// <summary>
    /// Gets or sets the optional document ID. If not provided, a new ID will be generated.
    /// If provided and document exists, it will be updated.
    /// </summary>
    [JsonPropertyName("id")]
    public Guid? Id { get; init; }

    /// <summary>
    /// Gets or sets the repository URL (required for duplicate detection).
    /// </summary>
    [JsonPropertyName("repoUrl")]
    public required string RepoUrl { get; init; }

    /// <summary>
    /// Gets or sets the file path within the repository (required for duplicate detection).
    /// </summary>
    [JsonPropertyName("filePath")]
    public required string FilePath { get; init; }

    /// <summary>
    /// Gets or sets the document title.
    /// </summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the document text content (required).
    /// </summary>
    [JsonPropertyName("text")]
    public required string Text { get; init; }

    /// <summary>
    /// Gets or sets optional metadata as JSON string.
    /// </summary>
    [JsonPropertyName("metadataJson")]
    public string MetadataJson { get; init; } = "{}";

    /// <summary>
    /// Gets or sets the file type (e.g., "cs", "md", "py"). Auto-detected from FilePath if not set.
    /// </summary>
    [JsonPropertyName("fileType")]
    public string? FileType { get; init; }

    /// <summary>
    /// Gets or sets whether this is code content. Auto-detected from FileType if not set.
    /// </summary>
    [JsonPropertyName("isCode")]
    public bool? IsCode { get; init; }

    /// <summary>
    /// Gets or sets whether this is implementation code (vs. test, config, docs).
    /// Auto-detected from FilePath if not set.
    /// </summary>
    [JsonPropertyName("isImplementation")]
    public bool? IsImplementation { get; init; }

    /// <summary>
    /// Gets or sets pre-computed embedding. If provided, embedding generation is skipped.
    /// </summary>
    [JsonPropertyName("embedding")]
    public float[]? Embedding { get; init; }
}
