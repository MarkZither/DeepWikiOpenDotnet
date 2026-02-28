using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using DeepWiki.Rag.Core.Providers;
using System.Threading;
using System.Net.Http;
using DeepWiki.Data.Abstractions.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeepWiki.Rag.Core.Tests.Providers
{
    public class OllamaProviderBehavioralTests
    {
        [Fact]
        public async Task StreamAsync_Should_Parse_NDJSON_Lines_To_GenerationDeltas()
        {
            // Arrange: fake HTTP client that returns NDJSON body
            var ndjson = new StringBuilder();
            ndjson.AppendLine(JsonSerializer.Serialize(new { response = "Hello" }));
            ndjson.AppendLine(JsonSerializer.Serialize(new { response = " world" }));
            ndjson.AppendLine(JsonSerializer.Serialize(new { done = true }));

            var handler = new FakeHandler(ndjson.ToString(), HttpStatusCode.OK);
            var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
            var provider = new OllamaProvider(client, NullLogger<OllamaProvider>.Instance, "test-model", TimeSpan.FromSeconds(5));

            // Act
            var list = new System.Collections.Generic.List<GenerationDelta>();
            await foreach (var d in provider.StreamAsync("prompt", null, CancellationToken.None))
            {
                list.Add(d);
            }

            // Assert
            list.Count.Should().Be(3);
            list[0].Type.Should().Be("token");
            list[0].Text.Should().Be("Hello");
            list[1].Text.Should().Be(" world");
            list[2].Type.Should().Be("done");
        }

        [Fact]
        public async Task IsAvailableAsync_ReturnsFalse_OnHttpError()
        {
            var handler = new FakeHandler(string.Empty, HttpStatusCode.InternalServerError);
            var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
            var provider = new OllamaProvider(client, NullLogger<OllamaProvider>.Instance, "test-model", TimeSpan.FromSeconds(1));

            var ok = await provider.IsAvailableAsync();

            ok.Should().BeFalse();
        }

        [Fact]
        public async Task StreamAsync_Should_ThrowTimeoutException_On_Stall()
        {
            // Handler simulates a slow stream by only sending data after delay exceeding stall timeout
            var handler = new SlowStreamHandler(delayBeforeFirstByteMs: 3000); // 3s
            var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
            var provider = new OllamaProvider(client, NullLogger<OllamaProvider>.Instance, "test-model", TimeSpan.FromMilliseconds(500)); // 0.5s stall timeout

            var ex = await Assert.ThrowsAsync<TimeoutException>(async () =>
            {
                var list = new System.Collections.Generic.List<GenerationDelta>();
                await foreach (var d in provider.StreamAsync("prompt", null, CancellationToken.None))
                {
                    list.Add(d);
                }
            });

            ex.Message.Should().Contain("stalled");
        }

        private class FakeHandler : HttpMessageHandler
        {
            private readonly string _content;
            private readonly HttpStatusCode _status;

            public FakeHandler(string content, HttpStatusCode status)
            {
                _content = content;
                _status = status;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var resp = new HttpResponseMessage(_status)
                {
                    Content = new StringContent(_content, Encoding.UTF8, "application/x-ndjson")
                };
                return Task.FromResult(resp);
            }
        }

        private class SlowStreamHandler : HttpMessageHandler
        {
            private readonly int _delayMs;

            public SlowStreamHandler(int delayBeforeFirstByteMs)
            {
                _delayMs = delayBeforeFirstByteMs;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var stream = new System.IO.MemoryStream();
                var content = new StreamContent(new DelayedStream(_delayMs));
                content.Headers.ContentType = new MediaTypeHeaderValue("application/x-ndjson");

                var resp = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = content
                };

                return resp;
            }

            private class DelayedStream : System.IO.Stream
            {
                private readonly int _delayMs;
                private bool _firstRead = true;

                public DelayedStream(int delayMs)
                {
                    _delayMs = delayMs;
                }

                public override bool CanRead => true;
                public override bool CanSeek => false;
                public override bool CanWrite => false;
                public override long Length => 0;
                public override long Position { get => 0; set { } }

                public override void Flush() { }
                public override int Read(byte[] buffer, int offset, int count)
                {
                    if (_firstRead)
                    {
                        _firstRead = false;
                        Thread.Sleep(_delayMs);
                    }
                    return 0; // indicate EOF after delay
                }

                public override long Seek(long offset, System.IO.SeekOrigin origin) => throw new NotSupportedException();
                public override void SetLength(long value) => throw new NotSupportedException();
                public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

                public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
                {
                    if (_firstRead)
                    {
                        _firstRead = false;
                        try
                        {
                            await Task.Delay(_delayMs, cancellationToken);
                        }
                        catch (OperationCanceledException) { throw; }
                    }
                    return 0;
                }
            }
        }
    }
}
