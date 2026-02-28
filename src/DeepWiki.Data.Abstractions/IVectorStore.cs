using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DeepWiki.Data.Abstractions;

/// <summary>
/// Abstraction for vector store operations used by the agent framework.
/// Methods return JSON-serializable results and should surface errors via return values where possible
/// so agent tool loops are not broken by thrown exceptions.
/// </summary>
public interface IVectorStore
{
    /// <summary>
    /// Queries the vector store for the k most similar documents to the given embedding.
    /// </summary>
    /// <param name="embedding">The query embedding (typically 1536 dimensions).</param>
    /// <param name="k">The number of results to return.</param>
    /// <param name="filters">Optional metadata filters (e.g., "repoUrl", "filePath" with SQL LIKE patterns).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of query results ordered by similarity (most similar first).</returns>
    Task<IReadOnlyList<Models.VectorQueryResult>> QueryAsync(float[] embedding, int k, Dictionary<string, string>? filters = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts a document to the vector store. If a document with the same RepoUrl and FilePath exists, it is updated.
    /// </summary>
    /// <param name="document">The document to upsert.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpsertAsync(Models.DocumentDto document, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a document from the vector store by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the document to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all chunks for a specific (repoUrl, filePath) pair.
    /// Used before re-ingestion to purge stale chunks when chunk count changes.
    /// </summary>
    /// <param name="repoUrl">Repository URL.</param>
    /// <param name="filePath">File path within the repository.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteChunksAsync(string repoUrl, string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rebuilds the vector store index for optimal query performance.
    /// May be a no-op on some providers if indexing is automatic.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RebuildIndexAsync(CancellationToken cancellationToken = default);
}
