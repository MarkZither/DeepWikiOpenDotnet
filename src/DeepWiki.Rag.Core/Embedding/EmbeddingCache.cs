using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace DeepWiki.Rag.Core.Embedding;

/// <summary>
/// In-memory implementation of IEmbeddingCache with TTL support.
/// Provides resilience by caching successful embeddings for fallback.
/// </summary>
public sealed class EmbeddingCache : IEmbeddingCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly ILogger<EmbeddingCache>? _logger;
    private readonly TimeSpan _defaultTtl;
    private readonly int _maxEntries;
    private readonly object _cleanupLock = new();
    private DateTime _lastCleanup = DateTime.UtcNow;
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Creates a new embedding cache.
    /// </summary>
    /// <param name="defaultTtl">Default time-to-live for cache entries. Defaults to 1 hour.</param>
    /// <param name="maxEntries">Maximum number of entries before eviction. Defaults to 10,000.</param>
    /// <param name="logger">Optional logger.</param>
    public EmbeddingCache(TimeSpan? defaultTtl = null, int maxEntries = 10_000, ILogger<EmbeddingCache>? logger = null)
    {
        _defaultTtl = defaultTtl ?? TimeSpan.FromHours(1);
        _maxEntries = maxEntries;
        _logger = logger;
    }

    /// <inheritdoc />
    public int Count => _cache.Count;

    /// <inheritdoc />
    public Task<float[]?> GetAsync(string text, string modelId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TryCleanupExpired();

        var key = ComputeKey(text, modelId);

        if (_cache.TryGetValue(key, out var entry))
        {
            if (entry.ExpiresAt > DateTime.UtcNow)
            {
                _logger?.LogDebug("Cache hit for embedding (model: {ModelId}, key: {Key})", modelId, key[..16]);
                return Task.FromResult<float[]?>(entry.Embedding);
            }

            // Entry expired - remove it
            _cache.TryRemove(key, out _);
            _logger?.LogDebug("Cache entry expired for (model: {ModelId}, key: {Key})", modelId, key[..16]);
        }

        _logger?.LogDebug("Cache miss for embedding (model: {ModelId})", modelId);
        return Task.FromResult<float[]?>(null);
    }

    /// <inheritdoc />
    public Task SetAsync(string text, string modelId, float[] embedding, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TryCleanupExpired();

        // Evict if at capacity
        if (_cache.Count >= _maxEntries)
        {
            EvictOldest();
        }

        var key = ComputeKey(text, modelId);
        var effectiveTtl = ttl ?? _defaultTtl;
        var entry = new CacheEntry(embedding, DateTime.UtcNow.Add(effectiveTtl));

        _cache[key] = entry;
        _logger?.LogDebug("Cached embedding (model: {ModelId}, key: {Key}, TTL: {TTL})", modelId, key[..16], effectiveTtl);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveAsync(string text, string modelId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var key = ComputeKey(text, modelId);
        if (_cache.TryRemove(key, out _))
        {
            _logger?.LogDebug("Removed cached embedding (model: {ModelId}, key: {Key})", modelId, key[..16]);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ClearAsync(string? modelId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (modelId is null)
        {
            var count = _cache.Count;
            _cache.Clear();
            _logger?.LogInformation("Cleared all {Count} cached embeddings", count);
        }
        else
        {
            // Clear only entries for specific model (model prefix in key)
            var prefix = $"{modelId}:";
            var keysToRemove = _cache.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            foreach (var key in keysToRemove)
            {
                _cache.TryRemove(key, out _);
            }
            _logger?.LogInformation("Cleared {Count} cached embeddings for model {ModelId}", keysToRemove.Count, modelId);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Computes a cache key from text and model ID using SHA256 hash.
    /// </summary>
    private static string ComputeKey(string text, string modelId)
    {
        var input = $"{modelId}:{text}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return $"{modelId}:{Convert.ToHexString(hashBytes)}";
    }

    /// <summary>
    /// Periodically cleans up expired entries.
    /// </summary>
    private void TryCleanupExpired()
    {
        if (DateTime.UtcNow - _lastCleanup < _cleanupInterval)
            return;

        lock (_cleanupLock)
        {
            if (DateTime.UtcNow - _lastCleanup < _cleanupInterval)
                return;

            var now = DateTime.UtcNow;
            var expiredKeys = _cache
                .Where(kvp => kvp.Value.ExpiresAt <= now)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                _cache.TryRemove(key, out _);
            }

            _lastCleanup = DateTime.UtcNow;

            if (expiredKeys.Count > 0)
            {
                _logger?.LogDebug("Cleaned up {Count} expired cache entries", expiredKeys.Count);
            }
        }
    }

    /// <summary>
    /// Evicts oldest entries when cache is at capacity.
    /// </summary>
    private void EvictOldest()
    {
        // Remove oldest 10% of entries
        var toRemove = Math.Max(1, _cache.Count / 10);
        var oldestKeys = _cache
            .OrderBy(kvp => kvp.Value.ExpiresAt)
            .Take(toRemove)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in oldestKeys)
        {
            _cache.TryRemove(key, out _);
        }

        _logger?.LogDebug("Evicted {Count} oldest cache entries", oldestKeys.Count);
    }

    /// <summary>
    /// Internal cache entry with embedding and expiration time.
    /// </summary>
    private sealed record CacheEntry(float[] Embedding, DateTime ExpiresAt);
}
