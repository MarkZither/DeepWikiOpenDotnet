using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using deepwiki_open_dotnet.Web.Models;
using deepwiki_open_dotnet.Web.Services;
using DeepWiki.Web.Tests.Fixtures;
using Xunit;

namespace DeepWiki.Web.Tests.Services;

// T070, T071, T072 – US5: DocumentsApiClient tests
public class DocumentsApiClientTests
{
    private static DocumentsApiClient BuildClient(Func<HttpRequestMessage, System.Threading.CancellationToken, Task<HttpResponseMessage>> handler)
    {
        var fakeHandler = new FakeHttpHandler(handler);
        var http = new HttpClient(fakeHandler) { BaseAddress = new Uri("https+http://apiservice") };
        return new DocumentsApiClient(http);
    }

    // ── T070: IngestAsync posts to /api/documents/ingest ─────────────────────

    [Fact]
    public async Task IngestAsync_Posts_To_Ingest_Endpoint_And_Returns_Response()
    {
        var responseJson = """
            {
              "successCount": 1,
              "failureCount": 0,
              "totalChunks": 5,
              "durationMs": 120,
              "ingestedDocumentIds": ["550e8400-e29b-41d4-a716-446655440000"],
              "errors": []
            }
            """;

        Uri? capturedUri = null;
        string? capturedMethod = null;
        string? capturedBody = null;

        var client = BuildClient(async (req, ct) =>
        {
            capturedUri = req.RequestUri;
            capturedMethod = req.Method.Method;
            capturedBody = await req.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        });

        var request = new IngestRequestDto
        {
            Documents = new List<IngestDocumentDto>
            {
                new() { RepoUrl = "https://github.com/org/repo", FilePath = "src/Main.cs", Title = "Main", Text = "public class Main {}" }
            },
            ContinueOnError = true,
            BatchSize = 5
        };

        var result = await client.IngestAsync(request);

        Assert.NotNull(capturedUri);
        Assert.Contains("/api/documents/ingest", capturedUri!.ToString());
        Assert.Equal("POST", capturedMethod);
        Assert.NotNull(capturedBody);
        Assert.Contains("repoUrl", capturedBody);

        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(0, result.FailureCount);
        Assert.Equal(5, result.TotalChunks);
        Assert.Single(result.IngestedDocumentIds);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task IngestAsync_Returns_Errors_On_Partial_Failure()
    {
        var responseJson = """
            {
              "successCount": 0,
              "failureCount": 1,
              "totalChunks": 0,
              "durationMs": 50,
              "ingestedDocumentIds": [],
              "errors": [
                {
                  "documentIdentifier": "https://github.com/org/repo:src/Bad.cs",
                  "message": "Embedding service unavailable",
                  "stage": "Embedding"
                }
              ]
            }
            """;

        var client = BuildClient((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
        }));

        var result = await client.IngestAsync(new IngestRequestDto
        {
            Documents = new List<IngestDocumentDto>
            {
                new() { RepoUrl = "https://github.com/org/repo", FilePath = "src/Bad.cs", Title = "Bad", Text = "bad doc" }
            }
        });

        Assert.Equal(1, result.FailureCount);
        Assert.Single(result.Errors);
        Assert.Equal("Embedding service unavailable", result.Errors[0].Message);
    }

    // ── T071: ListAsync calls GET /api/documents with query params ────────────

    [Fact]
    public async Task ListAsync_Calls_Get_With_Pagination_And_Filter_Params()
    {
        var responseJson = """
            {
              "items": [
                {
                  "id": "550e8400-e29b-41d4-a716-446655440001",
                  "repoUrl": "https://github.com/org/repo",
                  "filePath": "src/A.cs",
                  "title": "File A",
                  "createdAt": "2025-01-01T00:00:00Z",
                  "updatedAt": "2025-01-02T00:00:00Z",
                  "tokenCount": 200,
                  "fileType": "cs",
                  "isCode": true
                }
              ],
              "totalCount": 1,
              "page": 2,
              "pageSize": 10,
              "totalPages": 1
            }
            """;

        Uri? capturedUri = null;
        var client = BuildClient((req, ct) =>
        {
            capturedUri = req.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });
        });

        var result = await client.ListAsync(page: 2, pageSize: 10, repoUrl: "https://github.com/org/repo");

        Assert.NotNull(capturedUri);
        var uriString = capturedUri!.ToString();
        Assert.Contains("/api/documents", uriString);
        Assert.Contains("page=2", uriString);
        Assert.Contains("pageSize=10", uriString);
        Assert.Contains("repoUrl=", uriString);

        Assert.Single(result.Items);
        Assert.Equal("File A", result.Items[0].Title);
        Assert.Equal(2, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(1, result.DocumentTotalCount);
    }

    [Fact]
    public async Task ListAsync_Omits_RepoUrl_Param_When_Null()
    {
        Uri? capturedUri = null;
        var client = BuildClient((req, ct) =>
        {
            capturedUri = req.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"items":[],"totalCount":0,"page":1,"pageSize":10,"totalPages":0}""", Encoding.UTF8, "application/json")
            });
        });

        await client.ListAsync(page: 1, pageSize: 10);

        Assert.NotNull(capturedUri);
        Assert.DoesNotContain("repoUrl", capturedUri!.ToString());
    }

    // ── T072: DeleteAsync sends DELETE and returns bool ───────────────────────

    [Fact]
    public async Task DeleteAsync_Returns_True_On_204_NoContent()
    {
        var id = Guid.NewGuid();
        Uri? capturedUri = null;
        string? capturedMethod = null;

        var client = BuildClient((req, ct) =>
        {
            capturedUri = req.RequestUri;
            capturedMethod = req.Method.Method;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        });

        var result = await client.DeleteAsync(id);

        Assert.True(result);
        Assert.NotNull(capturedUri);
        Assert.Contains(id.ToString(), capturedUri!.ToString());
        Assert.Equal("DELETE", capturedMethod);
    }

    [Fact]
    public async Task DeleteAsync_Returns_False_On_404_NotFound()
    {
        var client = BuildClient((req, ct) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("""{"detail":"Document not found."}""", Encoding.UTF8, "application/json")
            }));

        var result = await client.DeleteAsync(Guid.NewGuid());

        Assert.False(result);
    }

    [Fact]
    public async Task GetAsync_Returns_Null_On_404()
    {
        var client = BuildClient((req, ct) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("""{"detail":"Document not found."}""", Encoding.UTF8, "application/json")
            }));

        var result = await client.GetAsync(Guid.NewGuid());

        Assert.Null(result);
    }
}
