using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.VectorData;
using DeepWiki.Data.Abstractions.Models;

namespace DeepWiki.Data.Abstractions.VectorData;

/// <summary>
/// Document-specific vector store collection interface that extends
/// Microsoft.Extensions.VectorData patterns for our DocumentRecord model.
/// 
/// This interface combines the standard VectorStore collection operations with
/// search capabilities specifically designed for document retrieval.
/// </summary>
public interface IDocumentVectorCollection
{
    /// <summary>
    /// The name of the collection.
    /// </summary>
    string CollectionName { get; }

    #region Collection Management

    /// <summary>
    /// Checks if the collection exists in the vector store.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the collection exists, false otherwise.</returns>
    Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the collection if it doesn't exist.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the collection if it exists.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task EnsureCollectionDeletedAsync(CancellationToken cancellationToken = default);

    #endregion

    #region CRUD Operations

    /// <summary>
    /// Gets a document record by its key.
    /// </summary>
    /// <param name="key">The document ID.</param>
    /// <param name="options">Optional retrieval options (e.g., whether to include vectors).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The document record if found, null otherwise.</returns>
    Task<DocumentRecord?> GetAsync(Guid key, RecordRetrievalOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets multiple document records by their keys.
    /// </summary>
    /// <param name="keys">The document IDs.</param>
    /// <param name="options">Optional retrieval options (e.g., whether to include vectors).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of document records found.</returns>
    IAsyncEnumerable<DocumentRecord> GetBatchAsync(IEnumerable<Guid> keys, RecordRetrievalOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts a document record (insert if new, update if exists).
    /// </summary>
    /// <param name="record">The document record to upsert.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The key of the upserted record.</returns>
    Task<Guid> UpsertAsync(DocumentRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts multiple document records.
    /// </summary>
    /// <param name="records">The document records to upsert.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of keys for the upserted records.</returns>
    IAsyncEnumerable<Guid> UpsertBatchAsync(IEnumerable<DocumentRecord> records, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a document record by its key.
    /// </summary>
    /// <param name="key">The document ID to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteAsync(Guid key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes multiple document records by their keys.
    /// </summary>
    /// <param name="keys">The document IDs to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteBatchAsync(IEnumerable<Guid> keys, CancellationToken cancellationToken = default);

    #endregion

    #region Vector Search

    /// <summary>
    /// Performs a vector similarity search using the provided embedding.
    /// </summary>
    /// <param name="vector">The query embedding vector (1536 dimensions for OpenAI embeddings).</param>
    /// <param name="top">Maximum number of results to return.</param>
    /// <param name="options">Optional search options including filters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of search results with scores.</returns>
    IAsyncEnumerable<VectorSearchResult<DocumentRecord>> SearchAsync(
        ReadOnlyMemory<float> vector,
        int top = 10,
        VectorSearchOptions<DocumentRecord>? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a vector similarity search with a repository URL filter.
    /// This is a convenience method for common repository-scoped queries.
    /// </summary>
    /// <param name="vector">The query embedding vector.</param>
    /// <param name="repoUrlPrefix">Repository URL prefix to filter by (uses prefix matching).</param>
    /// <param name="top">Maximum number of results to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of search results with scores.</returns>
    IAsyncEnumerable<VectorSearchResult<DocumentRecord>> SearchByRepoAsync(
        ReadOnlyMemory<float> vector,
        string repoUrlPrefix,
        int top = 10,
        CancellationToken cancellationToken = default);

    #endregion
}
