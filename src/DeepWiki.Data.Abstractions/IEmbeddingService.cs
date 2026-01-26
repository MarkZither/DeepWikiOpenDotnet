using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DeepWiki.Data.Abstractions;

/// <summary>
/// Microsoft Agent Framework-compatible embedding service for converting text to vector embeddings.
/// Supports OpenAI, Microsoft AI Foundry, and Ollama providers.
/// All methods are async with resilient error handling for agent tool calls.
/// Result types are JSON-serializable for agent context passing.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// Gets the name of the current embedding provider (e.g., "openai", "foundry", "ollama").
    /// </summary>
    string Provider { get; }

    /// <summary>
    /// Gets the model ID used for embeddings (e.g., "text-embedding-ada-002", "text-embedding-3-small").
    /// </summary>
    string ModelId { get; }

    /// <summary>
    /// Gets the expected dimensionality of embeddings from this provider.
    /// Standard is 1536 for OpenAI ada-002 and most compatible models.
    /// </summary>
    int EmbeddingDimension { get; }

    /// <summary>
    /// Converts text into a vector embedding.
    /// </summary>
    /// <param name="text">The text to embed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A 1536-dimensional float array representing the text embedding.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when embedding fails after all retries and no cached embedding is available.
    /// The exception message includes provider context for debugging.
    /// </exception>
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts multiple texts into vector embeddings efficiently using batching.
    /// Yields embeddings as they become available for streaming consumption.
    /// </summary>
    /// <param name="texts">The texts to embed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of 1536-dimensional float arrays.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when embedding fails after all retries for any text.
    /// </exception>
    IAsyncEnumerable<float[]> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts multiple texts into vector embeddings as a single batch operation.
    /// More efficient than individual calls for bulk operations.
    /// </summary>
    /// <param name="texts">The texts to embed.</param>
    /// <param name="batchSize">Number of texts per API batch (default: 10, max: 100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of embedding responses with vectors and metadata.</returns>
    Task<IReadOnlyList<Models.EmbeddingResponse>> EmbedBatchWithMetadataAsync(
        IEnumerable<string> texts,
        int batchSize = 10,
        CancellationToken cancellationToken = default);
}
