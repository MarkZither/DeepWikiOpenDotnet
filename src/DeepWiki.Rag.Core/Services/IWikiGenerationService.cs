using DeepWiki.Rag.Core.Models;

namespace DeepWiki.Rag.Core.Services;

/// <summary>
/// Two-phase wiki generation service.
/// Phase 1: generates a Table of Contents (TOC) from collection document summaries.
/// Phase 2: generates page content for each TOC entry, streaming progress events.
/// </summary>
public interface IWikiGenerationService
{
    /// <summary>
    /// Runs the full two-phase wiki generation pipeline for the given request.
    /// </summary>
    /// <param name="request">Generation parameters (collection, name, description).</param>
    /// <param name="cancellationToken">Cancellation token — partial results are saved on cancellation.</param>
    /// <returns>
    /// Async stream of <see cref="WikiGenerationProgress"/> events in order:
    /// wiki_created → toc_complete → (page_start → page_token* → page_complete | page_error)* → generation_complete | generation_cancelled
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown synchronously (before the first yield) when a concurrent generation for the
    /// same collection+name pair is already in progress (maps to HTTP 409).
    /// </exception>
    IAsyncEnumerable<WikiGenerationProgress> GenerateAsync(
        WikiGenerationRequest request,
        CancellationToken cancellationToken = default);
}
