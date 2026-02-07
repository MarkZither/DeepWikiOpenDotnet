using DeepWiki.Data.Abstractions.Models;

namespace DeepWiki.Data.Abstractions;

/// <summary>
/// Core service interface for streaming generation with RAG (Retrieval-Augmented Generation).
/// Orchestrates document retrieval and token-by-token streaming from model providers.
/// </summary>
public interface IGenerationService
{
    /// <summary>
    /// Generates a streaming response for the given prompt with RAG context.
    /// Retrieves relevant documents via IVectorStore and streams token deltas from the configured model provider.
    /// </summary>
    /// <param name="sessionId">Target session identifier.</param>
    /// <param name="promptText">User prompt text.</param>
    /// <param name="topK">Number of documents to retrieve for context (default 5).</param>
    /// <param name="filters">Optional retrieval filters (e.g., repoUrl, filePath).</param>
    /// <param name="idempotencyKey">Optional retry-safe key. Returns cached response if duplicate.</param>
    /// <param name="cancellationToken">Cancellation token for aborting generation.</param>
    /// <returns>Async stream of GenerationDelta events (token, done, error).</returns>
    /// <exception cref="ArgumentException">Thrown if sessionId is invalid or prompt is empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown if provider is unavailable.</exception>
    /// <exception cref="TimeoutException">Thrown if generation exceeds timeout (30s default).</exception>
    IAsyncEnumerable<GenerationDelta> GenerateAsync(
        string sessionId,
        string promptText,
        int topK = 5,
        Dictionary<string, string>? filters = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to cancel an in-flight prompt for the given session.
    /// </summary>
    Task CancelAsync(string sessionId, string promptId);
}
