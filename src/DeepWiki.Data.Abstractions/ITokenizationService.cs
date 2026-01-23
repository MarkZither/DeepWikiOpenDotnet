using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DeepWiki.Data.Abstractions;

/// <summary>
/// Microsoft Agent Framework-compatible tokenization service for counting tokens and chunking text.
/// Validates token counts before embedding calls to prevent agent tool failures.
/// All result types are JSON-serializable for use in agent context.
/// </summary>
public interface ITokenizationService
{
    /// <summary>
    /// Counts the number of tokens in the given text using the specified model's tokenizer.
    /// </summary>
    /// <param name="text">The text to count tokens for.</param>
    /// <param name="modelId">The model identifier (e.g., "gpt-4", "text-embedding-ada-002", "ollama/llama3").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of tokens in the text.</returns>
    Task<int> CountTokensAsync(string text, string modelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Chunks the text into segments that respect the specified maximum token limit.
    /// Preserves word boundaries (no mid-word splits).
    /// </summary>
    /// <param name="text">The text to chunk.</param>
    /// <param name="maxTokens">Maximum tokens per chunk (default: 8192 for embedding models).</param>
    /// <param name="modelId">The model identifier for tokenization (optional, defaults to cl100k_base).</param>
    /// <param name="parentId">Optional parent document ID for chunk metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Collection of text chunks with metadata.</returns>
    Task<IReadOnlyList<TextChunk>> ChunkAsync(
        string text,
        int maxTokens = 8192,
        string? modelId = null,
        Guid? parentId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the maximum token limit for the specified model.
    /// </summary>
    /// <param name="modelId">The model identifier.</param>
    /// <returns>The maximum token limit for the model.</returns>
    int GetMaxTokens(string modelId);
}

/// <summary>
/// Represents a chunk of text with metadata. JSON-serializable for agent context.
/// </summary>
public sealed class TextChunk
{
    /// <summary>
    /// The text content of this chunk.
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// Zero-based index of this chunk within the parent document.
    /// </summary>
    public int ChunkIndex { get; init; }

    /// <summary>
    /// The ID of the parent document this chunk was derived from.
    /// </summary>
    public Guid? ParentId { get; init; }

    /// <summary>
    /// The number of tokens in this chunk.
    /// </summary>
    public int TokenCount { get; init; }

    /// <summary>
    /// Detected or inferred language of the text (e.g., "en", "code").
    /// </summary>
    public string Language { get; init; } = "en";

    /// <summary>
    /// The character offset of this chunk in the original text.
    /// </summary>
    public int StartOffset { get; init; }

    /// <summary>
    /// The character length of this chunk.
    /// </summary>
    public int Length { get; init; }
}
