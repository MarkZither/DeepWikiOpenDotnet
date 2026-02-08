using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using DeepWiki.Data.Abstractions.Models;

namespace DeepWiki.Rag.Core.Tests.Services
{
    public class GenerationServiceTests
    {
        private class SimpleMeterFactory : System.Diagnostics.Metrics.IMeterFactory, System.IDisposable
        {
            public System.Diagnostics.Metrics.Meter Create(System.Diagnostics.Metrics.MeterOptions options) => new System.Diagnostics.Metrics.Meter(options.Name, options.Version);
            public System.Diagnostics.Metrics.Meter Create(string name, string? version = null) => new System.Diagnostics.Metrics.Meter(name, version);
            public void Dispose() { }
        }
        [Fact]
        public void IGenerationService_Has_Expected_Signature()
        {
            var type = typeof(DeepWiki.Data.Abstractions.IGenerationService);
            var method = type.GetMethod("GenerateAsync");
            method.Should().NotBeNull();

            method!.ReturnType.IsGenericType.Should().BeTrue();
            method.ReturnType.GetGenericTypeDefinition().Should().Be(typeof(System.Collections.Generic.IAsyncEnumerable<>));
            method.ReturnType.GetGenericArguments()[0].Should().Be(typeof(GenerationDelta));

            var hasCancellation = method.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken));
            hasCancellation.Should().BeTrue();
        }

        [Fact]
        public async Task GenerateAsync_Should_Throw_On_ProviderFailure()
        {
            // Arrange: provider that throws
            var provider = new ThrowingProvider(new Exception("boom"));
            var sessionManager = new DeepWiki.Rag.Core.Services.SessionManager();
            var metricsFactory = new SimpleMeterFactory();
            var gm = new DeepWiki.Rag.Core.Observability.GenerationMetrics(metricsFactory);
            var service = new DeepWiki.Rag.Core.Services.GenerationService(provider, sessionManager, gm, Microsoft.Extensions.Logging.Abstractions.NullLogger<DeepWiki.Rag.Core.Services.GenerationService>.Instance);

            var session = sessionManager.CreateSession();

            // Act & Assert
            var ex = await Assert.ThrowsAsync<Exception>(async () =>
            {
                await foreach (var d in service.GenerateAsync(session.SessionId, "prompt", cancellationToken: CancellationToken.None))
                {
                    // consume
                }
            });

            Assert.Equal("boom", ex.Message);
        }

        [Fact]
        public async Task GenerateAsync_ShouldSupportCancellation()
        {
            // Arrange: provider that yields tokens slowly
            var provider = new SlowProvider(10, 100);
            var sessionManager = new DeepWiki.Rag.Core.Services.SessionManager();
            var metricsFactory = new SimpleMeterFactory();
            var gm = new DeepWiki.Rag.Core.Observability.GenerationMetrics(metricsFactory);
            var service = new DeepWiki.Rag.Core.Services.GenerationService(provider, sessionManager, gm, Microsoft.Extensions.Logging.Abstractions.NullLogger<DeepWiki.Rag.Core.Services.GenerationService>.Instance);

            var session = sessionManager.CreateSession();
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(50);

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                await foreach (var d in service.GenerateAsync(session.SessionId, "prompt", cancellationToken: cts.Token))
                {
                    // consume
                }
            });
        }

        private class ThrowingProvider : DeepWiki.Rag.Core.Providers.IModelProvider
        {
            private readonly Exception _ex;
            public ThrowingProvider(Exception ex) => _ex = ex;
            public string Name => "Thrower";
            public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
            public async IAsyncEnumerable<DeepWiki.Data.Abstractions.Models.GenerationDelta> StreamAsync(string promptText, string? systemPrompt = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                await Task.Yield();
                var sentinel = DateTime.UtcNow.Ticks == 0;
                if (sentinel) yield break;
                throw _ex;
            }
        }

        private class SlowProvider : DeepWiki.Rag.Core.Providers.IModelProvider
        {
            private readonly int _count;
            private readonly int _delayMs;
            public SlowProvider(int count, int delayMs) { _count = count; _delayMs = delayMs; }
            public string Name => "Slow";
            public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
            public async IAsyncEnumerable<DeepWiki.Data.Abstractions.Models.GenerationDelta> StreamAsync(string promptText, string? systemPrompt = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                var pid = Guid.NewGuid().ToString();
                for (int i = 0; i < _count; i++)
                {
                    await Task.Delay(_delayMs, cancellationToken);
                    yield return new DeepWiki.Data.Abstractions.Models.GenerationDelta { PromptId = pid, Role = "assistant", Type = "token", Seq = i, Text = i.ToString() };
                }
            }
        }

        [Fact]
        public async Task GenerateAsync_Builds_Rag_SystemPrompt_When_TopKProvided()
        {
            // Arrange
            var capturedSystemPrompt = (string?)null;

            var provider = new TestProviderCaptureSystemPrompt((pt, sp, ct) => capturedSystemPrompt = sp);
            var sessionManager = new DeepWiki.Rag.Core.Services.SessionManager();
            var metricsFactory = new SimpleMeterFactory();
            var gm = new DeepWiki.Rag.Core.Observability.GenerationMetrics(metricsFactory);

            var vectorStore = new FakeVectorStore();
            var embeddingService = new FakeEmbeddingService();

            var service = new DeepWiki.Rag.Core.Services.GenerationService(provider, sessionManager, gm, Microsoft.Extensions.Logging.Abstractions.NullLogger<DeepWiki.Rag.Core.Services.GenerationService>.Instance, vectorStore, embeddingService, TimeSpan.FromMilliseconds(500));

            var session = sessionManager.CreateSession();

            // Act
            await foreach (var d in service.GenerateAsync(session.SessionId, "tell me about X", topK: 2, cancellationToken: CancellationToken.None))
            {
                // consume
            }

            // Assert
            capturedSystemPrompt.Should().NotBeNull();
            capturedSystemPrompt.Should().Contain("Context documents:");
            capturedSystemPrompt.Should().Contain("Title: doc-1");
        }

        [Fact]
        public async Task GenerateAsync_Emits_Error_On_Provider_Stall()
        {
            // Arrange: provider that never yields tokens
            var provider = new NeverYieldProvider();
            var sessionManager = new DeepWiki.Rag.Core.Services.SessionManager();
            var metricsFactory = new SimpleMeterFactory();
            var gm = new DeepWiki.Rag.Core.Observability.GenerationMetrics(metricsFactory);

            // use very short stall timeout so test completes quickly
            var service = new DeepWiki.Rag.Core.Services.GenerationService(provider, sessionManager, gm, Microsoft.Extensions.Logging.Abstractions.NullLogger<DeepWiki.Rag.Core.Services.GenerationService>.Instance, null, null, TimeSpan.FromMilliseconds(200));

            var session = sessionManager.CreateSession();

            // Act & Assert - expect an error delta to be emitted
            var foundError = false;
            await foreach (var d in service.GenerateAsync(session.SessionId, "prompt", cancellationToken: CancellationToken.None))
            {
                if (d.Type == "error")
                {
                    foundError = true;
                    break;
                }
            }

            foundError.Should().BeTrue();
        }

        private class TestProviderCaptureSystemPrompt : DeepWiki.Rag.Core.Providers.IModelProvider
        {
            private readonly System.Action<string, string?, CancellationToken> _act;
            public TestProviderCaptureSystemPrompt(System.Action<string, string?, CancellationToken> act) { _act = act; }
            public string Name => "Capture";
            public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
            public async IAsyncEnumerable<DeepWiki.Data.Abstractions.Models.GenerationDelta> StreamAsync(string promptText, string? systemPrompt = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                _act(promptText, systemPrompt, cancellationToken);
                yield return new DeepWiki.Data.Abstractions.Models.GenerationDelta { PromptId = Guid.NewGuid().ToString(), Role = "assistant", Type = "token", Seq = 0, Text = "x" };
                yield return new DeepWiki.Data.Abstractions.Models.GenerationDelta { PromptId = Guid.NewGuid().ToString(), Role = "assistant", Type = "done", Seq = 1 };
            }
        }

        private class FakeVectorStore : DeepWiki.Data.Abstractions.IVectorStore
        {
            public Task<IReadOnlyList<DeepWiki.Data.Abstractions.Models.VectorQueryResult>> QueryAsync(float[] embedding, int k, System.Collections.Generic.Dictionary<string, string>? filters = null, CancellationToken cancellationToken = default)
            {
                var docs = new[] {
                    new DeepWiki.Data.Abstractions.Models.VectorQueryResult { Document = new DeepWiki.Data.Abstractions.Models.DocumentDto { Title = "doc-1", Text = "This is doc 1" }, SimilarityScore = 0.9f },
                    new DeepWiki.Data.Abstractions.Models.VectorQueryResult { Document = new DeepWiki.Data.Abstractions.Models.DocumentDto { Title = "doc-2", Text = "This is doc 2" }, SimilarityScore = 0.8f }
                };

                return Task.FromResult((IReadOnlyList<DeepWiki.Data.Abstractions.Models.VectorQueryResult>)docs);
            }

            public Task UpsertAsync(DeepWiki.Data.Abstractions.Models.DocumentDto document, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task RebuildIndexAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        }

        private class FakeEmbeddingService : DeepWiki.Data.Abstractions.IEmbeddingService
        {
            public string Provider => "noop";
            public string ModelId => "noop";
            public int EmbeddingDimension => 1536;
            public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default) => Task.FromResult(new float[1536]);
            public async IAsyncEnumerable<float[]> EmbedBatchAsync(System.Collections.Generic.IEnumerable<string> texts, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { yield return new float[1536]; }
            public Task<System.Collections.Generic.IReadOnlyList<DeepWiki.Data.Abstractions.Models.EmbeddingResponse>> EmbedBatchWithMetadataAsync(System.Collections.Generic.IEnumerable<string> texts, int batchSize = 10, CancellationToken cancellationToken = default) => Task.FromResult((System.Collections.Generic.IReadOnlyList<DeepWiki.Data.Abstractions.Models.EmbeddingResponse>)new System.Collections.Generic.List<DeepWiki.Data.Abstractions.Models.EmbeddingResponse>());
        }

        private class NeverYieldProvider : DeepWiki.Rag.Core.Providers.IModelProvider
        {
            public string Name => "Never";
            public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
            public async IAsyncEnumerable<DeepWiki.Data.Abstractions.Models.GenerationDelta> StreamAsync(string promptText, string? systemPrompt = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                yield break;
            }
        }
    }
}
