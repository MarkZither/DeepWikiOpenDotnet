using DeepWiki.Rag.Core.Models;

namespace DeepWiki.Rag.Core.Services;

/// <summary>
/// Abstraction for pushing wiki generation progress events to connected clients in real-time.
/// Called by <see cref="WikiGenerationOrchestrator"/> alongside (or instead of) the Channel-based
/// async stream, allowing transports such as SignalR to deliver events independently of the
/// originating HTTP request lifetime and Polly resiliency pipelines.
/// </summary>
public interface IWikiProgressNotifier
{
    /// <summary>
    /// Broadcasts a progress event to all clients that are subscribed to updates for
    /// the wiki referenced by <paramref name="progress"/>.<see cref="WikiGenerationProgress.WikiId"/>.
    /// Implementations MUST NOT throw — failures should be swallowed or logged internally
    /// so that a notification failure never aborts the generation pipeline.
    /// </summary>
    Task NotifyAsync(WikiGenerationProgress progress, CancellationToken ct = default);
}
