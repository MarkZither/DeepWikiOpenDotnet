using System;
using System.Text.Json.Serialization;

namespace DeepWiki.Data.Abstractions.Models;

/// <summary>
/// Response model for embedding operations. JSON-serializable for agent context passing.
/// </summary>
public sealed class EmbeddingResponse
{
    /// <summary>
    /// Gets or sets the embedding vector (typically 1536 dimensions).
    /// </summary>
    [JsonPropertyName("vector")]
    public required float[] Vector { get; init; }

    /// <summary>
    /// Gets or sets the provider that generated this embedding (e.g., "openai", "foundry", "ollama").
    /// </summary>
    [JsonPropertyName("provider")]
    public required string Provider { get; init; }

    /// <summary>
    /// Gets or sets the model ID used for this embedding.
    /// </summary>
    [JsonPropertyName("modelId")]
    public required string ModelId { get; init; }

    /// <summary>
    /// Gets or sets the latency in milliseconds for this embedding request.
    /// </summary>
    [JsonPropertyName("latencyMs")]
    public long LatencyMs { get; init; }

    /// <summary>
    /// Gets or sets the number of tokens in the input text (if available from the provider).
    /// </summary>
    [JsonPropertyName("tokenCount")]
    public int? TokenCount { get; init; }

    /// <summary>
    /// Gets or sets whether this embedding was served from cache.
    /// </summary>
    [JsonPropertyName("fromCache")]
    public bool FromCache { get; init; }

    /// <summary>
    /// Gets or sets the number of retry attempts made before success.
    /// </summary>
    [JsonPropertyName("retryAttempts")]
    public int RetryAttempts { get; init; }

    /// <summary>
    /// Gets the dimensionality of the embedding vector.
    /// </summary>
    [JsonIgnore]
    public int Dimension => Vector?.Length ?? 0;

    /// <summary>
    /// Creates a successful embedding response.
    /// </summary>
    public static EmbeddingResponse Success(float[] vector, string provider, string modelId, long latencyMs = 0, int? tokenCount = null, bool fromCache = false, int retryAttempts = 0)
        => new()
        {
            Vector = vector,
            Provider = provider,
            ModelId = modelId,
            LatencyMs = latencyMs,
            TokenCount = tokenCount,
            FromCache = fromCache,
            RetryAttempts = retryAttempts
        };
}
