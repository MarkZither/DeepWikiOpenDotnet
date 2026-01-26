using System.Text;
using System.Text.RegularExpressions;
using DeepWiki.Data.Abstractions;
using Microsoft.Extensions.Logging;

namespace DeepWiki.Rag.Core.Tokenization;

/// <summary>
/// Text chunker that splits documents into token-limited segments while preserving word boundaries.
/// Supports metadata tagging for chunk tracking and document reassembly.
/// </summary>
public sealed partial class Chunker
{
    private readonly ITokenEncoder _encoder;
    private readonly ILogger<Chunker>? _logger;

    // Characters that are valid split points
    private static readonly char[] SentenceEnders = ['.', '!', '?', ';'];
    private static readonly char[] ParagraphEnders = ['\n', '\r'];

    /// <summary>
    /// Creates a new chunker with the specified token encoder.
    /// </summary>
    /// <param name="encoder">The token encoder to use for counting tokens.</param>
    /// <param name="logger">Optional logger.</param>
    public Chunker(ITokenEncoder encoder, ILogger<Chunker>? logger = null)
    {
        _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
        _logger = logger;
    }

    /// <summary>
    /// Chunks text into segments that respect the maximum token limit.
    /// Preserves word boundaries and attempts to split at sentence/paragraph boundaries when possible.
    /// </summary>
    /// <param name="text">The text to chunk.</param>
    /// <param name="maxTokens">Maximum tokens per chunk.</param>
    /// <param name="parentId">Optional parent document ID for metadata.</param>
    /// <param name="language">Optional language hint for metadata.</param>
    /// <returns>List of text chunks with metadata.</returns>
    public IReadOnlyList<TextChunk> ChunkText(
        string text,
        int maxTokens = 8192,
        Guid? parentId = null,
        string language = "en")
    {
        if (string.IsNullOrEmpty(text))
            return [];

        if (maxTokens <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxTokens), "Max tokens must be positive");

        var chunks = new List<TextChunk>();
        var currentOffset = 0;
        var chunkIndex = 0;

        _logger?.LogDebug("Starting chunking of text with {Length} characters, max tokens: {MaxTokens}",
            text.Length, maxTokens);

        while (currentOffset < text.Length)
        {
            var remainingText = text[currentOffset..];
            var tokenCount = _encoder.CountTokens(remainingText);

            // If remaining text fits in one chunk, add it all
            if (tokenCount <= maxTokens)
            {
                var chunk = CreateChunk(remainingText.Trim(), chunkIndex, parentId, language, currentOffset);
                if (!string.IsNullOrWhiteSpace(chunk.Text))
                {
                    chunks.Add(chunk);
                }
                break;
            }

            // Find the best split point
            var splitPoint = FindBestSplitPoint(remainingText, maxTokens);

            if (splitPoint <= 0)
            {
                // Couldn't find a good split - force split at token boundary
                splitPoint = _encoder.FindSplitPoint(remainingText, maxTokens);
                if (splitPoint <= 0)
                {
                    // Last resort: just take what we can
                    splitPoint = Math.Min(remainingText.Length, maxTokens * 4); // ~4 chars per token estimate
                }
                _logger?.LogWarning("Forced split at position {Position} - no word boundary found", splitPoint);
            }

            var chunkText = remainingText[..splitPoint].Trim();
            if (!string.IsNullOrWhiteSpace(chunkText))
            {
                var chunk = CreateChunk(chunkText, chunkIndex, parentId, language, currentOffset);
                chunks.Add(chunk);
                chunkIndex++;
            }

            currentOffset += splitPoint;

            // Skip leading whitespace in next chunk
            while (currentOffset < text.Length && char.IsWhiteSpace(text[currentOffset]))
            {
                currentOffset++;
            }
        }

        _logger?.LogDebug("Chunking complete: {ChunkCount} chunks created", chunks.Count);
        return chunks;
    }

    /// <summary>
    /// Validates that a chunk does not contain mid-word splits.
    /// </summary>
    /// <param name="chunk">The chunk to validate.</param>
    /// <param name="originalText">The original full text.</param>
    /// <returns>True if the chunk has clean word boundaries.</returns>
    public static bool ValidateWordBoundaries(TextChunk chunk, string originalText)
    {
        if (string.IsNullOrEmpty(chunk.Text) || string.IsNullOrEmpty(originalText))
            return true;

        var startOffset = chunk.StartOffset;
        var endOffset = startOffset + chunk.Length;

        // Check start boundary (should be at start of text or after whitespace/punctuation)
        if (startOffset > 0)
        {
            var prevChar = originalText[startOffset - 1];
            var currChar = originalText[startOffset];
            if (!char.IsWhiteSpace(prevChar) && !char.IsPunctuation(prevChar) &&
                !char.IsWhiteSpace(currChar) && char.IsLetterOrDigit(currChar))
            {
                return false; // Mid-word split at start
            }
        }

        // Check end boundary (should be at end of text or before whitespace/punctuation)
        if (endOffset < originalText.Length)
        {
            var currChar = originalText[endOffset - 1];
            var nextChar = originalText[endOffset];
            if (!char.IsWhiteSpace(currChar) && !char.IsPunctuation(currChar) &&
                char.IsLetterOrDigit(currChar) && char.IsLetterOrDigit(nextChar))
            {
                return false; // Mid-word split at end
            }
        }

        return true;
    }

    private TextChunk CreateChunk(string text, int index, Guid? parentId, string language, int startOffset)
    {
        return new TextChunk
        {
            Text = text,
            ChunkIndex = index,
            ParentId = parentId,
            TokenCount = _encoder.CountTokens(text),
            Language = language,
            StartOffset = startOffset,
            Length = text.Length
        };
    }

    private int FindBestSplitPoint(string text, int maxTokens)
    {
        // First, find the maximum character position that fits within token limit
        var maxCharPos = _encoder.FindSplitPoint(text, maxTokens);

        if (maxCharPos <= 0 || maxCharPos >= text.Length)
            return maxCharPos;

        // Try to find a paragraph break first (best split point)
        var paragraphSplit = FindLastSplitBefore(text, maxCharPos, ParagraphEnders);
        if (paragraphSplit > maxCharPos / 2) // Only use if we're not losing too much content
            return paragraphSplit;

        // Try to find a sentence break
        var sentenceSplit = FindLastSplitBefore(text, maxCharPos, SentenceEnders);
        if (sentenceSplit > maxCharPos / 2)
            return sentenceSplit;

        // Fall back to word boundary (whitespace)
        var wordSplit = FindLastWhitespaceBefore(text, maxCharPos);
        if (wordSplit > 0)
            return wordSplit;

        // Last resort: use the token-calculated position
        return maxCharPos;
    }

    private static int FindLastSplitBefore(string text, int maxPos, char[] splitChars)
    {
        var lastSplit = -1;
        for (var i = 0; i < maxPos && i < text.Length; i++)
        {
            if (Array.IndexOf(splitChars, text[i]) >= 0)
            {
                lastSplit = i + 1; // Include the split character
            }
        }
        return lastSplit;
    }

    private static int FindLastWhitespaceBefore(string text, int maxPos)
    {
        for (var i = Math.Min(maxPos, text.Length - 1); i >= 0; i--)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                return i + 1; // Split after whitespace
            }
        }
        return -1;
    }

    /// <summary>
    /// Detects the probable language of the text based on character analysis.
    /// </summary>
    /// <param name="text">The text to analyze.</param>
    /// <returns>Language code (e.g., "en", "code", "mixed").</returns>
    public static string DetectLanguage(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "en";

        // Simple heuristics for language detection
        var codeIndicators = 0;
        var totalChars = 0;

        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                totalChars++;
                // Code-like characters
                if (c is '{' or '}' or '[' or ']' or '(' or ')' or ';' or '=' or '<' or '>')
                    codeIndicators++;
            }
        }

        // Check for code patterns
        if (CodePatternRegex().IsMatch(text))
            return "code";

        if (totalChars > 0 && (double)codeIndicators / totalChars > 0.05)
            return "code";

        // Default to English
        return "en";
    }

    [GeneratedRegex(@"(function\s+\w+|class\s+\w+|def\s+\w+|public\s+|private\s+|import\s+|using\s+|#include)", RegexOptions.IgnoreCase)]
    private static partial Regex CodePatternRegex();
}
