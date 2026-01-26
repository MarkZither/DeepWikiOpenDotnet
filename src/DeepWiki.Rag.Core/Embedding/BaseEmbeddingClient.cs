using System.Diagnostics;
using System.Runtime.CompilerServices;
using DeepWiki.Data.Abstractions;
using DeepWiki.Data.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace DeepWiki.Rag.Core.Embedding;

/// <summary>
/// Abstract base class for embedding clients with common retry, fallback, and logging logic.
/// Provides shared functionality for OpenAI, Foundry, and Ollama providers.
/// </summary>
public abstract class BaseEmbeddingClient : IEmbeddingService
{
    /// <summary>
    /// The retry policy for handling failures.
    /// </summary>
    protected readonly RetryPolicy RetryPolicy;

    /// <summary>
    /// Optional embedding cache for fallback.
    /// </summary>
    protected readonly IEmbeddingCache? Cache;

    /// <summary>
    /// Logger for this client.
    /// </summary>
    protected readonly ILogger? Logger;

    /// <summary>
    /// Default batch size for batch embedding operations.
    /// </summary>
    protected const int DefaultBatchSize = 10;

    /// <summary>
    /// Maximum batch size for batch embedding operations.
    /// </summary>
    protected const int MaxBatchSize = 100;

    /// <summary>
    /// Expected embedding dimensionality (1536 for OpenAI ada-002 compatible).
    /// </summary>
    protected const int ExpectedDimension = 1536;

    /// <summary>
    /// Creates a new base embedding client.
    /// </summary>
    /// <param name="retryPolicy">The retry policy.</param>
    /// <param name="cache">Optional embedding cache.</param>
    /// <param name="logger">Optional logger.</param>
    protected BaseEmbeddingClient(RetryPolicy? retryPolicy = null, IEmbeddingCache? cache = null, ILogger? logger = null)
    {
        RetryPolicy = retryPolicy ?? RetryPolicy.Default;
        Cache = cache;
        Logger = logger;
    }

    /// <inheritdoc />
    public abstract string Provider { get; }

    /// <inheritdoc />
    public abstract string ModelId { get; }

    /// <inheritdoc />
    public virtual int EmbeddingDimension => ExpectedDimension;

    /// <inheritdoc />
    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
        {
            throw new ArgumentException("Text cannot be null or empty.", nameof(text));
        }

        var sw = Stopwatch.StartNew();

        try
        {
            var embedding = await RetryPolicy.ExecuteEmbeddingAsync(
                ct => EmbedCoreAsync(text, ct),
                text,
                ModelId,
                Provider,
                cancellationToken);

            ValidateDimension(embedding);

            sw.Stop();
            Logger?.LogInformation(
                "Embedded text ({Length} chars) using [{Provider}] model {ModelId} in {LatencyMs}ms",
                text.Length, Provider, ModelId, sw.ElapsedMilliseconds);

            return embedding;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            Logger?.LogError(ex,
                "Failed to embed text using [{Provider}] model {ModelId} after {LatencyMs}ms: {Error}",
                Provider, ModelId, sw.ElapsedMilliseconds, ex.Message);
            throw;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<float[]> EmbedBatchAsync(
        IEnumerable<string> texts,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var textList = texts.ToList();
        if (textList.Count == 0)
        {
            yield break;
        }

        var sw = Stopwatch.StartNew();
        var processedCount = 0;

        // Process in batches
        foreach (var batch in ChunkTexts(textList, DefaultBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var embeddings = await EmbedBatchCoreAsync(batch, cancellationToken);

            foreach (var embedding in embeddings)
            {
                ValidateDimension(embedding);
                processedCount++;
                yield return embedding;
            }
        }

        sw.Stop();
        Logger?.LogInformation(
            "Batch embedded {Count} texts using [{Provider}] model {ModelId} in {LatencyMs}ms ({Rate:F1} texts/sec)",
            processedCount, Provider, ModelId, sw.ElapsedMilliseconds,
            processedCount / (sw.ElapsedMilliseconds / 1000.0));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EmbeddingResponse>> EmbedBatchWithMetadataAsync(
        IEnumerable<string> texts,
        int batchSize = DefaultBatchSize,
        CancellationToken cancellationToken = default)
    {
        var textList = texts.ToList();
        if (textList.Count == 0)
        {
            return [];
        }

        batchSize = Math.Clamp(batchSize, 1, MaxBatchSize);
        var results = new List<EmbeddingResponse>(textList.Count);
        var sw = Stopwatch.StartNew();

        foreach (var batch in ChunkTexts(textList, batchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batchSw = Stopwatch.StartNew();

            var embeddings = await EmbedBatchCoreAsync(batch, cancellationToken);
            batchSw.Stop();

            var batchLatency = batchSw.ElapsedMilliseconds / batch.Count;

            foreach (var embedding in embeddings)
            {
                ValidateDimension(embedding);
                results.Add(EmbeddingResponse.Success(
                    vector: embedding,
                    provider: Provider,
                    modelId: ModelId,
                    latencyMs: batchLatency));
            }
        }

        sw.Stop();
        Logger?.LogInformation(
            "Batch embedded {Count} texts with metadata using [{Provider}] model {ModelId} in {LatencyMs}ms",
            results.Count, Provider, ModelId, sw.ElapsedMilliseconds);

        return results;
    }

    /// <summary>
    /// Core embedding implementation for a single text. Override in derived classes.
    /// </summary>
    /// <param name="text">The text to embed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The embedding vector.</returns>
    protected abstract Task<float[]> EmbedCoreAsync(string text, CancellationToken cancellationToken);

    /// <summary>
    /// Core batch embedding implementation. Override in derived classes for provider-specific batching.
    /// Default implementation calls EmbedCoreAsync for each text.
    /// </summary>
    /// <param name="texts">The texts to embed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The embedding vectors in the same order as input texts.</returns>
    protected virtual async Task<IReadOnlyList<float[]>> EmbedBatchCoreAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken)
    {
        var results = new List<float[]>(texts.Count);

        foreach (var text in texts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var embedding = await RetryPolicy.ExecuteEmbeddingAsync(
                ct => EmbedCoreAsync(text, ct),
                text,
                ModelId,
                Provider,
                cancellationToken);
            results.Add(embedding);
        }

        return results;
    }

    /// <summary>
    /// Validates that the embedding has the expected dimensionality.
    /// </summary>
    /// <param name="embedding">The embedding to validate.</param>
    /// <exception cref="InvalidOperationException">Thrown if dimension is incorrect.</exception>
    protected void ValidateDimension(float[] embedding)
    {
        if (embedding.Length != ExpectedDimension)
        {
            throw new InvalidOperationException(
                $"Embedding dimension mismatch: expected {ExpectedDimension}, got {embedding.Length}. " +
                $"Provider: {Provider}, Model: {ModelId}");
        }
    }

    /// <summary>
    /// Splits texts into batches of specified size.
    /// </summary>
    protected static IEnumerable<IReadOnlyList<string>> ChunkTexts(IReadOnlyList<string> texts, int batchSize)
    {
        for (int i = 0; i < texts.Count; i += batchSize)
        {
            var count = Math.Min(batchSize, texts.Count - i);
            yield return texts.Skip(i).Take(count).ToList();
        }
    }
}
