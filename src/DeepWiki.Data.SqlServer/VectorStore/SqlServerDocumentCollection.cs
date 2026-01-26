using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DeepWiki.Data.Abstractions.Models;
using DeepWiki.Data.Abstractions.VectorData;
using DeepWiki.Data.Entities;
using DeepWiki.Data.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;

namespace DeepWiki.Data.SqlServer.VectorStore;

/// <summary>
/// SQL Server implementation of IDocumentVectorCollection using Microsoft.Extensions.VectorData patterns.
/// Wraps the existing IPersistenceVectorStore to provide VectorData-compliant operations.
/// </summary>
public sealed class SqlServerDocumentCollection : IDocumentVectorCollection
{
    private readonly IPersistenceVectorStore _persistenceStore;
    private readonly ILogger<SqlServerDocumentCollection>? _logger;

    /// <summary>
    /// Gets the name of this collection.
    /// </summary>
    public string CollectionName { get; }

    /// <summary>
    /// Creates a new SqlServerDocumentCollection.
    /// </summary>
    /// <param name="persistenceStore">The underlying persistence vector store.</param>
    /// <param name="collectionName">The name of the collection.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public SqlServerDocumentCollection(
        IPersistenceVectorStore persistenceStore,
        string collectionName,
        ILogger<SqlServerDocumentCollection>? logger = null)
    {
        _persistenceStore = persistenceStore ?? throw new ArgumentNullException(nameof(persistenceStore));
        CollectionName = collectionName ?? throw new ArgumentNullException(nameof(collectionName));
        _logger = logger;
    }

    #region Collection Management

    /// <inheritdoc />
    public Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        // SQL Server EF Core manages tables automatically via migrations.
        // The Documents table exists if the DbContext is configured correctly.
        // Return true since we're always working with the default "documents" collection.
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        // EF Core migrations handle table creation.
        // No-op since the persistence layer manages schema.
        _logger?.LogDebug("EnsureCollectionExistsAsync called for {Collection} - handled by EF Core migrations", CollectionName);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task EnsureCollectionDeletedAsync(CancellationToken cancellationToken = default)
    {
        // Deleting the collection would delete all data - not supported for safety.
        // Use DeleteByRepoAsync for scoped cleanup instead.
        throw new NotSupportedException(
            "Deleting the entire collection is not supported. Use repo-scoped deletion methods instead.");
    }

    #endregion

    #region CRUD Operations

    /// <inheritdoc />
    public Task<DocumentRecord?> GetAsync(Guid key, RecordRetrievalOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Use QueryNearestAsync with a dummy embedding and filter by ID is not ideal.
        // Since the persistence layer doesn't have a direct GetById, we'd need to add one
        // or use the repository. For now, this is a limitation we'll document.
        
        // TODO: Consider adding GetByIdAsync to IPersistenceVectorStore
        _logger?.LogWarning("GetAsync by key not directly supported - consider using repository layer for single record retrieval");
        return Task.FromResult<DocumentRecord?>(null);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<DocumentRecord> GetBatchAsync(
        IEnumerable<Guid> keys, 
        RecordRetrievalOptions? options = null, 
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Similar limitation as GetAsync
        _logger?.LogWarning("GetBatchAsync by keys not directly supported - consider using repository layer");
        await Task.CompletedTask; // Satisfy async requirement
        yield break;
    }

    /// <inheritdoc />
    public async Task<Guid> UpsertAsync(DocumentRecord record, CancellationToken cancellationToken = default)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));

        var entity = MapToEntity(record);
        await _persistenceStore.UpsertAsync(entity, cancellationToken);
        
        return entity.Id;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<Guid> UpsertBatchAsync(
        IEnumerable<DocumentRecord> records, 
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (records == null) throw new ArgumentNullException(nameof(records));

        var entities = new List<DocumentEntity>();
        foreach (var record in records)
        {
            entities.Add(MapToEntity(record));
        }

        await _persistenceStore.BulkUpsertAsync(entities, cancellationToken);

        foreach (var entity in entities)
        {
            yield return entity.Id;
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid key, CancellationToken cancellationToken = default)
    {
        await _persistenceStore.DeleteAsync(key, cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteBatchAsync(IEnumerable<Guid> keys, CancellationToken cancellationToken = default)
    {
        if (keys == null) throw new ArgumentNullException(nameof(keys));

        foreach (var key in keys)
        {
            await _persistenceStore.DeleteAsync(key, cancellationToken);
        }
    }

    #endregion

    #region Vector Search

    /// <inheritdoc />
    public async IAsyncEnumerable<VectorSearchResult<DocumentRecord>> SearchAsync(
        ReadOnlyMemory<float> vector,
        int top = 10,
        VectorSearchOptions<DocumentRecord>? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (vector.IsEmpty) throw new ArgumentException("Vector cannot be empty", nameof(vector));
        if (vector.Length != 1536) throw new ArgumentException("Vector must be 1536 dimensions", nameof(vector));
        if (top < 1) throw new ArgumentException("top must be >= 1", nameof(top));

        // Extract filters if provided - VectorSearchOptions uses LINQ expressions
        // For now, we support simple scenarios via convention
        string? repoUrlFilter = null;
        string? filePathFilter = null;

        // Query using the persistence store
        var results = await _persistenceStore.QueryNearestAsync(
            vector,
            top,
            repoUrlFilter,
            filePathFilter,
            cancellationToken);

        foreach (var entity in results)
        {
            // VectorSearchResult uses constructor pattern: (record, score)
            // Score is null since our persistence layer doesn't return distance metrics
            yield return new VectorSearchResult<DocumentRecord>(MapToRecord(entity), null);
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<VectorSearchResult<DocumentRecord>> SearchByRepoAsync(
        ReadOnlyMemory<float> vector,
        string repoUrlPrefix,
        int top = 10,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (vector.IsEmpty) throw new ArgumentException("Vector cannot be empty", nameof(vector));
        if (vector.Length != 1536) throw new ArgumentException("Vector must be 1536 dimensions", nameof(vector));
        if (string.IsNullOrWhiteSpace(repoUrlPrefix)) throw new ArgumentException("repoUrlPrefix cannot be empty", nameof(repoUrlPrefix));
        if (top < 1) throw new ArgumentException("top must be >= 1", nameof(top));

        // Use prefix filter - note security validation happens in the persistence layer
        var filterPattern = repoUrlPrefix.EndsWith('%') ? repoUrlPrefix : repoUrlPrefix + "%";

        var results = await _persistenceStore.QueryNearestAsync(
            vector,
            top,
            filterPattern,
            null,
            cancellationToken);

        foreach (var entity in results)
        {
            yield return new VectorSearchResult<DocumentRecord>(MapToRecord(entity), null);
        }
    }

    #endregion

    #region Mapping

    private static DocumentRecord MapToRecord(DocumentEntity entity)
    {
        return new DocumentRecord
        {
            Id = entity.Id,
            RepoUrl = entity.RepoUrl,
            FilePath = entity.FilePath,
            Title = entity.Title ?? string.Empty,
            Text = entity.Text,
            Embedding = entity.Embedding,
            MetadataJson = entity.MetadataJson ?? "{}",
            FileType = entity.FileType ?? string.Empty,
            IsCode = entity.IsCode,
            IsImplementation = entity.IsImplementation,
            TokenCount = entity.TokenCount,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }

    private static DocumentEntity MapToEntity(DocumentRecord record)
    {
        return new DocumentEntity
        {
            Id = record.Id == Guid.Empty ? Guid.NewGuid() : record.Id,
            RepoUrl = record.RepoUrl,
            FilePath = record.FilePath,
            Title = record.Title,
            Text = record.Text,
            Embedding = record.Embedding,
            MetadataJson = record.MetadataJson,
            FileType = record.FileType,
            IsCode = record.IsCode,
            IsImplementation = record.IsImplementation,
            TokenCount = record.TokenCount,
            CreatedAt = record.CreatedAt == default ? DateTime.UtcNow : record.CreatedAt,
            UpdatedAt = DateTime.UtcNow
        };
    }

    #endregion
}
