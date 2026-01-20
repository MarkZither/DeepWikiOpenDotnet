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
    Task<IReadOnlyList<Models.VectorQueryResult>> QueryAsync(float[] embedding, int k, Dictionary<string, string>? filters = null, CancellationToken cancellationToken = default);

    Task UpsertAsync(Models.DocumentDto document, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task RebuildIndexAsync(CancellationToken cancellationToken = default);
}
