using System.ClientModel;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using OpenAI.Embeddings;

namespace DeepWiki.Rag.Core.Embedding.Providers;

/// <summary>
/// Microsoft AI Foundry (Azure OpenAI) embedding client.
/// Supports Azure OpenAI endpoints and Foundry Local deployments.
/// </summary>
public sealed class FoundryEmbeddingClient : BaseEmbeddingClient
{
    private readonly EmbeddingClient _client;
    private readonly string _modelId;
    private readonly string _deploymentName;

    /// <inheritdoc />
    public override string Provider => "foundry";

    /// <inheritdoc />
    public override string ModelId => _modelId;

    /// <summary>
    /// Gets the Azure OpenAI deployment name.
    /// </summary>
    public string DeploymentName => _deploymentName;

    /// <summary>
    /// Creates a new Foundry embedding client.
    /// </summary>
    /// <param name="endpoint">The Azure OpenAI endpoint.</param>
    /// <param name="deploymentName">The deployment name.</param>
    /// <param name="modelId">The model ID (default: same as deployment name).</param>
    /// <param name="apiKey">Optional API key (if not using managed identity).</param>
    /// <param name="retryPolicy">Optional retry policy.</param>
    /// <param name="cache">Optional embedding cache.</param>
    /// <param name="logger">Optional logger.</param>
    public FoundryEmbeddingClient(
        string endpoint,
        string deploymentName,
        string? modelId = null,
        string? apiKey = null,
        RetryPolicy? retryPolicy = null,
        IEmbeddingCache? cache = null,
        ILogger<FoundryEmbeddingClient>? logger = null)
        : base(retryPolicy, cache, logger)
    {
        if (string.IsNullOrEmpty(endpoint))
        {
            throw new ArgumentException("Endpoint cannot be null or empty.", nameof(endpoint));
        }

        if (string.IsNullOrEmpty(deploymentName))
        {
            throw new ArgumentException("Deployment name cannot be null or empty.", nameof(deploymentName));
        }

        _deploymentName = deploymentName;
        _modelId = modelId ?? deploymentName;

        // Create Azure OpenAI client
        AzureOpenAIClient azureClient;

        if (!string.IsNullOrEmpty(apiKey))
        {
            azureClient = new AzureOpenAIClient(
                new Uri(endpoint),
                new ApiKeyCredential(apiKey));
        }
        else
        {
            // Use DefaultAzureCredential for managed identity
            azureClient = new AzureOpenAIClient(
                new Uri(endpoint),
                new Azure.Identity.DefaultAzureCredential());
        }

        _client = azureClient.GetEmbeddingClient(deploymentName);

        Logger?.LogInformation(
            "Initialized Foundry embedding client with deployment {DeploymentName} at {Endpoint}",
            deploymentName, endpoint);
    }

    /// <inheritdoc />
    protected override async Task<float[]> EmbedCoreAsync(string text, CancellationToken cancellationToken)
    {
        Logger?.LogDebug(
            "Embedding text ({Length} chars) using Foundry deployment {Deployment}",
            text.Length, _deploymentName);

        var result = await _client.GenerateEmbeddingAsync(text, cancellationToken: cancellationToken);

        // Convert ReadOnlyMemory<float> to float[]
        var embedding = result.Value.ToFloats().ToArray();

        Logger?.LogDebug("Received embedding with {Dimension} dimensions from Foundry", embedding.Length);

        return embedding;
    }

    /// <inheritdoc />
    protected override async Task<IReadOnlyList<float[]>> EmbedBatchCoreAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken)
    {
        Logger?.LogDebug(
            "Batch embedding {Count} texts using Foundry deployment {Deployment}",
            texts.Count, _deploymentName);

        var results = await _client.GenerateEmbeddingsAsync(texts, cancellationToken: cancellationToken);

        var embeddings = results.Value
            .OrderBy(e => e.Index)
            .Select(e => e.ToFloats().ToArray())
            .ToList();

        Logger?.LogDebug("Received {Count} embeddings from Foundry batch request", embeddings.Count);

        return embeddings;
    }
}
