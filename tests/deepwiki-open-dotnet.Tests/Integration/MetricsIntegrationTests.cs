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
    public class MetricsIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
    {
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

            listener.Start();

            var client = _factory.CreateClient();

            // Create session
            var createResp = await client.PostAsJsonAsync("/api/generation/session", new { owner = "test" });
            createResp.EnsureSuccessStatusCode();
            var session = JsonSerializer.Deserialize<DeepWiki.Data.Abstractions.Models.SessionResponse>(await createResp.Content.ReadAsStringAsync())!;

            // Start streaming request and consume all tokens
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/generation/stream")
            {
                Content = JsonContent.Create(new { sessionId = session.SessionId, prompt = "hello" })
            };

            var resp = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();

            var stream = await resp.Content.ReadAsStreamAsync();
            using var reader = new System.IO.StreamReader(stream);
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (line == null) break;
            }

            // Give MeterListener a moment to flush
            await Task.Delay(100);

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
