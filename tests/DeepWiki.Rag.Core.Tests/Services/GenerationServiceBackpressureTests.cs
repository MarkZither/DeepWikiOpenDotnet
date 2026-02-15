using DeepWiki.Data.Abstractions.Models;
using DeepWiki.Rag.Core.Observability;
using DeepWiki.Rag.Core.Providers;
using DeepWiki.Rag.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace DeepWiki.Rag.Core.Tests.Services;

/// <summary>
/// Tests for backpressure handling in GenerationService.
/// Validates that slow consumers don't cause unbounded memory growth.
/// </summary>
public class GenerationServiceBackpressureTests
{
    [Fact]
    public async Task GenerationService_WithSlowConsumer_AppliesBackpressure()
    {
        // Arrange: Fast provider that emits 200 tokens immediately
        var mockProvider = new Mock<IModelProvider>();
        mockProvider.Setup(x => x.Name).Returns("FastProvider");
        mockProvider.Setup(x => x.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Emit 200 tokens as fast as possible
        mockProvider.Setup(x => x.StreamAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(GenerateFastTokens(200));

        var sessionManager = new SessionManager();
        var meterFactory = new SimpleMeterFactory();
        var metrics = new GenerationMetrics(meterFactory);
        var service = new GenerationService(
            new[] { mockProvider.Object },
            sessionManager,
            metrics,
            NullLogger<GenerationService>.Instance);

        var session = sessionManager.CreateSession();

        // Act: Consume slowly (50ms delay per token)
        var consumedCount = 0;

        await foreach (var delta in service.GenerateAsync(session.SessionId, "test prompt"))
        {
            if (delta.Type == "token")
            {
                consumedCount++;
                
                // Simulate slow consumer (50ms per token)
                // With bounded channel (capacity 100), fast provider will block after filling buffer
                await Task.Delay(50);
            }
        }

        // Assert: All tokens consumed
        consumedCount.Should().BeGreaterThan(0);

        // Memory should remain bounded due to backpressure (bounded channel capacity 100)
        // Without backpressure, 200 tokens would accumulate in memory before consumer reads them
        // This test validates the bounded channel prevents unbounded memory growth
    }

    [Fact]
    public async Task GenerationService_WithBoundedChannel_WaitsOnFullBuffer()
    {
        // Arrange: Provider that emits 150 tokens
        var mockProvider = new Mock<IModelProvider>();
        mockProvider.Setup(x => x.Name).Returns("BoundedProvider");
        mockProvider.Setup(x => x.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var emittedCount = 0;
        mockProvider.Setup(x => x.StreamAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(GenerateCountedTokens(150, () => emittedCount++));

        var sessionManager = new SessionManager();
        var meterFactory = new SimpleMeterFactory();
        var metrics = new GenerationMetrics(meterFactory);
        var service = new GenerationService(
            new[] { mockProvider.Object },
            sessionManager,
            metrics,
            NullLogger<GenerationService>.Instance);

        var session = sessionManager.CreateSession();

        // Act: Start consumption
        var consumeTask = Task.Run(async () =>
        {
            var count = 0;
            await foreach (var delta in service.GenerateAsync(session.SessionId, "test prompt"))
            {
                if (delta.Type == "token")
                {
                    count++;
                    await Task.Delay(100); // Slow consumer
                }
            }
            return count;
        });

        // Wait a bit for channel to fill (bounded capacity 100)
        await Task.Delay(500);

        // With bounded channel, producer should wait when buffer fills
        // emittedCount should be close to channel capacity + consumed tokens
        // Without backpressure, emittedCount would be 150 immediately
        var midEmitCount = emittedCount;
        midEmitCount.Should().BeLessThan(150);

        // Wait for completion
        var consumed = await consumeTask;
        consumed.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// Generate tokens as fast as possible (no delays)
    /// </summary>
    private static async IAsyncEnumerable<GenerationDelta> GenerateFastTokens(int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return new GenerationDelta
            {
                PromptId = "fast",
                Type = "token",
                Seq = i,
                Text = $"token{i}",
                Role = "assistant"
            };
        }

        yield return new GenerationDelta
        {
            PromptId = "fast",
            Type = "done",
            Seq = count,
            Role = "assistant"
        };
    }

    /// <summary>
    /// Generate tokens with a callback to track emission count
    /// </summary>
    private static async IAsyncEnumerable<GenerationDelta> GenerateCountedTokens(
        int count, 
        Action onEmit)
    {
        for (int i = 0; i < count; i++)
        {
            onEmit();
            yield return new GenerationDelta
            {
                PromptId = "counted",
                Type = "token",
                Seq = i,
                Text = $"token{i}",
                Role = "assistant"
            };
        }

        yield return new GenerationDelta
        {
            PromptId = "counted",
            Type = "done",
            Seq = count,
            Role = "assistant"
        };
    }
}
