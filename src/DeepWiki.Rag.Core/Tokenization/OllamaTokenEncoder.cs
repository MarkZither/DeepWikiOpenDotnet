using Microsoft.Extensions.Logging;

namespace DeepWiki.Rag.Core.Tokenization;

/// <summary>
/// Ollama token encoder.
/// Uses cl100k_base encoding as an approximation since Ollama models (Llama, Mistral, etc.)
/// have similar tokenization characteristics to GPT models for most use cases.
/// </summary>
public sealed class OllamaTokenEncoder : ITokenEncoder
{
    private readonly OpenAITokenEncoder _innerEncoder;
    private readonly ILogger<OllamaTokenEncoder>? _logger;

    /// <inheritdoc />
    public string EncodingName => _innerEncoder.EncodingName;

    /// <summary>
    /// Creates a new Ollama token encoder.
    /// </summary>
    /// <param name="encodingName">The encoding name (defaults to cl100k_base).</param>
    /// <param name="logger">Optional logger.</param>
    public OllamaTokenEncoder(string encodingName = "cl100k_base", ILogger<OllamaTokenEncoder>? logger = null)
    {
        _logger = logger;
        // Ollama uses cl100k_base as an approximation for token counting
        // This provides reasonable estimates for chunking purposes
        _innerEncoder = new OpenAITokenEncoder(encodingName);
        _logger?.LogDebug("Initialized Ollama token encoder with encoding: {Encoding} (approximation)", EncodingName);
    }

    /// <inheritdoc />
    public int CountTokens(string text) => _innerEncoder.CountTokens(text);

    /// <inheritdoc />
    public IReadOnlyList<int> Encode(string text) => _innerEncoder.Encode(text);

    /// <inheritdoc />
    public string Decode(IReadOnlyList<int> tokens) => _innerEncoder.Decode(tokens);

    /// <inheritdoc />
    public int FindSplitPoint(string text, int maxTokens) => _innerEncoder.FindSplitPoint(text, maxTokens);
}
