using System.Runtime.CompilerServices;
using DeepWiki.Data.Abstractions;
using DeepWiki.Data.Abstractions.Models;

namespace DeepWiki.Rag.Core.Embedding;

/// <summary>
/// No-op implementation of IEmbeddingService for testing and placeholder use.
/// Returns zero vectors and does not call any external APIs.
/// </summary>
public sealed class NoOpEmbeddingService : IEmbeddingService
{
    /// <inheritdoc />
    public string Provider => "noop";

    /// <inheritdoc />
    public string ModelId => "noop-embedding";

    /// <inheritdoc />
    public int EmbeddingDimension => 1536;

    /// <inheritdoc />
    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new float[1536]);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<float[]> EmbedBatchAsync(
        IEnumerable<string> texts,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var _ in texts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return await Task.FromResult(new float[1536]);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<EmbeddingResponse>> EmbedBatchWithMetadataAsync(
        IEnumerable<string> texts,
        int batchSize = 10,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var results = texts.Select(_ => EmbeddingResponse.Success(
            vector: new float[1536],
            provider: Provider,
            modelId: ModelId,
            latencyMs: 0)).ToList();

        return Task.FromResult<IReadOnlyList<EmbeddingResponse>>(results);
    }
}
