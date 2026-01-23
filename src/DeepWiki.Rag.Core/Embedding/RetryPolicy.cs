using Microsoft.Extensions.Logging;

namespace DeepWiki.Rag.Core.Embedding;

/// <summary>
/// Retry policy with exponential backoff for embedding service calls.
/// Supports fallback to cached embeddings when all retries fail.
/// </summary>
public sealed class RetryPolicy
{
    private readonly IEmbeddingCache? _cache;
    private readonly ILogger<RetryPolicy>? _logger;
    private readonly Random _jitterRandom = new();

    /// <summary>
    /// Gets the maximum number of retry attempts.
    /// </summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>
    /// Gets the base delay in milliseconds for exponential backoff.
    /// </summary>
    public int BaseDelayMs { get; init; } = 100;

    /// <summary>
    /// Gets the maximum delay in milliseconds between retries.
    /// </summary>
    public int MaxDelayMs { get; init; } = 10_000;

    /// <summary>
    /// Gets the backoff multiplier (delay is multiplied by this each retry).
    /// </summary>
    public double Multiplier { get; init; } = 2.0;

    /// <summary>
    /// Gets the jitter factor (±percentage applied to delays).
    /// </summary>
    public double JitterFactor { get; init; } = 0.20;

    /// <summary>
    /// Gets or sets whether to use cached fallback when all retries fail.
    /// </summary>
    public bool UseCacheFallback { get; init; } = true;

    /// <summary>
    /// Creates a new retry policy.
    /// </summary>
    /// <param name="cache">Optional embedding cache for fallback.</param>
    /// <param name="logger">Optional logger.</param>
    public RetryPolicy(IEmbeddingCache? cache = null, ILogger<RetryPolicy>? logger = null)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Creates a retry policy with default settings.
    /// </summary>
    public static RetryPolicy Default => new();

    /// <summary>
    /// Creates a retry policy with cache fallback.
    /// </summary>
    public static RetryPolicy WithCache(IEmbeddingCache cache, ILogger<RetryPolicy>? logger = null)
        => new(cache, logger) { UseCacheFallback = true };

    /// <summary>
    /// Executes an operation with retry logic and optional cache fallback.
    /// </summary>
    /// <typeparam name="T">The return type of the operation.</typeparam>
    /// <param name="operation">The operation to execute.</param>
    /// <param name="getCacheKey">Function to get cache lookup parameters (text, modelId) for fallback.</param>
    /// <param name="operationName">Name of the operation for logging.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The operation result.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when all retries fail and no cached value is available.
    /// </exception>
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        Func<(string text, string modelId)>? getCacheKey,
        string operationName,
        CancellationToken cancellationToken = default) where T : class
    {
        Exception? lastException = null;
        var attempt = 0;

        while (attempt < MaxRetries)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (attempt > 0)
                {
                    var delay = CalculateDelay(attempt);
                    _logger?.LogDebug(
                        "Retry attempt {Attempt}/{MaxRetries} for {Operation} after {Delay}ms",
                        attempt, MaxRetries, operationName, delay);
                    await Task.Delay(delay, cancellationToken);
                }

                attempt++;
                return await operation(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger?.LogWarning(ex,
                    "Attempt {Attempt}/{MaxRetries} failed for {Operation}: {Error}",
                    attempt, MaxRetries, operationName, ex.Message);
            }
        }

        // All retries exhausted - try cache fallback
        if (UseCacheFallback && _cache is not null && getCacheKey is not null)
        {
            var (text, modelId) = getCacheKey();
            _logger?.LogInformation(
                "All {MaxRetries} retries failed for {Operation}, attempting cache fallback",
                MaxRetries, operationName);

            var cached = await _cache.GetAsync(text, modelId, cancellationToken);
            if (cached is not null && cached is T cachedResult)
            {
                _logger?.LogInformation(
                    "Cache fallback successful for {Operation}",
                    operationName);
                return cachedResult;
            }

            _logger?.LogWarning(
                "Cache fallback failed - no cached value for {Operation}",
                operationName);
        }

        throw new InvalidOperationException(
            $"{operationName} failed after {MaxRetries} retries. Last error: {lastException?.Message}",
            lastException);
    }

    /// <summary>
    /// Executes an embedding operation with retry logic and cache fallback.
    /// </summary>
    /// <param name="operation">The embedding operation to execute.</param>
    /// <param name="text">The input text (for cache lookup).</param>
    /// <param name="modelId">The model ID (for cache lookup).</param>
    /// <param name="provider">The provider name for logging.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The embedding vector.</returns>
    public async Task<float[]> ExecuteEmbeddingAsync(
        Func<CancellationToken, Task<float[]>> operation,
        string text,
        string modelId,
        string provider,
        CancellationToken cancellationToken = default)
    {
        Exception? lastException = null;
        var attempt = 0;

        while (attempt < MaxRetries)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (attempt > 0)
                {
                    var delay = CalculateDelay(attempt);
                    _logger?.LogDebug(
                        "Retry attempt {Attempt}/{MaxRetries} for embedding [{Provider}] after {Delay}ms",
                        attempt, MaxRetries, provider, delay);
                    await Task.Delay(delay, cancellationToken);
                }

                attempt++;
                var result = await operation(cancellationToken);

                // Cache successful result for future fallback
                if (_cache is not null)
                {
                    await _cache.SetAsync(text, modelId, result, cancellationToken: cancellationToken);
                }

                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger?.LogWarning(ex,
                    "Attempt {Attempt}/{MaxRetries} failed for embedding [{Provider}]: {Error}",
                    attempt, MaxRetries, provider, ex.Message);
            }
        }

        // All retries exhausted - try cache fallback
        if (UseCacheFallback && _cache is not null)
        {
            _logger?.LogInformation(
                "All {MaxRetries} retries failed for embedding [{Provider}], attempting cache fallback",
                MaxRetries, provider);

            var cached = await _cache.GetAsync(text, modelId, cancellationToken);
            if (cached is not null)
            {
                _logger?.LogInformation(
                    "Cache fallback successful for embedding [{Provider}]",
                    provider);
                return cached;
            }

            _logger?.LogWarning(
                "Cache fallback failed - no cached embedding for [{Provider}]",
                provider);
        }

        throw new InvalidOperationException(
            $"Embedding failed after {MaxRetries} retries using [{provider}]. " +
            $"Model: {modelId}. Last error: {lastException?.Message}",
            lastException);
    }

    /// <summary>
    /// Calculates the delay for a retry attempt using exponential backoff with jitter.
    /// </summary>
    /// <param name="attempt">The retry attempt number (1-based).</param>
    /// <returns>The delay in milliseconds.</returns>
    public int CalculateDelay(int attempt)
    {
        // Exponential: baseDelay * multiplier^(attempt-1)
        var exponentialDelay = BaseDelayMs * Math.Pow(Multiplier, attempt - 1);

        // Apply jitter: ±JitterFactor%
        var jitter = 1.0 + ((_jitterRandom.NextDouble() * 2 - 1) * JitterFactor);
        var delayWithJitter = exponentialDelay * jitter;

        // Cap at max delay
        return (int)Math.Min(delayWithJitter, MaxDelayMs);
    }

    /// <summary>
    /// Gets the expected delays for all retry attempts (for testing).
    /// </summary>
    /// <returns>Array of base delays without jitter for each attempt.</returns>
    public int[] GetExpectedDelays()
    {
        var delays = new int[MaxRetries - 1]; // First attempt has no delay
        for (int i = 1; i < MaxRetries; i++)
        {
            var exponentialDelay = BaseDelayMs * Math.Pow(Multiplier, i - 1);
            delays[i - 1] = (int)Math.Min(exponentialDelay, MaxDelayMs);
        }
        return delays;
    }
}
