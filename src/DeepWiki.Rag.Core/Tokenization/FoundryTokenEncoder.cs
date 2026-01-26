using Microsoft.Extensions.Logging;

namespace DeepWiki.Rag.Core.Tokenization;

/// <summary>
/// Microsoft AI Foundry token encoder.
/// Uses the same tokenization as OpenAI models (cl100k_base) since Foundry hosts OpenAI-compatible models.
/// </summary>
public sealed class FoundryTokenEncoder : ITokenEncoder
{
    private readonly OpenAITokenEncoder _innerEncoder;
    private readonly ILogger<FoundryTokenEncoder>? _logger;

    /// <inheritdoc />
    public string EncodingName => _innerEncoder.EncodingName;

    /// <summary>
    /// Creates a new Foundry token encoder.
    /// </summary>
    /// <param name="encodingName">The encoding name (defaults to cl100k_base).</param>
    /// <param name="logger">Optional logger.</param>
    public FoundryTokenEncoder(string encodingName = "cl100k_base", ILogger<FoundryTokenEncoder>? logger = null)
    {
        _logger = logger;
        // Foundry uses the same tokenization as OpenAI
        _innerEncoder = new OpenAITokenEncoder(encodingName);
        _logger?.LogDebug("Initialized Foundry token encoder with encoding: {Encoding}", EncodingName);
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
