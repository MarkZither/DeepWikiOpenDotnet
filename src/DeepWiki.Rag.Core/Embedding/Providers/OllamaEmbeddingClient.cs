using Microsoft.Extensions.Logging;
using OllamaSharp;

namespace DeepWiki.Rag.Core.Embedding.Providers;

/// <summary>
/// Ollama embedding client wrapping OllamaSharp.
/// Supports local Ollama deployments for embedding generation.
/// </summary>
public sealed class OllamaEmbeddingClient : BaseEmbeddingClient
{
    private readonly OllamaApiClient _client;
    private readonly string _modelId;

    /// <inheritdoc />
    public override string Provider => "ollama";

    /// <inheritdoc />
    public override string ModelId => _modelId;

    /// <summary>
    /// Creates a new Ollama embedding client.
    /// </summary>
    /// <param name="endpoint">The Ollama server endpoint (default: http://localhost:11434).</param>
    /// <param name="modelId">The model ID (default: nomic-embed-text).</param>
    /// <param name="retryPolicy">Optional retry policy.</param>
    /// <param name="cache">Optional embedding cache.</param>
    /// <param name="logger">Optional logger.</param>
    public OllamaEmbeddingClient(
        string endpoint = "http://localhost:11434",
        string modelId = "nomic-embed-text",
        RetryPolicy? retryPolicy = null,
        IEmbeddingCache? cache = null,
        ILogger<OllamaEmbeddingClient>? logger = null)
        : base(retryPolicy, cache, logger)
    {
        if (string.IsNullOrEmpty(endpoint))
        {
            endpoint = "http://localhost:11434";
        }

        _modelId = modelId;
        _client = new OllamaApiClient(new Uri(endpoint));
        _client.SelectedModel = modelId;

        Logger?.LogInformation(
            "Initialized Ollama embedding client with model {ModelId} at {Endpoint}",
            modelId, endpoint);
    }

    /// <inheritdoc />
    protected override async Task<float[]> EmbedCoreAsync(string text, CancellationToken cancellationToken)
    {
        Logger?.LogDebug("Embedding text ({Length} chars) using Ollama model {ModelId}", text.Length, _modelId);

        var response = await _client.EmbedAsync(new OllamaSharp.Models.EmbedRequest
        {
            Model = _modelId,
            Input = [text]
        }, cancellationToken);

        if (response?.Embeddings is null || response.Embeddings.Count == 0)
        {
            throw new InvalidOperationException(
                $"Ollama returned no embeddings for model {_modelId}. " +
                "Ensure the model is downloaded and supports embeddings.");
        }

        var embedding = response.Embeddings.First();

        // Ollama may return different dimension embeddings depending on model
        // nomic-embed-text returns 768, we need to pad or handle this
        var result = NormalizeEmbeddingDimension(embedding);

        Logger?.LogDebug(
            "Received embedding with {OriginalDim} dimensions from Ollama (normalized to {NormalizedDim})",
            embedding.Length, result.Length);

        return result;
    }

    /// <inheritdoc />
    protected override async Task<IReadOnlyList<float[]>> EmbedBatchCoreAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken)
    {
        Logger?.LogDebug("Batch embedding {Count} texts using Ollama model {ModelId}", texts.Count, _modelId);

        // Ollama supports batch embedding in a single request
        var response = await _client.EmbedAsync(new OllamaSharp.Models.EmbedRequest
        {
            Model = _modelId,
            Input = texts.ToList()
        }, cancellationToken);

        if (response?.Embeddings is null)
        {
            throw new InvalidOperationException(
                $"Ollama returned no embeddings for model {_modelId} in batch request.");
        }

        var embeddings = response.Embeddings
            .Select(NormalizeEmbeddingDimension)
            .ToList();

        Logger?.LogDebug("Received {Count} embeddings from Ollama batch request", embeddings.Count);

        return embeddings;
    }

    /// <summary>
    /// Normalizes embedding dimension to 1536.
    /// Ollama models may return different dimensions (e.g., nomic-embed-text returns 768).
    /// This pads with zeros or truncates to match expected dimension.
    /// </summary>
    private float[] NormalizeEmbeddingDimension(float[] embedding)
    {
        if (embedding.Length == ExpectedDimension)
        {
            return embedding;
        }

        var result = new float[ExpectedDimension];

        // Copy what we have
        var copyLength = Math.Min(embedding.Length, ExpectedDimension);
        Array.Copy(embedding, result, copyLength);

        // If embedding is smaller, the rest remains as zeros (padding)
        // If embedding is larger, we truncate (unlikely for 768->1536)

        Logger?.LogDebug(
            "Normalized Ollama embedding from {Original} to {Expected} dimensions",
            embedding.Length, ExpectedDimension);

        return result;
    }
}
