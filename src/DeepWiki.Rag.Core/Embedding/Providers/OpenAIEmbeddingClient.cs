using System.ClientModel;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using OpenAI.Embeddings;

namespace DeepWiki.Rag.Core.Embedding.Providers;

/// <summary>
/// OpenAI embedding client wrapping the Azure.AI.OpenAI SDK.
/// Supports both OpenAI API and Azure OpenAI endpoints.
/// </summary>
public sealed class OpenAIEmbeddingClient : BaseEmbeddingClient
{
    private readonly EmbeddingClient _client;
    private readonly string _modelId;

    /// <inheritdoc />
    public override string Provider => "openai";

    /// <inheritdoc />
    public override string ModelId => _modelId;

    /// <summary>
    /// Creates a new OpenAI embedding client.
    /// </summary>
    /// <param name="apiKey">The OpenAI API key.</param>
    /// <param name="modelId">The model ID (default: text-embedding-ada-002).</param>
    /// <param name="endpoint">Optional custom endpoint for Azure OpenAI.</param>
    /// <param name="retryPolicy">Optional retry policy.</param>
    /// <param name="cache">Optional embedding cache.</param>
    /// <param name="logger">Optional logger.</param>
    public OpenAIEmbeddingClient(
        string apiKey,
        string modelId = "text-embedding-ada-002",
        string? endpoint = null,
        RetryPolicy? retryPolicy = null,
        IEmbeddingCache? cache = null,
        ILogger<OpenAIEmbeddingClient>? logger = null)
        : base(retryPolicy, cache, logger)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new ArgumentException("API key cannot be null or empty.", nameof(apiKey));
        }

        _modelId = modelId;

        // Create the appropriate client based on whether an endpoint is provided
        if (!string.IsNullOrEmpty(endpoint))
        {
            // Azure OpenAI
            var azureClient = new AzureOpenAIClient(
                new Uri(endpoint),
                new ApiKeyCredential(apiKey));
            _client = azureClient.GetEmbeddingClient(modelId);
        }
        else
        {
            // Standard OpenAI
            var openAIClient = new OpenAI.OpenAIClient(apiKey);
            _client = openAIClient.GetEmbeddingClient(modelId);
        }

        Logger?.LogInformation(
            "Initialized OpenAI embedding client with model {ModelId}, endpoint: {Endpoint}",
            modelId, endpoint ?? "api.openai.com");
    }

    /// <inheritdoc />
    protected override async Task<float[]> EmbedCoreAsync(string text, CancellationToken cancellationToken)
    {
        Logger?.LogDebug("Embedding text ({Length} chars) using OpenAI model {ModelId}", text.Length, _modelId);

        var result = await _client.GenerateEmbeddingAsync(text, cancellationToken: cancellationToken);

        // Convert ReadOnlyMemory<float> to float[]
        var embedding = result.Value.ToFloats().ToArray();

        Logger?.LogDebug("Received embedding with {Dimension} dimensions from OpenAI", embedding.Length);

        return embedding;
    }

    /// <inheritdoc />
    protected override async Task<IReadOnlyList<float[]>> EmbedBatchCoreAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken)
    {
        Logger?.LogDebug("Batch embedding {Count} texts using OpenAI model {ModelId}", texts.Count, _modelId);

        var results = await _client.GenerateEmbeddingsAsync(texts, cancellationToken: cancellationToken);

        var embeddings = results.Value
            .OrderBy(e => e.Index)
            .Select(e => e.ToFloats().ToArray())
            .ToList();

        Logger?.LogDebug("Received {Count} embeddings from OpenAI batch request", embeddings.Count);

        return embeddings;
    }
}
