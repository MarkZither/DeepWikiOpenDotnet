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
            var service = new DeepWiki.Rag.Core.Services.GenerationService(provider, sessionManager, Microsoft.Extensions.Logging.Abstractions.NullLogger<DeepWiki.Rag.Core.Services.GenerationService>.Instance);

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
            var service = new DeepWiki.Rag.Core.Services.GenerationService(provider, sessionManager, Microsoft.Extensions.Logging.Abstractions.NullLogger<DeepWiki.Rag.Core.Services.GenerationService>.Instance);

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
                for (int i = 0; i < _count; i++)
                {
                    await Task.Delay(_delayMs, cancellationToken);
                    yield return new DeepWiki.Data.Abstractions.Models.GenerationDelta { PromptId = string.Empty, Role = "assistant", Type = "token", Seq = i, Text = i.ToString() };
                }
            }
        }
    }
}
