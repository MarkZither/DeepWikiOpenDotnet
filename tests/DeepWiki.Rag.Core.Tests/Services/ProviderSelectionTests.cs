using System.Threading.Channels;
using DeepWiki.Rag.Core.Services;
using DeepWiki.Rag.Core.Providers;
using DeepWiki.Rag.Core.Models;
using DeepWiki.Data.Abstractions.Models;
using FluentAssertions;
using Moq;
using Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;

namespace DeepWiki.Rag.Core.Tests.Services
{
    // Simple IMeterFactory implementation used for tests
    internal class SimpleMeterFactory : System.Diagnostics.Metrics.IMeterFactory, System.IDisposable
    {
        public System.Diagnostics.Metrics.Meter Create(System.Diagnostics.Metrics.MeterOptions options) => new System.Diagnostics.Metrics.Meter(options.Name, options.Version);
        public System.Diagnostics.Metrics.Meter Create(string name, string? version = null) => new System.Diagnostics.Metrics.Meter(name, version);
        public void Dispose() { }
    }

    public class ProviderSelectionTests
{
    [Fact]
    public async Task GenerationService_falls_back_to_next_provider_on_failure()
    {
        var sessionManager = new SessionManager();
        var metrics = new DeepWiki.Rag.Core.Observability.GenerationMetrics(new SimpleMeterFactory());
        var logger = new NullLogger<GenerationService>();

        // provider A: available but throws
        var mockA = new Mock<IModelProvider>();
        mockA.SetupGet(p => p.Name).Returns("A");
        mockA.Setup(p => p.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        mockA.Setup(p => p.StreamAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).Returns(FailingStream());

        // provider B: available and emits token
        var mockB = new Mock<IModelProvider>();
        mockB.SetupGet(p => p.Name).Returns("B");
        mockB.Setup(p => p.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        mockB.Setup(p => p.StreamAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).Returns(SuccessStream());

        var svc = new GenerationService(new[] { mockA.Object, mockB.Object }, sessionManager, metrics, logger);

        var session = sessionManager.CreateSession("test");

        var results = new List<DeepWiki.Data.Abstractions.Models.GenerationDelta>();
        await foreach (var d in svc.GenerateAsync(session.SessionId, "hello"))
        {
            results.Add(d);
        }

        // Should have received token from provider B
        results.Should().ContainSingle(x => x.Type == "token" && x.Text == "from B");
        results.Should().ContainSingle(x => x.Type == "done");

        static async IAsyncEnumerable<DeepWiki.Data.Abstractions.Models.GenerationDelta> FailingStream()
        {
            await Task.Yield();
            throw new Exception("provider failure");
#pragma warning disable 162
            yield break;
#pragma warning restore 162
        }

        static async IAsyncEnumerable<DeepWiki.Data.Abstractions.Models.GenerationDelta> SuccessStream()
        {
            yield return new DeepWiki.Data.Abstractions.Models.GenerationDelta { PromptId = "p-b", Type = "token", Text = "from B", Role = "assistant", Seq = 0 };
            await Task.Delay(10);
            yield return new DeepWiki.Data.Abstractions.Models.GenerationDelta { PromptId = "p-b", Type = "done", Role = "assistant", Seq = 1 };
        }
    }

    [Fact]
    public async Task Circuit_opens_after_failure_threshold_and_skips_provider()
    {
        var sessionManager = new SessionManager();
        var metrics = new DeepWiki.Rag.Core.Observability.GenerationMetrics(new SimpleMeterFactory());
        var logger = new NullLogger<GenerationService>();

        var mockA = new Mock<IModelProvider>();
        mockA.SetupGet(p => p.Name).Returns("A");
        mockA.Setup(p => p.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        mockA.Setup(p => p.StreamAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).Returns(FailingStream());

        var mockB = new Mock<IModelProvider>();
        mockB.SetupGet(p => p.Name).Returns("B");
        mockB.Setup(p => p.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        mockB.Setup(p => p.StreamAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).Returns(SuccessStream());

        var svc = new GenerationService(new[] { mockA.Object, mockB.Object }, sessionManager, metrics, logger, failureThreshold: 2, circuitBreakDuration: TimeSpan.FromSeconds(1));
        var session = sessionManager.CreateSession("test");

        // First call: A fails, fallback to B -> success
        var res1 = new List<DeepWiki.Data.Abstractions.Models.GenerationDelta>();
        await foreach (var d in svc.GenerateAsync(session.SessionId, "hello1")) res1.Add(d);
        res1.Should().ContainSingle(x => x.Type == "token" && x.Text == "from B");

        // Second call: A fails again -> this should trip circuit (threshold = 2), B used
        var res2 = new List<DeepWiki.Data.Abstractions.Models.GenerationDelta>();
        await foreach (var d in svc.GenerateAsync(session.SessionId, "hello2")) res2.Add(d);
        res2.Should().ContainSingle(x => x.Type == "token" && x.Text == "from B");

        // Now simulate immediate third call: A should be skipped due to open circuit, and B used
        var res3 = new List<DeepWiki.Data.Abstractions.Models.GenerationDelta>();
        await foreach (var d in svc.GenerateAsync(session.SessionId, "hello3")) res3.Add(d);
        res3.Should().ContainSingle(x => x.Type == "token" && x.Text == "from B");

        // Wait for circuit to close (circuitBreakDuration = 1s)
        await Task.Delay(1100);

        // After circuit close: A will be attempted again (it still fails) but fallback to B
        var res4 = new List<DeepWiki.Data.Abstractions.Models.GenerationDelta>();
        await foreach (var d in svc.GenerateAsync(session.SessionId, "hello4")) res4.Add(d);
        res4.Should().ContainSingle(x => x.Type == "token" && x.Text == "from B");

        static async IAsyncEnumerable<DeepWiki.Data.Abstractions.Models.GenerationDelta> FailingStream()
        {
            await Task.Yield();
            throw new Exception("provider failure");
#pragma warning disable 162
            yield break;
#pragma warning restore 162
        }

        static async IAsyncEnumerable<DeepWiki.Data.Abstractions.Models.GenerationDelta> SuccessStream()
        {
            yield return new DeepWiki.Data.Abstractions.Models.GenerationDelta { PromptId = "p-b", Type = "token", Text = "from B", Role = "assistant", Seq = 0 };
            await Task.Delay(10);
            yield return new DeepWiki.Data.Abstractions.Models.GenerationDelta { PromptId = "p-b", Type = "done", Role = "assistant", Seq = 1 };
        }
    }
}
}
