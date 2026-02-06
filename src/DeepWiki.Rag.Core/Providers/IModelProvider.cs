using DeepWiki.Data.Abstractions.Models;

namespace DeepWiki.Rag.Core.Providers;

/// <summary>
/// Abstraction for LLM model providers that support streaming token generation.
/// Implementations adapt provider-specific streaming APIs (Ollama, OpenAI, etc.) to a common interface.
/// </summary>
public interface IModelProvider
{
    /// <summary>
    /// Provider name for identification and logging (e.g., "Ollama", "OpenAI").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Checks if the provider is available and healthy.
    /// Used for health checks and provider selection logic.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if provider is reachable and ready, false otherwise.</returns>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams token deltas from the provider for the given prompt.
    /// Implementations must handle provider-specific formats (NDJSON, SDK streaming, etc.)
    /// and map to GenerationDelta events.
    /// </summary>
    /// <param name="promptText">User prompt including RAG context.</param>
    /// <param name="systemPrompt">Optional system prompt for role/behavior instructions.</param>
    /// <param name="cancellationToken">Cancellation token for aborting generation.</param>
    /// <returns>Async stream of GenerationDelta events.</returns>
    /// <exception cref="InvalidOperationException">Thrown if provider is unavailable.</exception>
    /// <exception cref="TimeoutException">Thrown if provider stalls (30s default).</exception>
    IAsyncEnumerable<GenerationDelta> StreamAsync(
        string promptText,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default);
}
