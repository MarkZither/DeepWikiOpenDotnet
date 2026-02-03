using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DeepWiki.Data.Interfaces;

/// <summary>
/// Provides CRUD (Create, Read, Update, Delete) operations for document entities.
/// Complements IVectorStore by handling standard database operations like retrieval by ID and listing.
/// </summary>
public interface IDocumentRepository
{
    /// <summary>
    /// Retrieves a document by its unique identifier.
    /// </summary>
    /// <param name="id">Document unique identifier.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>
    /// Document entity if found, including all properties (embedding, metadata, etc.).
    /// Returns null if document does not exist (not an exception).
    /// </returns>
    /// <remarks>
    /// Performance: &lt;100ms for local database (primary key lookup).
    /// Uses index on Id (clustered index/primary key).
    /// </remarks>
    Task<DeepWiki.Data.Entities.DocumentEntity?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all documents belonging to a repository with pagination support.
    /// </summary>
    /// <param name="repoUrl">Repository URL filter. Uses exact string match (case-sensitive).</param>
    /// <param name="skip">Number of documents to skip for pagination (default 0).</param>
    /// <param name="take">Number of documents to take. Maximum 1000 to prevent excessive memory usage.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>
    /// List of documents ordered by CreatedAt descending (newest first).
    /// Returns empty list if no documents match the repository URL.
    /// </returns>
    /// <exception cref="ArgumentException">If repoUrl is null/empty or take > 1000.</exception>
    /// <remarks>
    /// Performance: &lt;500ms for 10K document repository (with index on RepoUrl).
    /// Supports cursor-based pagination via skip/take parameters.
    /// 
    /// Example pagination:
    /// // Get first 100
    /// var page1 = await repo.GetByRepoAsync(url, skip: 0, take: 100);
    /// // Get next 100
    /// var page2 = await repo.GetByRepoAsync(url, skip: 100, take: 100);
    /// </remarks>
    Task<List<DeepWiki.Data.Entities.DocumentEntity>> GetByRepoAsync(
        string repoUrl,
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists documents with optional repository filter and returns total count for pagination.
    /// </summary>
    /// <param name="repoUrl">Optional repository URL filter. When null, lists all documents.</param>
    /// <param name="skip">Number of documents to skip for pagination (default 0).</param>
    /// <param name="take">Number of documents to take. Maximum 1000.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>Tuple of (Items, TotalCount).</returns>
    Task<(List<DeepWiki.Data.Entities.DocumentEntity> Items, int TotalCount)> ListAsync(
        string? repoUrl = null,
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new document to the database.
    /// </summary>
    /// <param name="document">Document entity to add. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>Task representing the async operation.</returns>
    /// <exception cref="ArgumentNullException">If document is null.</exception>
    /// <exception cref="DbUpdateException">If a document with the same Id already exists.</exception>
    /// <remarks>
    /// Automatically sets CreatedAt and UpdatedAt to current UTC time.
    /// Uses client-generated Guid for Id (not database-generated).
    /// </remarks>
    Task AddAsync(
        DeepWiki.Data.Entities.DocumentEntity document,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing document in the database.
    /// </summary>
    /// <param name="document">Document entity with updated properties. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>Task representing the async operation.</returns>
    /// <exception cref="ArgumentNullException">If document is null.</exception>
    /// <exception cref="InvalidOperationException">If document does not exist.</exception>
    /// <exception cref="DbUpdateConcurrencyException">If document was modified concurrently.</exception>
    /// <remarks>
    /// Automatically updates UpdatedAt to current UTC time.
    /// Uses optimistic concurrency via UpdatedAt timestamp (concurrency token).
    /// CreatedAt is never modified (read-only).
    /// </remarks>
    Task UpdateAsync(
        DeepWiki.Data.Entities.DocumentEntity document,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a document from the database by its unique identifier.
    /// </summary>
    /// <param name="id">Document unique identifier.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>Task representing the async operation.</returns>
    /// <exception cref="InvalidOperationException">If document does not exist.</exception>
    /// <remarks>
    /// Deletion is permanent (no soft delete). Uses immediate physical deletion.
    /// Performance: &lt;100ms for single document deletion.
    /// </remarks>
    Task DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a document exists by its unique identifier.
    /// </summary>
    /// <param name="id">Document unique identifier.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>True if document exists, false otherwise.</returns>
    /// <remarks>
    /// Lightweight query (no document data loaded, uses EXISTS semantics).
    /// Performance: &lt;50ms for local database (primary key lookup).
    /// </remarks>
    Task<bool> ExistsAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
