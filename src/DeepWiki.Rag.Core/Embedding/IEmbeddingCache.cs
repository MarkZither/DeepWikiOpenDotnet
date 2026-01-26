namespace DeepWiki.Rag.Core.Embedding;

/// <summary>
/// Interface for caching embeddings to support fallback on provider failures.
/// Provides resilience for agent knowledge retrieval workflows.
/// </summary>
public interface IEmbeddingCache
{
    /// <summary>
    /// Attempts to retrieve a cached embedding by text hash.
    /// </summary>
    /// <param name="text">The original text that was embedded.</param>
    /// <param name="modelId">The model ID used for embedding.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached embedding vector if found; null otherwise.</returns>
    Task<float[]?> GetAsync(string text, string modelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores an embedding in the cache.
    /// </summary>
    /// <param name="text">The original text that was embedded.</param>
    /// <param name="modelId">The model ID used for embedding.</param>
    /// <param name="embedding">The embedding vector to cache.</param>
    /// <param name="ttl">Optional time-to-live for the cache entry. Defaults to provider configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SetAsync(string text, string modelId, float[] embedding, TimeSpan? ttl = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a cached embedding.
    /// </summary>
    /// <param name="text">The original text.</param>
    /// <param name="modelId">The model ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RemoveAsync(string text, string modelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all cached embeddings for a specific model.
    /// </summary>
    /// <param name="modelId">The model ID, or null to clear all caches.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ClearAsync(string? modelId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the number of cached embeddings.
    /// </summary>
    int Count { get; }
}
