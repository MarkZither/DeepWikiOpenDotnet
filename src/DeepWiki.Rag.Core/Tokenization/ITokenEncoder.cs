namespace DeepWiki.Rag.Core.Tokenization;

/// <summary>
/// Interface for provider-specific token encoding.
/// Each provider (OpenAI, Foundry, Ollama) may have different tokenization strategies.
/// </summary>
public interface ITokenEncoder
{
    /// <summary>
    /// Gets the name of this encoder (e.g., "cl100k_base", "o200k_base").
    /// </summary>
    string EncodingName { get; }

    /// <summary>
    /// Counts the number of tokens in the given text.
    /// </summary>
    /// <param name="text">The text to tokenize.</param>
    /// <returns>The number of tokens.</returns>
    int CountTokens(string text);

    /// <summary>
    /// Encodes the text into token IDs.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <returns>The token IDs.</returns>
    IReadOnlyList<int> Encode(string text);

    /// <summary>
    /// Decodes token IDs back into text.
    /// </summary>
    /// <param name="tokens">The token IDs to decode.</param>
    /// <returns>The decoded text.</returns>
    string Decode(IReadOnlyList<int> tokens);

    /// <summary>
    /// Finds the optimal split point within a text that results in at most maxTokens.
    /// Returns the character index where the split should occur.
    /// </summary>
    /// <param name="text">The text to find a split point in.</param>
    /// <param name="maxTokens">The maximum number of tokens allowed.</param>
    /// <returns>The character index of the split point, or the full text length if under limit.</returns>
    int FindSplitPoint(string text, int maxTokens);
}
