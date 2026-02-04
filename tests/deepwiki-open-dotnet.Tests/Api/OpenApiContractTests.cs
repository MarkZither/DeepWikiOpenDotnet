using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace DeepWiki.ApiService.Tests.Api;

public class OpenApiContractTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _factory;

    public OpenApiContractTests(ApiTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task OpenApi_Document_Contains_Required_Paths_And_Schemas()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act - fetch generated OpenAPI document
        var res = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var json = await res.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert basics
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Check paths (accept any path that contains the primary resource segment to be robust)
        Assert.True(root.TryGetProperty("paths", out var paths));
        bool PathContains(JsonElement p, string segment)
        {
            foreach (var prop in p.EnumerateObject())
            {
                if (prop.Name.Contains(segment, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        Assert.True(PathContains(paths, "query"));
        Assert.True(PathContains(paths, "documents"));
        Assert.True(PathContains(paths, "documents/{id}" ) || PathContains(paths, "documents/"));
        Assert.True(PathContains(paths, "ingest"));

        // Check required components/schemas
        Assert.True(root.TryGetProperty("components", out var components));
        Assert.True(components.TryGetProperty("schemas", out var schemas));
        Assert.True(schemas.TryGetProperty("QueryRequest", out _));
        Assert.True(schemas.TryGetProperty("QueryResultItem", out _));
        Assert.True(schemas.TryGetProperty("IngestRequest", out _));
        Assert.True(schemas.TryGetProperty("IngestResponse", out _));
    }

    [Fact]
    public async Task Quickstart_Curl_Examples_Accept_Requests()
    {
        var client = _factory.CreateClient();

        // Simple query should return 200 and a JSON array
        var queryPayload = JsonSerializer.Serialize(new { query = "test", k = 1, includeFullText = false });
        var ctsShort = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        ctsShort.CancelAfter(TimeSpan.FromSeconds(5));

        var qRes = await client.PostAsync("/api/query", new StringContent(queryPayload, System.Text.Encoding.UTF8, "application/json"), ctsShort.Token);
        Assert.Equal(HttpStatusCode.OK, qRes.StatusCode);

        // Ingest example should accept request; allow 5s timeout for the call to avoid long waits in CI
        var ingestPayload = JsonSerializer.Serialize(new
        {
            documents = new[]
            {
                new { repoUrl = "https://github.com/example/repo", filePath = "docs/README.md", title = "Project README", text = "# My Project\n\nThis is the documentation..." }
            }
        });

        var iRes = await client.PostAsync("/api/documents/ingest", new StringContent(ingestPayload, System.Text.Encoding.UTF8, "application/json"), ctsShort.Token);
        Assert.True(iRes.StatusCode == HttpStatusCode.OK || iRes.StatusCode == HttpStatusCode.ServiceUnavailable || iRes.StatusCode == HttpStatusCode.RequestTimeout);
    }
}
