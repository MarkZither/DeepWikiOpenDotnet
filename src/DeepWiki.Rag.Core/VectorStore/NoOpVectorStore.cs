using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DeepWiki.Data.Abstractions;
using DeepWiki.Data.Abstractions.Models;

namespace DeepWiki.Rag.Core.VectorStore;

public class NoOpVectorStore : IVectorStore
{
    public Task<IReadOnlyList<VectorQueryResult>> QueryAsync(float[] embedding, int k, Dictionary<string, string>? filters = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult((IReadOnlyList<VectorQueryResult>)Array.Empty<VectorQueryResult>());
    }

    public Task UpsertAsync(DocumentDto document, CancellationToken cancellationToken = default)
    {
        // No-op for default registration during early bootstrapping
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task RebuildIndexAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
