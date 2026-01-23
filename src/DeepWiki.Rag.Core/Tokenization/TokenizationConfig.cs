using System.Collections.Frozen;

namespace DeepWiki.Rag.Core.Tokenization;

/// <summary>
/// Configuration for tokenization with mappings for OpenAI, Foundry, and Ollama models.
/// Provides token limits and encoding type mappings per model.
/// </summary>
public static class TokenizationConfig
{
    /// <summary>
    /// Default encoding for most modern models.
    /// </summary>
    public const string DefaultEncoding = "cl100k_base";

    /// <summary>
    /// Default maximum tokens for embedding models (text-embedding-ada-002, text-embedding-3-*).
    /// </summary>
    public const int DefaultEmbeddingMaxTokens = 8192;

    /// <summary>
    /// Default maximum tokens for GPT-4 context.
    /// </summary>
    public const int DefaultGpt4MaxTokens = 128000;

    /// <summary>
    /// Default maximum tokens for GPT-3.5-turbo context.
    /// </summary>
    public const int DefaultGpt35MaxTokens = 16385;

    /// <summary>
    /// Embedding dimension for text-embedding-ada-002 and text-embedding-3-* models.
    /// </summary>
    public const int EmbeddingDimension = 1536;

    /// <summary>
    /// Model to encoding type mapping. All modern models use cl100k_base.
    /// </summary>
    public static FrozenDictionary<string, string> ModelEncodings { get; } = new Dictionary<string, string>
    {
        // OpenAI embedding models
        ["text-embedding-ada-002"] = "cl100k_base",
        ["text-embedding-3-small"] = "cl100k_base",
        ["text-embedding-3-large"] = "cl100k_base",

        // OpenAI GPT models
        ["gpt-4"] = "cl100k_base",
        ["gpt-4-turbo"] = "cl100k_base",
        ["gpt-4-turbo-preview"] = "cl100k_base",
        ["gpt-4o"] = "o200k_base",
        ["gpt-4o-mini"] = "o200k_base",
        ["gpt-3.5-turbo"] = "cl100k_base",
        ["gpt-3.5-turbo-16k"] = "cl100k_base",

        // Azure/Foundry models (same encodings as OpenAI)
        ["foundry/gpt-4"] = "cl100k_base",
        ["foundry/gpt-4-turbo"] = "cl100k_base",
        ["foundry/gpt-4o"] = "o200k_base",
        ["foundry/text-embedding-ada-002"] = "cl100k_base",
        ["foundry/text-embedding-3-small"] = "cl100k_base",
        ["foundry/text-embedding-3-large"] = "cl100k_base",

        // Ollama models (approximate with cl100k_base)
        ["ollama/llama3"] = "cl100k_base",
        ["ollama/llama3.1"] = "cl100k_base",
        ["ollama/llama3.2"] = "cl100k_base",
        ["ollama/mistral"] = "cl100k_base",
        ["ollama/mixtral"] = "cl100k_base",
        ["ollama/codellama"] = "cl100k_base",
        ["ollama/phi3"] = "cl100k_base",
        ["ollama/nomic-embed-text"] = "cl100k_base",
        ["ollama/mxbai-embed-large"] = "cl100k_base",
        ["ollama/all-minilm"] = "cl100k_base",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Model to maximum token limit mapping.
    /// </summary>
    public static FrozenDictionary<string, int> ModelMaxTokens { get; } = new Dictionary<string, int>
    {
        // OpenAI embedding models
        ["text-embedding-ada-002"] = 8191,
        ["text-embedding-3-small"] = 8191,
        ["text-embedding-3-large"] = 8191,

        // OpenAI GPT models
        ["gpt-4"] = 8192,
        ["gpt-4-turbo"] = 128000,
        ["gpt-4-turbo-preview"] = 128000,
        ["gpt-4o"] = 128000,
        ["gpt-4o-mini"] = 128000,
        ["gpt-3.5-turbo"] = 4096,
        ["gpt-3.5-turbo-16k"] = 16385,

        // Azure/Foundry models
        ["foundry/gpt-4"] = 8192,
        ["foundry/gpt-4-turbo"] = 128000,
        ["foundry/gpt-4o"] = 128000,
        ["foundry/text-embedding-ada-002"] = 8191,
        ["foundry/text-embedding-3-small"] = 8191,
        ["foundry/text-embedding-3-large"] = 8191,

        // Ollama models (typical context windows)
        ["ollama/llama3"] = 8192,
        ["ollama/llama3.1"] = 131072,
        ["ollama/llama3.2"] = 131072,
        ["ollama/mistral"] = 32768,
        ["ollama/mixtral"] = 32768,
        ["ollama/codellama"] = 16384,
        ["ollama/phi3"] = 128000,
        ["ollama/nomic-embed-text"] = 8192,
        ["ollama/mxbai-embed-large"] = 512,
        ["ollama/all-minilm"] = 256,
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the encoding type for the specified model.
    /// </summary>
    /// <param name="modelId">The model identifier.</param>
    /// <returns>The encoding type (defaults to cl100k_base).</returns>
    public static string GetEncoding(string modelId)
    {
        if (string.IsNullOrEmpty(modelId))
            return DefaultEncoding;

        // Try exact match first
        if (ModelEncodings.TryGetValue(modelId, out var encoding))
            return encoding;

        // Try prefix matching for model families
        var lowerModel = modelId.ToLowerInvariant();

        if (lowerModel.Contains("gpt-4o"))
            return "o200k_base";

        if (lowerModel.Contains("gpt-4") || lowerModel.Contains("gpt-3.5") ||
            lowerModel.Contains("embedding") || lowerModel.Contains("text-embedding"))
            return "cl100k_base";

        // Default to cl100k_base for unknown models
        return DefaultEncoding;
    }

    /// <summary>
    /// Gets the maximum token limit for the specified model.
    /// </summary>
    /// <param name="modelId">The model identifier.</param>
    /// <returns>The maximum token limit (defaults to 8192 for embedding use cases).</returns>
    public static int GetMaxTokens(string modelId)
    {
        if (string.IsNullOrEmpty(modelId))
            return DefaultEmbeddingMaxTokens;

        // Try exact match first
        if (ModelMaxTokens.TryGetValue(modelId, out var maxTokens))
            return maxTokens;

        // Try prefix matching for model families
        var lowerModel = modelId.ToLowerInvariant();

        if (lowerModel.Contains("embedding"))
            return DefaultEmbeddingMaxTokens;

        if (lowerModel.Contains("gpt-4-turbo") || lowerModel.Contains("gpt-4o"))
            return DefaultGpt4MaxTokens;

        if (lowerModel.Contains("gpt-4"))
            return 8192;

        if (lowerModel.Contains("gpt-3.5-turbo-16k"))
            return DefaultGpt35MaxTokens;

        if (lowerModel.Contains("gpt-3.5"))
            return 4096;

        // Default for embedding models
        return DefaultEmbeddingMaxTokens;
    }

    /// <summary>
    /// Determines the provider from the model ID.
    /// </summary>
    /// <param name="modelId">The model identifier.</param>
    /// <returns>The provider name (openai, foundry, ollama).</returns>
    public static string GetProvider(string modelId)
    {
        if (string.IsNullOrEmpty(modelId))
            return "openai";

        var lowerModel = modelId.ToLowerInvariant();

        if (lowerModel.StartsWith("foundry/") || lowerModel.StartsWith("azure/"))
            return "foundry";

        if (lowerModel.StartsWith("ollama/"))
            return "ollama";

        // Default to OpenAI for standard model names
        return "openai";
    }
}
