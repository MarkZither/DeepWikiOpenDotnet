using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DeepWiki.Data.Abstractions.Models;

namespace DeepWiki.Data.Abstractions;

/// <summary>
/// Microsoft Agent Framework-compatible document ingestion service for orchestrating
/// document chunking, embedding, and vector store upsert operations.
/// Supports batch ingestion with duplicate detection and concurrent write handling.
/// All result types are JSON-serializable for agent context passing.
/// </summary>
public interface IDocumentIngestionService
{
    /// <summary>
    /// Ingests a batch of documents by chunking, embedding, and upserting them to the vector store.
    /// Continues processing remaining documents if individual documents fail.
    /// </summary>
    /// <param name="request">The ingestion request containing documents and configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing success/failure counts and any errors encountered.</returns>
    Task<IngestionResult> IngestAsync(IngestionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts a single document to the vector store with atomic transaction semantics.
    /// If a document with the same RepoUrl and FilePath exists, it will be updated.
    /// </summary>
    /// <param name="document">The document to upsert.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The upserted document with generated/updated IDs and timestamps.</returns>
    /// <exception cref="ArgumentException">Thrown when document is invalid (missing required fields).</exception>
    Task<DocumentDto> UpsertAsync(DocumentDto document, CancellationToken cancellationToken = default);

    /// <summary>
    /// Chunks text and generates embeddings for each chunk.
    /// Validates that chunks respect the token limit before embedding.
    /// </summary>
    /// <param name="text">The text to chunk and embed.</param>
    /// <param name="maxTokensPerChunk">Maximum tokens per chunk (default: 8192).</param>
    /// <param name="parentDocumentId">Optional parent document ID for chunk metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of chunk results containing text, embedding, and metadata.</returns>
    Task<IReadOnlyList<ChunkEmbeddingResult>> ChunkAndEmbedAsync(
        string text,
        int maxTokensPerChunk = 8192,
        Guid? parentDocumentId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of chunking and embedding a piece of text. JSON-serializable for agent context.
/// </summary>
public sealed class ChunkEmbeddingResult
{
    /// <summary>
    /// The text content of this chunk.
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// The embedding vector for this chunk (typically 1536 dimensions).
    /// </summary>
    public float[] Embedding { get; init; } = Array.Empty<float>();

    /// <summary>
    /// Zero-based index of this chunk within the parent document.
    /// </summary>
    public int ChunkIndex { get; init; }

    /// <summary>
    /// The ID of the parent document this chunk was derived from.
    /// </summary>
    public Guid? ParentDocumentId { get; init; }

    /// <summary>
    /// The number of tokens in this chunk.
    /// </summary>
    public int TokenCount { get; init; }

    /// <summary>
    /// Detected or inferred language of the text.
    /// </summary>
    public string Language { get; init; } = "en";

    /// <summary>
    /// The embedding latency in milliseconds for this chunk.
    /// </summary>
    public long EmbeddingLatencyMs { get; init; }
}
