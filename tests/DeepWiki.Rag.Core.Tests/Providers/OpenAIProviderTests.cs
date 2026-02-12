using DeepWiki.Rag.Core.Providers;
using DeepWiki.Data.Abstractions.Models;
using FluentAssertions;
using Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Http;
using System.Net;
using System.Threading;

namespace DeepWiki.Rag.Core.Tests.Providers;

public class OpenAIProviderTests
{
    private class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }

    [Fact]
    public async Task IsAvailableAsync_returns_false_when_no_api_key()
    {
        var handler = new FakeHandler(req => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = new HttpClient(handler) { BaseAddress = new System.Uri("https://api.example.com") };
        var p = new OpenAIProvider(client, null, "openai", "gpt-test", new NullLogger<OpenAIProvider>());
        var available = await p.IsAvailableAsync();
        available.Should().BeFalse();
    }

    [Fact]
    public async Task StreamAsync_throws_when_not_configured()
    {
        var handler = new FakeHandler(req => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = new HttpClient(handler) { BaseAddress = new System.Uri("https://api.example.com") };
        var p = new OpenAIProvider(client, null, "openai", "gpt-test", new NullLogger<OpenAIProvider>());

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var d in p.StreamAsync("hi")) { }
        });
    }

    [Fact]
    public async Task StreamAsync_emits_tokens_when_configured()
    {
        var ndjson =
            "data: {\"choices\":[{\"delta\":{\"content\":\"HelloTest\"},\"index\":0,\"finish_reason\":null}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"!\"},\"index\":0,\"finish_reason\":null}]}\n\n" +
            "data: [DONE]\n\n";

        var handler = new FakeHandler(req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri?.AbsolutePath.EndsWith("/v1/chat/completions") == true)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ndjson) };
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var client = new HttpClient(handler) { BaseAddress = new System.Uri("https://api.example.com") };
        var p = new OpenAIProvider(client, "fake-key", "openai", "gpt-test", new NullLogger<OpenAIProvider>());

        var items = await p.StreamAsync("hello").ToListAsync();

        items.Should().Contain(x => x.Type == "token" && x.Text != null && x.Text.Contains("HelloTest"));
        items.Should().Contain(x => x.Type == "done");
    }
}
