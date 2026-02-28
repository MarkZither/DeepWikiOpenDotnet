using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DeepWiki.Data.Interfaces;

/// <summary>
/// Provides vector similarity search operations for document embeddings.
/// Enables semantic search across knowledge base documents with support for multiple database providers.
/// </summary>
public interface IPersistenceVectorStore
{
    /// <summary>
    /// Inserts a new document or updates an existing document (upsert semantics based on Id).
    /// </summary>
    /// <param name="document">Document entity with embedding vector. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>Task representing the async operation.</returns>
    /// <exception cref="ArgumentNullException">If document is null.</exception>
    /// <exception cref="ArgumentException">If embedding dimensions invalid (not 1536 when non-null).</exception>
    /// <exception cref="DbUpdateException">If database operation fails.</exception>
    /// <remarks>
    /// Behavior:
    /// - If document with same Id exists: Update all properties including embedding
    /// - If document does not exist: Insert new document
    /// - Automatically sets UpdatedAt to current UTC time
    /// - Validates embedding dimensions before database operation
    /// </remarks>
    Task UpsertAsync(
        DeepWiki.Data.Entities.DocumentEntity document,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries for the k nearest documents to the query embedding using cosine similarity.
    /// </summary>
    /// <param name="queryEmbedding">Query vector for similarity search. Must be exactly 1536 dimensions.</param>
    /// <param name="k">Number of nearest neighbors to return. Default 10. Must be >= 1.</param>
    /// <param name="repoUrlFilter">Optional filter to only search within a specific repository URL.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>
    /// List of k nearest documents ordered by cosine similarity (closest first).
    /// If fewer than k documents exist or match the filter, returns all available results.
    /// Returns empty list if no documents with embeddings exist.
    /// </returns>
    /// <exception cref="ArgumentNullException">If queryEmbedding is null.</exception>
    /// <exception cref="ArgumentException">If queryEmbedding is not 1536 dimensions or k < 1.</exception>
    /// <remarks>
    /// Cosine distance formula: distance = 1 - (dot_product / (norm_a * norm_b))
    /// Range: [0, 2] where 0 = identical, 1 = orthogonal, 2 = opposite.
    /// 
    /// Database implementations:
    /// - SQL Server: Uses VECTOR_DISTANCE('cosine', ...) function
    /// - PostgreSQL: Uses &lt;=&gt; cosine distance operator
    /// 
    /// Performance: With HNSW indexing, expects &lt;500ms @ 10K docs, &lt;2s @ 3M docs.
    /// </remarks>
    Task<List<DeepWiki.Data.Entities.DocumentEntity>> QueryNearestAsync(
        ReadOnlyMemory<float> queryEmbedding,
        int k = 10,
        string? repoUrlFilter = null,
        string? filePathFilter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a document by its unique identifier.
    /// </summary>
    /// <param name="id">Document unique identifier.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>Task representing the async operation.</returns>
    /// <exception cref="InvalidOperationException">If document with given id does not exist.</exception>
    /// <remarks>
    /// Deletion is permanent (no soft delete). Uses immediate physical deletion.
    /// </remarks>
    Task DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all chunks for a specific (repoUrl, filePath) pair.
    /// Used before re-ingestion to purge stale chunks when chunk count changes.
    /// </summary>
    /// <param name="repoUrl">Repository URL. Must not be null or empty.</param>
    /// <param name="filePath">File path within the repository. Must not be null or empty.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>Task representing the async operation.</returns>
    /// <remarks>
    /// Idempotent: if no rows exist for the pair, succeeds silently.
    /// </remarks>
    Task DeleteChunksAsync(
        string repoUrl,
        string filePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all documents belonging to a specific repository.
    /// </summary>
    /// <param name="repoUrl">Repository URL to match for deletion. Uses exact string match (case-sensitive).</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>Task representing the async operation.</returns>
    /// <exception cref="ArgumentException">If repoUrl is null or empty.</exception>
    /// <remarks>
    /// Uses batch deletion for performance. All matching documents deleted in single transaction.
    /// If no documents match the repository URL, succeeds without error (idempotent).
    /// </remarks>
    Task DeleteByRepoAsync(
        string repoUrl,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts total documents, optionally filtered by repository.
    /// </summary>
    /// <param name="repoUrlFilter">Optional repository URL filter. If null, counts all documents.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>Count of documents matching filter.</returns>
    /// <remarks>
    /// Includes all documents regardless of embedding presence (null embeddings counted).
    /// </remarks>
    Task<int> CountAsync(
        string? repoUrlFilter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts a batch of documents in a single atomic transaction.
    /// </summary>
    /// <param name="documents">Collection of documents to insert or update. Must not be null or empty.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>Task representing the async operation.</returns>
    /// <exception cref="ArgumentNullException">If documents collection is null.</exception>
    /// <exception cref="ArgumentException">If documents collection is empty or any document has invalid embedding dimensions (not 1536 when non-null).</exception>
    /// <exception cref="DbUpdateException">If database operation fails.</exception>
    /// <remarks>
    /// Behavior:
    /// - All documents in the collection are inserted or updated atomically in a single transaction
    /// - If document with same Id exists: Update all properties including embedding
    /// - If document does not exist: Insert new document
    /// - Automatically sets CreatedAt and UpdatedAt timestamps
    /// - All-or-nothing semantics: If any document fails validation or database operation, entire batch is rolled back
    /// - Validates all embedding dimensions before any database operations
    /// Performance: Optimized for bulk operations with batching. Expects batch sizes up to 10,000 documents.
    /// </remarks>
    Task BulkUpsertAsync(
        IEnumerable<DeepWiki.Data.Entities.DocumentEntity> documents,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs provider-specific index maintenance, such as rebuilding columnstore or pgvector indexes.
    /// Implementations MAY perform a no-op when maintenance is not required.
    /// </summary>
    Task RebuildIndexAsync(CancellationToken cancellationToken = default);
}
