namespace DeepWiki.Rag.Core.Providers;

/// <summary>
/// Thrown when an LLM or embedding provider returns HTTP 429 Too Many Requests.
/// Carries the <see cref="RetryAfter"/> duration extracted from the response's
/// <c>Retry-After</c> header so callers can schedule the next attempt correctly
/// instead of burning quota with immediate retries.
/// </summary>
public sealed class RateLimitException : Exception
{
    /// <summary>
    /// The amount of time callers should wait before retrying, as specified by the
    /// provider's <c>Retry-After</c> header. <see langword="null"/> when the header
    /// was absent; callers should fall back to a conservative default (e.g. 60 s).
    /// </summary>
    public TimeSpan? RetryAfter { get; }

    public RateLimitException(TimeSpan? retryAfter, string message, Exception? inner = null)
        : base(message, inner)
    {
        RetryAfter = retryAfter;
    }
}
