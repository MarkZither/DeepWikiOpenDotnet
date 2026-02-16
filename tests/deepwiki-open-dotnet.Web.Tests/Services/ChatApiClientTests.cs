using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using deepwiki_open_dotnet.Web.Models;
using deepwiki_open_dotnet.Web.Services;
using Xunit;

namespace DeepWiki.Web.Tests.Services;

internal class FakeHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;
    public FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) => _responder = responder;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => _responder(request, cancellationToken);
}

public class ChatApiClientTests
{
    [Fact]
    public async Task StreamGenerationAsync_Returns_Response_With_Stream_Content()
    {
        var ndjson = "{\"type\":\"token\",\"text\":\"hi\"}\n";
        var handler = new FakeHandler((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ndjson, Encoding.UTF8, "application/x-ndjson")
        }));

        var http = new HttpClient(handler) { BaseAddress = new Uri("https+http://apiservice") };
        var api = new ChatApiClient(http);

        var resp = await api.StreamGenerationAsync(new GenerationRequestDto { SessionId = Guid.NewGuid(), Prompt = "hi" });

        Assert.True(resp.IsSuccessStatusCode);
        var content = await resp.Content.ReadAsStringAsync();
        Assert.Contains("hi", content);
    }

    [Fact]
    public async Task StreamGenerationAsync_Propagates_NonSuccess_Status()
    {
        var handler = new FakeHandler((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("error")
        }));

        var http = new HttpClient(handler) { BaseAddress = new Uri("https+http://apiservice") };
        var api = new ChatApiClient(http);

        var resp = await api.StreamGenerationAsync(new GenerationRequestDto { SessionId = Guid.NewGuid(), Prompt = "fail" });

        Assert.False(resp.IsSuccessStatusCode);
    }

    [Fact]
    public async Task StreamGenerationAsync_Cancellation_Throws_TaskCanceled()
    {
        var handler = new FakeHandler(async (req, ct) =>
        {
            // Simulate a long-running send that respects cancellation
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        });

        var http = new HttpClient(handler) { BaseAddress = new Uri("https+http://apiservice") };
        var api = new ChatApiClient(http);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(10); // cancel almost immediately

        await Assert.ThrowsAsync<TaskCanceledException>(() => api.StreamGenerationAsync(new GenerationRequestDto { SessionId = Guid.NewGuid(), Prompt = "x" }, cts.Token));
    }
}
