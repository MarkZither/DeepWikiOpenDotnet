using Microsoft.Extensions.Logging;
using Tiktoken;

namespace DeepWiki.Rag.Core.Tokenization;

/// <summary>
/// OpenAI token encoder using the tiktoken library.
/// Supports cl100k_base (GPT-4, GPT-3.5, text-embedding-ada-002) and o200k_base (GPT-4o) encodings.
/// </summary>
public sealed class OpenAITokenEncoder : ITokenEncoder
{
    private readonly Encoder _encoder;
    private readonly ILogger<OpenAITokenEncoder>? _logger;

    /// <inheritdoc />
    public string EncodingName { get; }

    /// <summary>
    /// Creates a new OpenAI token encoder with the specified encoding.
    /// </summary>
    /// <param name="encodingName">The encoding name (cl100k_base or o200k_base). Defaults to cl100k_base.</param>
    /// <param name="logger">Optional logger.</param>
    public OpenAITokenEncoder(string encodingName = "cl100k_base", ILogger<OpenAITokenEncoder>? logger = null)
    {
        EncodingName = encodingName;
        _logger = logger;

        // Get encoder based on encoding name - ModelToEncoder.For uses model names
        var modelName = encodingName.ToLowerInvariant() switch
        {
            "o200k_base" => "gpt-4o",
            "cl100k_base" or _ => "gpt-4",
        };
        _encoder = ModelToEncoder.For(modelName);

        _logger?.LogDebug("Initialized OpenAI token encoder with encoding: {Encoding}", EncodingName);
    }

    /// <inheritdoc />
    public int CountTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        try
        {
            return _encoder.CountTokens(text);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error counting tokens for text of length {Length}, falling back to estimate", text.Length);
            // Rough estimate: ~4 chars per token for English
            return (int)Math.Ceiling(text.Length / 4.0);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<int> Encode(string text)
    {
        if (string.IsNullOrEmpty(text))
            return Array.Empty<int>();

        try
        {
            return _encoder.Encode(text).ToList();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error encoding text of length {Length}", text.Length);
            return Array.Empty<int>();
        }
    }

    /// <inheritdoc />
    public string Decode(IReadOnlyList<int> tokens)
    {
        if (tokens == null || tokens.Count == 0)
            return string.Empty;

        try
        {
            return _encoder.Decode(tokens);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error decoding {Count} tokens", tokens.Count);
            return string.Empty;
        }
    }

    /// <inheritdoc />
    public int FindSplitPoint(string text, int maxTokens)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var tokens = Encode(text);
        if (tokens.Count <= maxTokens)
            return text.Length;

        // Binary search for the right split point
        var targetTokens = tokens.Take(maxTokens).ToList();
        var decoded = Decode(targetTokens);

        // Find the last word boundary before or at the decoded length
        var splitPoint = FindWordBoundary(text, decoded.Length);
        return splitPoint;
    }

    private static int FindWordBoundary(string text, int nearPosition)
    {
        if (nearPosition >= text.Length)
            return text.Length;

        // Look for whitespace/punctuation near the position
        var searchStart = Math.Max(0, nearPosition - 50);
        var lastGoodSplit = searchStart;

        for (var i = searchStart; i <= nearPosition && i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]) || IsSplitPunctuation(text[i]))
            {
                lastGoodSplit = i + 1;
            }
        }

        return lastGoodSplit > searchStart ? lastGoodSplit : nearPosition;
    }

    private static bool IsSplitPunctuation(char c)
    {
        return c is '.' or '!' or '?' or ';' or '\n' or '\r';
    }
}
