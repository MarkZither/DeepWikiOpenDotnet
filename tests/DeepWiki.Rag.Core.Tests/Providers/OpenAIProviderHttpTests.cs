using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;
using DeepWiki.Rag.Core.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using System.Linq;
using System.Threading;

namespace DeepWiki.Rag.Core.Tests.Providers
{
    public class OpenAIProviderHttpTests
    {
        private class FakeHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
            public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(_responder(request));
        }

        [Fact]
        public async Task StreamAsync_Parses_OpenAI_NDJSON_Stream()
        {
            var ndjson =
                "data: {\"choices\":[{\"delta\":{\"content\":\"Hello\"},\"index\":0,\"finish_reason\":null}]}\n\n" +
                "data: {\"choices\":[{\"delta\":{\"content\":\" world\"},\"index\":0,\"finish_reason\":null}]}\n\n" +
                "data: {\"choices\":[{\"delta\":{},\"index\":0,\"finish_reason\":\"stop\"}]}\n\n" +
                "data: [DONE]\n\n";

            var handler = new FakeHandler(req =>
            {
                if (req.Method == HttpMethod.Post && req.RequestUri?.AbsolutePath.EndsWith("/v1/chat/completions") == true)
                {
                    var content = new StringContent(ndjson);
                    var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
                    return resp;
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            var client = new HttpClient(handler) { BaseAddress = new System.Uri("https://api.example.com") };
            var provider = new OpenAIProvider(client, apiKey: "test", providerType: "openai", modelId: "gpt-test", logger: NullLogger<OpenAIProvider>.Instance);

            var items = await provider.StreamAsync("hi").ToListAsync();

            Assert.True(items.Count >= 3);
            Assert.Equal("token", items[0].Type);
            Assert.Equal("Hello", items[0].Text);
            Assert.Equal("token", items[1].Type);
            Assert.Equal(" world", items[1].Text);
            Assert.Equal("done", items.Last().Type);
        }

        [Fact]
        public async Task IsAvailableAsync_Checks_Models_Endpoint_For_OpenAI()
        {
            var handler = new FakeHandler(req =>
            {
                if (req.Method == HttpMethod.Get && req.RequestUri?.AbsolutePath == "/v1/models")
                    return new HttpResponseMessage(HttpStatusCode.OK);
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            var client = new HttpClient(handler) { BaseAddress = new System.Uri("https://api.example.com") };
            var provider = new OpenAIProvider(client, apiKey: "test", providerType: "openai", modelId: "gpt-test", logger: NullLogger<OpenAIProvider>.Instance);

            var avail = await provider.IsAvailableAsync();
            Assert.True(avail);
        }
    }
}
