using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using DeepWiki.Data.Abstractions.VectorData;
using DeepWiki.Data.Interfaces;
using Microsoft.Extensions.Logging;

namespace DeepWiki.Data.SqlServer.VectorStore;

/// <summary>
/// SQL Server implementation of IDocumentVectorStore using Microsoft.Extensions.VectorData patterns.
/// Acts as a factory for document collections backed by SQL Server 2025 vector capabilities.
/// </summary>
public sealed class SqlServerDocumentVectorStore : IDocumentVectorStore
{
    private readonly IPersistenceVectorStore _persistenceStore;
    private readonly ILoggerFactory? _loggerFactory;

    private const string DefaultCollectionName = "documents";

    /// <summary>
    /// Creates a new SqlServerDocumentVectorStore.
    /// </summary>
    /// <param name="persistenceStore">The underlying persistence vector store.</param>
    /// <param name="loggerFactory">Optional logger factory for creating collection loggers.</param>
    public SqlServerDocumentVectorStore(
        IPersistenceVectorStore persistenceStore,
        ILoggerFactory? loggerFactory = null)
    {
        _persistenceStore = persistenceStore ?? throw new System.ArgumentNullException(nameof(persistenceStore));
        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    public IDocumentVectorCollection GetDocumentCollection(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new System.ArgumentException("Collection name cannot be null or empty", nameof(name));

        var logger = _loggerFactory?.CreateLogger<SqlServerDocumentCollection>();
        return new SqlServerDocumentCollection(_persistenceStore, name, logger);
    }

    /// <inheritdoc />
    public IDocumentVectorCollection GetDocumentCollection()
    {
        return GetDocumentCollection(DefaultCollectionName);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> ListCollectionNamesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // SQL Server EF Core model has a single Documents table.
        // Multiple "collections" could be simulated via repo URL prefixes,
        // but the physical storage is one collection.
        yield return DefaultCollectionName;
    }
}
