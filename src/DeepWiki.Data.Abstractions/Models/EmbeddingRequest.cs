using System;
using System.Text.Json.Serialization;

namespace DeepWiki.Data.Abstractions.Models;

/// <summary>
/// Request model for embedding operations. JSON-serializable for agent context passing.
/// </summary>
public sealed class EmbeddingRequest
{
    /// <summary>
    /// Gets or sets the text to embed.
    /// </summary>
    [JsonPropertyName("text")]
    public required string Text { get; init; }

    /// <summary>
    /// Gets or sets the model ID for embedding (e.g., "text-embedding-ada-002", "text-embedding-3-small").
    /// If not specified, the provider's default model will be used.
    /// </summary>
    [JsonPropertyName("modelId")]
    public string? ModelId { get; init; }

    /// <summary>
    /// Gets or sets optional metadata hints that may be used by the provider
    /// (e.g., document type, language hints for tokenization).
    /// </summary>
    [JsonPropertyName("metadataHint")]
    public string? MetadataHint { get; init; }

    /// <summary>
    /// Gets or sets the retry count for this specific request.
    /// Defaults to provider's retry policy if not specified.
    /// </summary>
    [JsonPropertyName("retryCount")]
    public int? RetryCount { get; init; }

    /// <summary>
    /// Gets or sets whether to use cached embeddings if available.
    /// Defaults to true for better resilience.
    /// </summary>
    [JsonPropertyName("useCache")]
    public bool UseCache { get; init; } = true;

    /// <summary>
    /// Creates a new embedding request for the specified text.
    /// </summary>
    /// <param name="text">The text to embed.</param>
    /// <returns>A new EmbeddingRequest instance.</returns>
    public static EmbeddingRequest Create(string text) => new() { Text = text };

    /// <summary>
    /// Creates a new embedding request with model specification.
    /// </summary>
    /// <param name="text">The text to embed.</param>
    /// <param name="modelId">The model ID to use.</param>
    /// <returns>A new EmbeddingRequest instance.</returns>
    public static EmbeddingRequest Create(string text, string modelId) => new() { Text = text, ModelId = modelId };
}
