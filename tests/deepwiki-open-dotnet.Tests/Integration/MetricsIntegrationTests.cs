using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace DeepWiki.ApiService.Tests.Integration
{
#pragma warning disable xUnit1051
    public class MetricsIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
    {
#pragma warning restore xUnit1051
        private readonly WebApplicationFactory<Program> _factory;

        public MetricsIntegrationTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory.WithWebHostBuilder(builder =>
            {
                // Register a fast provider for predictable token counts
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<DeepWiki.Rag.Core.Providers.IModelProvider>(new FastTestProvider());
                });
            });
        }

        [Fact]
        public async Task GenerationMetrics_Should_Record_Ttf_And_Tokens()
        {
            var measurements = new ConcurrentBag<(string Name, double Value)>();

            using var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "DeepWiki.Rag.Generation")
                    listener.EnableMeasurementEvents(instrument);
            };

            listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
            {
                measurements.Add((instrument.Name, measurement));
            });

            // Also listen for long-valued measurements (counters are long)
            listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
            {
                measurements.Add((instrument.Name, (double)measurement));
            });

            listener.Start();

            var client = _factory.CreateClient();

            // Create session
            var createResp = await client.PostAsJsonAsync("/api/generation/session", new { owner = "test" }, TestContext.Current.CancellationToken);
            createResp.EnsureSuccessStatusCode();
            var session = JsonSerializer.Deserialize<DeepWiki.Data.Abstractions.Models.SessionResponse>(await createResp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

            // Start streaming request and consume all tokens
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/generation/stream")
            {
                Content = JsonContent.Create(new { sessionId = session.SessionId, prompt = "hello" })
            };

            var resp = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
            resp.EnsureSuccessStatusCode();

            var stream = await resp.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
            using var reader = new System.IO.StreamReader(stream);
#pragma warning disable xUnit1051
            while (true)
            {
                var line = await reader.ReadLineAsync();
                if (line == null) break;
            }
#pragma warning restore xUnit1051

            // Give MeterListener a moment to flush
            await Task.Delay(100, TestContext.Current.CancellationToken);

            listener.Dispose();

            // Assert that we saw TTF and token metrics
            measurements.Should().Contain(m => m.Name == "generation.ttf");
            measurements.Should().Contain(m => m.Name == "generation.tokens");
        }

        private class FastTestProvider : DeepWiki.Rag.Core.Providers.IModelProvider
        {
            public string Name => "FastTest";
            public Task<bool> IsAvailableAsync(System.Threading.CancellationToken cancellationToken = default) => Task.FromResult(true);
            public async IAsyncEnumerable<DeepWiki.Data.Abstractions.Models.GenerationDelta> StreamAsync(string promptText, string? systemPrompt = null, [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken cancellationToken = default)
            {
                var pid = Guid.NewGuid().ToString();
                yield return new DeepWiki.Data.Abstractions.Models.GenerationDelta { PromptId = pid, Role = "assistant", Type = "token", Seq = 0, Text = "a" };
                yield return new DeepWiki.Data.Abstractions.Models.GenerationDelta { PromptId = pid, Role = "assistant", Type = "token", Seq = 1, Text = "b" };
                yield return new DeepWiki.Data.Abstractions.Models.GenerationDelta { PromptId = pid, Role = "assistant", Type = "done", Seq = 2 };
                await Task.CompletedTask;
            }
        }
    }
}
