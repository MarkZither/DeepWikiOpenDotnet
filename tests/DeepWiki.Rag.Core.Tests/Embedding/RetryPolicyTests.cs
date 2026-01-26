using DeepWiki.Rag.Core.Embedding;
using Microsoft.Extensions.Logging;

namespace DeepWiki.Rag.Core.Tests.Embedding;

/// <summary>
/// Unit tests for RetryPolicy covering T114-T118.
/// Tests exponential backoff, cache fallback, jitter, and retry behavior.
/// </summary>
public class RetryPolicyTests
{
    #region T115: Retry logic executes 3 times with exponential backoff (100ms, 200ms, 400ms)

    [Fact]
    public void Default_MaxRetries_IsThree()
    {
        // Arrange & Act
        var policy = RetryPolicy.Default;

        // Assert
        Assert.Equal(3, policy.MaxRetries);
    }

    [Fact]
    public void Default_BaseDelayMs_Is100()
    {
        // Arrange & Act
        var policy = RetryPolicy.Default;

        // Assert
        Assert.Equal(100, policy.BaseDelayMs);
    }

    [Fact]
    public void Default_Multiplier_IsTwo()
    {
        // Arrange & Act
        var policy = RetryPolicy.Default;

        // Assert
        Assert.Equal(2.0, policy.Multiplier);
    }

    [Fact]
    public void GetExpectedDelays_ReturnsExponentialSequence()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            MaxRetries = 4,
            BaseDelayMs = 100,
            Multiplier = 2.0
        };

        // Act
        var delays = policy.GetExpectedDelays();

        // Assert
        // First attempt has no delay, so we get MaxRetries-1 delays
        Assert.Equal(3, delays.Length);
        Assert.Equal(100, delays[0]);  // 100 * 2^0
        Assert.Equal(200, delays[1]);  // 100 * 2^1
        Assert.Equal(400, delays[2]);  // 100 * 2^2
    }

    [Fact]
    public void CalculateDelay_FirstRetry_ReturnsBaseDelay()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            BaseDelayMs = 100,
            Multiplier = 2.0,
            JitterFactor = 0  // No jitter for predictable test
        };

        // Act
        var delay = policy.CalculateDelay(1);

        // Assert
        Assert.Equal(100, delay);
    }

    [Fact]
    public void CalculateDelay_SecondRetry_ReturnsDoubleBaseDelay()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            BaseDelayMs = 100,
            Multiplier = 2.0,
            JitterFactor = 0  // No jitter for predictable test
        };

        // Act
        var delay = policy.CalculateDelay(2);

        // Assert
        Assert.Equal(200, delay);
    }

    [Fact]
    public void CalculateDelay_ThirdRetry_ReturnsQuadrupleBaseDelay()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            BaseDelayMs = 100,
            Multiplier = 2.0,
            JitterFactor = 0  // No jitter for predictable test
        };

        // Act
        var delay = policy.CalculateDelay(3);

        // Assert
        Assert.Equal(400, delay);
    }

    [Fact]
    public void CalculateDelay_CapsAtMaxDelay()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            BaseDelayMs = 1000,
            Multiplier = 10.0,
            MaxDelayMs = 5000,
            JitterFactor = 0
        };

        // Act - attempt 3 would be 1000 * 10^2 = 100,000ms but should cap at 5000
        var delay = policy.CalculateDelay(3);

        // Assert
        Assert.Equal(5000, delay);
    }

    #endregion

    #region T116: Retry logic falls back to cached embedding on 3rd failure

    [Fact]
    public void WithCache_CreatesRetryPolicyWithCacheFallback()
    {
        // Arrange
        var cache = new EmbeddingCache();

        // Act
        var policy = RetryPolicy.WithCache(cache);

        // Assert
        Assert.True(policy.UseCacheFallback);
    }

    [Fact]
    public async Task ExecuteEmbeddingAsync_WithCachedValue_ReturnsCachedOnAllFailures()
    {
        // Arrange
        var cache = new EmbeddingCache();
        var cachedEmbedding = CreateValidEmbedding();
        var text = "test text for cache";
        var modelId = "test-model";
        await cache.SetAsync(text, modelId, cachedEmbedding);

        var policy = new RetryPolicy(cache)
        {
            MaxRetries = 2, // Minimize retries
            BaseDelayMs = 1, // Minimize delays
            UseCacheFallback = true
        };

        var attemptCount = 0;

        // Act - operation always fails
        var result = await policy.ExecuteEmbeddingAsync(
            operation: async ct =>
            {
                attemptCount++;
                await Task.Delay(1, ct); // Small delay
                throw new HttpRequestException("Simulated failure");
            },
            text: text,
            modelId: modelId,
            provider: "test-provider");

        // Assert
        Assert.Equal(2, attemptCount); // All retries exhausted
        Assert.Equal(cachedEmbedding.Length, result.Length);
        Assert.Equal(cachedEmbedding, result);
    }

    #endregion

    #region T117: Retry logic throws after 3 failures if no cache available

    [Fact]
    public async Task ExecuteEmbeddingAsync_NoCache_ThrowsAfterAllRetries()
    {
        // Arrange
        var policy = new RetryPolicy(cache: null)
        {
            MaxRetries = 2,
            BaseDelayMs = 1,
            UseCacheFallback = false
        };

        var attemptCount = 0;

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.ExecuteEmbeddingAsync(
                operation: async ct =>
                {
                    attemptCount++;
                    await Task.Delay(1, ct);
                    throw new HttpRequestException("Simulated failure");
                },
                text: "test",
                modelId: "model",
                provider: "test-provider"));

        Assert.Equal(2, attemptCount);
        Assert.Contains("failed after 2 retries", exception.Message);
        Assert.Contains("test-provider", exception.Message);
    }

    [Fact]
    public async Task ExecuteEmbeddingAsync_NoCacheHit_ThrowsAfterAllRetries()
    {
        // Arrange - cache exists but has no entry for this text
        var cache = new EmbeddingCache();
        var policy = new RetryPolicy(cache)
        {
            MaxRetries = 2,
            BaseDelayMs = 1,
            UseCacheFallback = true
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.ExecuteEmbeddingAsync(
                operation: async ct =>
                {
                    await Task.Delay(1, ct);
                    throw new HttpRequestException("Simulated failure");
                },
                text: "uncached-text",
                modelId: "model",
                provider: "test-provider"));

        Assert.Contains("failed after 2 retries", exception.Message);
    }

    [Fact]
    public async Task ExecuteEmbeddingAsync_Success_DoesNotRetry()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            MaxRetries = 3,
            BaseDelayMs = 1000 // Would slow test if retries happen
        };

        var attemptCount = 0;
        var expectedEmbedding = CreateValidEmbedding();

        // Act
        var result = await policy.ExecuteEmbeddingAsync(
            operation: async ct =>
            {
                attemptCount++;
                await Task.Delay(1, ct);
                return expectedEmbedding;
            },
            text: "test",
            modelId: "model",
            provider: "test-provider");

        // Assert
        Assert.Equal(1, attemptCount); // Only one attempt - success on first try
        Assert.Equal(expectedEmbedding, result);
    }

    [Fact]
    public async Task ExecuteEmbeddingAsync_SuccessAfterRetry_CachesResult()
    {
        // Arrange
        var cache = new EmbeddingCache();
        var policy = new RetryPolicy(cache)
        {
            MaxRetries = 3,
            BaseDelayMs = 1
        };

        var attemptCount = 0;
        var expectedEmbedding = CreateValidEmbedding();
        var text = "text to cache";
        var modelId = "test-model";

        // Act - fail first, succeed second
        var result = await policy.ExecuteEmbeddingAsync(
            operation: async ct =>
            {
                attemptCount++;
                await Task.Delay(1, ct);
                if (attemptCount < 2) throw new HttpRequestException("First attempt fails");
                return expectedEmbedding;
            },
            text: text,
            modelId: modelId,
            provider: "test-provider");

        // Assert
        Assert.Equal(2, attemptCount);
        Assert.Equal(expectedEmbedding, result);

        // Verify result was cached
        var cached = await cache.GetAsync(text, modelId);
        Assert.NotNull(cached);
        Assert.Equal(expectedEmbedding, cached);
    }

    #endregion

    #region T118: Jitter (±20%) applied to backoff delays

    [Fact]
    public void Default_JitterFactor_Is20Percent()
    {
        // Arrange & Act
        var policy = RetryPolicy.Default;

        // Assert
        Assert.Equal(0.20, policy.JitterFactor);
    }

    [Fact]
    public void CalculateDelay_WithJitter_ReturnsValueInRange()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            BaseDelayMs = 1000,
            Multiplier = 1.0, // No multiplier to simplify
            JitterFactor = 0.20,
            MaxDelayMs = 10000
        };

        // Act - calculate multiple delays to verify jitter
        var delays = new List<int>();
        for (int i = 0; i < 100; i++)
        {
            delays.Add(policy.CalculateDelay(1));
        }

        // Assert - all delays should be within ±20% of 1000 (800-1200)
        var minExpected = 800;  // 1000 * (1 - 0.20)
        var maxExpected = 1200; // 1000 * (1 + 0.20)

        Assert.All(delays, d =>
        {
            Assert.InRange(d, minExpected, maxExpected);
        });

        // With 100 samples, we should see some variation (not all same value)
        var uniqueDelays = delays.Distinct().Count();
        Assert.True(uniqueDelays > 1, "Jitter should produce some variation in delays");
    }

    [Fact]
    public void CalculateDelay_ZeroJitter_ReturnsExactValue()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            BaseDelayMs = 1000,
            Multiplier = 1.0,
            JitterFactor = 0,
            MaxDelayMs = 10000
        };

        // Act - calculate multiple delays
        var delays = new List<int>();
        for (int i = 0; i < 10; i++)
        {
            delays.Add(policy.CalculateDelay(1));
        }

        // Assert - all delays should be exactly 1000 with no jitter
        Assert.All(delays, d => Assert.Equal(1000, d));
    }

    #endregion

    #region Additional retry policy tests

    [Fact]
    public async Task ExecuteEmbeddingAsync_CancellationRequested_ThrowsImmediately()
    {
        // Arrange
        var policy = new RetryPolicy { MaxRetries = 3 };
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            policy.ExecuteEmbeddingAsync(
                operation: async ct =>
                {
                    await Task.Delay(1000, ct);
                    return CreateValidEmbedding();
                },
                text: "test",
                modelId: "model",
                provider: "test-provider",
                cancellationToken: cts.Token));
    }

    [Fact]
    public async Task ExecuteEmbeddingAsync_CancellationDuringRetry_ThrowsOperationCanceled()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            MaxRetries = 5,
            BaseDelayMs = 100
        };
        var cts = new CancellationTokenSource();
        var attemptCount = 0;

        // Act & Assert
        // TaskCanceledException derives from OperationCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await policy.ExecuteEmbeddingAsync(
                operation: async ct =>
                {
                    attemptCount++;
                    if (attemptCount >= 2)
                    {
                        cts.Cancel();
                    }
                    await Task.Delay(10, ct);
                    throw new HttpRequestException("Simulated failure");
                },
                text: "test",
                modelId: "model",
                provider: "test-provider",
                cancellationToken: cts.Token);
        });

        Assert.True(attemptCount >= 2);
    }

    [Fact]
    public void Constructor_WithLogger_AcceptsLogger()
    {
        // Arrange
        using var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<RetryPolicy>();
        var cache = new EmbeddingCache();

        // Act
        var policy = new RetryPolicy(cache, logger);

        // Assert
        Assert.NotNull(policy);
    }

    [Fact]
    public async Task ExecuteAsync_GenericVersion_WorksCorrectly()
    {
        // Arrange
        var policy = new RetryPolicy { MaxRetries = 3, BaseDelayMs = 1 };
        var expectedResult = "test result";

        // Act
        var result = await policy.ExecuteAsync(
            operation: async ct =>
            {
                await Task.Delay(1, ct);
                return expectedResult;
            },
            getCacheKey: null,
            operationName: "test operation");

        // Assert
        Assert.Equal(expectedResult, result);
    }

    [Fact]
    public async Task ExecuteAsync_GenericVersion_RetriesOnFailure()
    {
        // Arrange
        var policy = new RetryPolicy { MaxRetries = 3, BaseDelayMs = 1 };
        var attemptCount = 0;

        // Act
        var result = await policy.ExecuteAsync<string>(
            operation: async ct =>
            {
                attemptCount++;
                await Task.Delay(1, ct);
                if (attemptCount < 2) throw new Exception("Simulated failure");
                return "success";
            },
            getCacheKey: null,
            operationName: "test operation");

        // Assert
        Assert.Equal(2, attemptCount);
        Assert.Equal("success", result);
    }

    #endregion

    #region Helpers

    private static float[] CreateValidEmbedding()
    {
        // Create a 1536-dimensional embedding
        var embedding = new float[1536];
        var random = new Random(42); // Deterministic for testing
        for (int i = 0; i < embedding.Length; i++)
        {
            embedding[i] = (float)(random.NextDouble() * 2 - 1);
        }
        return embedding;
    }

    #endregion
}
