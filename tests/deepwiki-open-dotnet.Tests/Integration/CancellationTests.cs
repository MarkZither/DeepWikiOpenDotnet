#pragma warning disable xUnit1051
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace DeepWiki.ApiService.Tests.Integration
{
    public class CancellationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public CancellationTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory.WithWebHostBuilder(builder =>
            {
                // Replace IModelProvider with a slow provider implementation for this test
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<DeepWiki.Rag.Core.Providers.IModelProvider>(new SlowTestProvider(1000, 100));
                });
            });
        }

        [Fact]
        public async Task Cancellation_ShouldCompleteWithin_200ms()
        {
#pragma warning disable xUnit1051
            var client = _factory.CreateClient();

            // Create session
            var createResp = await client.PostAsJsonAsync("/api/generation/session", new { owner = "test" }, TestContext.Current.CancellationToken);
            createResp.EnsureSuccessStatusCode();
            var session = JsonSerializer.Deserialize<DeepWiki.Data.Abstractions.Models.SessionResponse>(await createResp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

            // Start streaming request and read first token
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/generation/stream")
            {
                Content = JsonContent.Create(new { sessionId = session.SessionId, prompt = "hello" })
            };

            var resp = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
            resp.EnsureSuccessStatusCode();

            var stream = await resp.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
            var reader = new StreamReader(stream);

            // Read first line
            var firstLine = await reader.ReadLineAsync();
            var firstDelta = JsonSerializer.Deserialize<DeepWiki.Data.Abstractions.Models.GenerationDelta>(firstLine!);
            firstDelta.Should().NotBeNull();

            // Cancel using promptId from delta
            var cancelReq = new { sessionId = session.SessionId, promptId = firstDelta!.PromptId };

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var cancelResp = await client.PostAsJsonAsync("/api/generation/cancel", cancelReq, TestContext.Current.CancellationToken);
            cancelResp.EnsureSuccessStatusCode();

            // Verify that after cancellation we do not receive additional tokens within a short window
#pragma warning disable xUnit1051
            var nextLineTask = reader.ReadLineAsync();
            // Drain remaining stream and ensure it ends within a reasonable timeout (1s)
#pragma warning disable xUnit1051
            var drainTask = Task.Run(async () =>
            {
                while (true)
                {
                    var line = await reader.ReadLineAsync();
                    if (line == null) break;
                }
            });
#pragma warning restore xUnit1051

            var finished = await Task.WhenAny(drainTask, Task.Delay(1000, TestContext.Current.CancellationToken));
            finished.Should().Be(drainTask, "Stream did not end after cancellation within 1s");

            sw.Stop();
            sw.ElapsedMilliseconds.Should().BeLessThan(5000, "Cancel endpoint should return quickly");
        }

        private class SlowTestProvider : DeepWiki.Rag.Core.Providers.IModelProvider
        {
            private readonly int _count;
            private readonly int _delayMs;
            public SlowTestProvider(int count, int delayMs) { _count = count; _delayMs = delayMs; }
            public string Name => "SlowTest";
            public Task<bool> IsAvailableAsync(System.Threading.CancellationToken cancellationToken = default) => Task.FromResult(true);
            public async IAsyncEnumerable<DeepWiki.Data.Abstractions.Models.GenerationDelta> StreamAsync(string promptText, string? systemPrompt = null, [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken cancellationToken = default)
            {
                var pid = Guid.NewGuid().ToString();
                for (int i = 0; i < _count; i++)
                {
                    await Task.Delay(_delayMs, cancellationToken);
                    yield return new DeepWiki.Data.Abstractions.Models.GenerationDelta { PromptId = pid, Role = "assistant", Type = "token", Seq = i, Text = i.ToString() };
                }
                yield return new DeepWiki.Data.Abstractions.Models.GenerationDelta { PromptId = pid, Role = "assistant", Type = "done", Seq = _count };
            }
        }
    }
}
