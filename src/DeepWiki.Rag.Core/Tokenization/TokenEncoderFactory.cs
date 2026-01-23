using Microsoft.Extensions.Logging;

namespace DeepWiki.Rag.Core.Tokenization;

/// <summary>
/// Factory for creating token encoders based on model ID or provider.
/// Selects the appropriate encoder implementation for OpenAI, Foundry, or Ollama.
/// </summary>
public sealed class TokenEncoderFactory
{
    private readonly ILoggerFactory? _loggerFactory;

    /// <summary>
    /// Creates a new token encoder factory.
    /// </summary>
    /// <param name="loggerFactory">Optional logger factory for creating loggers.</param>
    public TokenEncoderFactory(ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Creates a token encoder for the specified model ID.
    /// </summary>
    /// <param name="modelId">The model identifier (e.g., "gpt-4", "foundry/gpt-4", "ollama/llama3").</param>
    /// <returns>An appropriate token encoder for the model.</returns>
    public ITokenEncoder CreateEncoder(string modelId)
    {
        var provider = TokenizationConfig.GetProvider(modelId);
        var encoding = TokenizationConfig.GetEncoding(modelId);

        return CreateEncoderForProvider(provider, encoding);
    }

    /// <summary>
    /// Creates a token encoder for the specified provider and encoding.
    /// </summary>
    /// <param name="provider">The provider name (openai, foundry, ollama).</param>
    /// <param name="encoding">The encoding name (cl100k_base, o200k_base).</param>
    /// <returns>An appropriate token encoder.</returns>
    public ITokenEncoder CreateEncoderForProvider(string provider, string encoding = "cl100k_base")
    {
        return provider.ToLowerInvariant() switch
        {
            "foundry" or "azure" => new FoundryTokenEncoder(
                encoding,
                _loggerFactory?.CreateLogger<FoundryTokenEncoder>()),

            "ollama" => new OllamaTokenEncoder(
                encoding,
                _loggerFactory?.CreateLogger<OllamaTokenEncoder>()),

            // Default to OpenAI
            _ => new OpenAITokenEncoder(
                encoding,
                _loggerFactory?.CreateLogger<OpenAITokenEncoder>()),
        };
    }

    /// <summary>
    /// Gets the default encoder (cl100k_base via OpenAI encoder).
    /// </summary>
    /// <returns>The default token encoder.</returns>
    public ITokenEncoder GetDefaultEncoder()
    {
        return new OpenAITokenEncoder(
            TokenizationConfig.DefaultEncoding,
            _loggerFactory?.CreateLogger<OpenAITokenEncoder>());
    }
}
