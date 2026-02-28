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
        var api = new ChatApiClient(http, Microsoft.Extensions.Logging.Abstractions.NullLogger<deepwiki_open_dotnet.Web.Services.ChatApiClient>.Instance);

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
        var api = new ChatApiClient(http, Microsoft.Extensions.Logging.Abstractions.NullLogger<deepwiki_open_dotnet.Web.Services.ChatApiClient>.Instance);

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
        var api = new ChatApiClient(http, Microsoft.Extensions.Logging.Abstractions.NullLogger<deepwiki_open_dotnet.Web.Services.ChatApiClient>.Instance);

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
        var api = new ChatApiClient(http, Microsoft.Extensions.Logging.Abstractions.NullLogger<deepwiki_open_dotnet.Web.Services.ChatApiClient>.Instance);

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
        var api = new ChatApiClient(http, Microsoft.Extensions.Logging.Abstractions.NullLogger<deepwiki_open_dotnet.Web.Services.ChatApiClient>.Instance);

        await api.StreamGenerationAsync(new GenerationRequestDto { SessionId = Guid.NewGuid(), Prompt = "no filters" });

        Assert.NotNull(capturedBody);
        Assert.DoesNotContain("collection_ids", capturedBody);
    }

    // T040 – US3: GetCollectionsAsync fetches /api/documents and deserializes response
    [Fact]
    public async Task GetCollectionsAsync_Returns_Collections_From_Api()
    {
        var json = """
            {
              "collections": [
                {"id": "col-1", "name": "Repo Alpha", "document_count": 10},
                {"id": "col-2", "name": "Repo Beta",  "document_count": 5}
              ],
              "total_count": 2
            }
            """;

        Uri? capturedUri = null;
        var handler = new FakeHttpHandler((req, ct) =>
        {
            capturedUri = req.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        });

        var http = new HttpClient(handler) { BaseAddress = new Uri("https+http://apiservice") };
        var api = new ChatApiClient(http, Microsoft.Extensions.Logging.Abstractions.NullLogger<deepwiki_open_dotnet.Web.Services.ChatApiClient>.Instance);

        var result = await api.GetCollectionsAsync();

        Assert.NotNull(capturedUri);
        Assert.Contains("/api/documents", capturedUri!.ToString());

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Collections.Count);
        Assert.Equal("col-1", result.Collections[0].Id);
        Assert.Equal("Repo Alpha", result.Collections[0].Name);
        Assert.Equal(10, result.Collections[0].DocumentCount);
        Assert.Equal("col-2", result.Collections[1].Id);
    }

    [Fact]
    public async Task GetCollectionsAsync_Returns_Empty_On_Empty_Response()
    {
        var handler = new FakeHttpHandler((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"collections":[],"total_count":0}""", Encoding.UTF8, "application/json")
        }));

        var http = new HttpClient(handler) { BaseAddress = new Uri("https+http://apiservice") };
        var api = new ChatApiClient(http, Microsoft.Extensions.Logging.Abstractions.NullLogger<deepwiki_open_dotnet.Web.Services.ChatApiClient>.Instance);

        var result = await api.GetCollectionsAsync();

        Assert.Empty(result.Collections);
        Assert.Equal(0, result.TotalCount);
    }
}
