using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using deepwiki_open_dotnet.Web.Models;
using deepwiki_open_dotnet.Web.Services;
using DeepWiki.Web.Tests.Fixtures;
using Xunit;

namespace DeepWiki.Web.Tests.Services;

public class ChatApiClientTests
{
    [Fact]
    public async Task StreamGenerationAsync_Returns_Response_With_Stream_Content()
    {
        var ndjson = "{\"type\":\"token\",\"text\":\"hi\"}\n";
        var handler = new FakeHttpHandler((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
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
        var handler = new FakeHttpHandler((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
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
        var handler = new FakeHttpHandler(async (req, ct) =>
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

    // T030 – US2: collection_ids included in request body
    [Fact]
    public async Task StreamGenerationAsync_Includes_CollectionIds_In_Request_Body()
    {
        string? capturedBody = null;
        var handler = new FakeHttpHandler(async (req, ct) =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        });

        var http = new HttpClient(handler) { BaseAddress = new Uri("https+http://apiservice") };
        var api = new ChatApiClient(http);

        var request = new GenerationRequestDto
        {
            SessionId = Guid.NewGuid(),
            Prompt = "test query",
            CollectionIds = new List<string> { "col-abc", "col-xyz" }
        };

        await api.StreamGenerationAsync(request);

        Assert.NotNull(capturedBody);
        Assert.Contains("collection_ids", capturedBody);
        Assert.Contains("col-abc", capturedBody);
        Assert.Contains("col-xyz", capturedBody);
    }

    [Fact]
    public async Task StreamGenerationAsync_Omits_CollectionIds_When_Null()
    {
        string? capturedBody = null;
        var handler = new FakeHttpHandler(async (req, ct) =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        });

        var http = new HttpClient(handler) { BaseAddress = new Uri("https+http://apiservice") };
        var api = new ChatApiClient(http);

        await api.StreamGenerationAsync(new GenerationRequestDto { SessionId = Guid.NewGuid(), Prompt = "no filters" });

        Assert.NotNull(capturedBody);
        Assert.DoesNotContain("collection_ids", capturedBody);
    }
}
