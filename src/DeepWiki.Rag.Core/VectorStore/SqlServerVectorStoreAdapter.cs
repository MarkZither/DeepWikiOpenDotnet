using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DeepWiki.Data.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace DeepWiki.Rag.Core.VectorStore;

/// <summary>
/// Shim adapter — provider-specific adapter has been moved to the provider layer (`DeepWiki.Data.SqlServer`).
/// This shim intentionally throws to make the migration explicit if used.
/// </summary>
public class SqlServerVectorStoreAdapter : DeepWiki.Data.Abstractions.IVectorStore
{
    private readonly ILogger<SqlServerVectorStoreAdapter> _logger;

    public SqlServerVectorStoreAdapter(ILogger<SqlServerVectorStoreAdapter> logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<VectorQueryResult>> QueryAsync(float[] embedding, int k, Dictionary<string, string>? filters = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("SqlServerVectorStoreAdapter has been moved to DeepWiki.Data.SqlServer.VectorStore.SqlServerVectorStoreAdapter. Register that adapter via AddSqlServerDataLayer().");
    }

    public Task UpsertAsync(DeepWiki.Data.Abstractions.Models.DocumentDto document, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("SqlServerVectorStoreAdapter has been moved to DeepWiki.Data.SqlServer.VectorStore.SqlServerVectorStoreAdapter. Register that adapter via AddSqlServerDataLayer().");
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("SqlServerVectorStoreAdapter has been moved to DeepWiki.Data.SqlServer.VectorStore.SqlServerVectorStoreAdapter. Register that adapter via AddSqlServerDataLayer().");
    }

    public Task DeleteChunksAsync(string repoUrl, string filePath, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("SqlServerVectorStoreAdapter has been moved to DeepWiki.Data.SqlServer.VectorStore.SqlServerVectorStoreAdapter. Register that adapter via AddSqlServerDataLayer().");
    }

    public Task RebuildIndexAsync(CancellationToken cancellationToken = default)
    {
        _logger?.LogWarning("RebuildIndexAsync shim called — use provider adapter for actual maintenance.");
        return Task.CompletedTask;
    }
}