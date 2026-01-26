using DeepWiki.Data.Abstractions;

namespace DeepWiki.Rag.Core.Tokenization;

/// <summary>
/// No-op implementation of ITokenizationService for testing and fallback scenarios.
/// Returns reasonable defaults without performing actual tokenization.
/// </summary>
public class NoOpTokenizationService : ITokenizationService
{
    /// <inheritdoc />
    public Task<int> CountTokensAsync(string text, string modelId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
            return Task.FromResult(0);

        // Rough estimate: ~4 characters per token for English text
        var estimate = (int)Math.Ceiling(text.Length / 4.0);
        return Task.FromResult(estimate);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TextChunk>> ChunkAsync(
        string text,
        int maxTokens = 8192,
        string? modelId = null,
        Guid? parentId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
            return Task.FromResult<IReadOnlyList<TextChunk>>([]);

        // Simple implementation: just return the whole text as one chunk if under limit
        var estimatedTokens = (int)Math.Ceiling(text.Length / 4.0);
        var chunk = new TextChunk
        {
            Text = text,
            ChunkIndex = 0,
            ParentId = parentId,
            TokenCount = estimatedTokens,
            Language = "en",
            StartOffset = 0,
            Length = text.Length
        };

        return Task.FromResult<IReadOnlyList<TextChunk>>([chunk]);
    }

    /// <inheritdoc />
    public int GetMaxTokens(string modelId)
    {
        return TokenizationConfig.GetMaxTokens(modelId);
    }
}
