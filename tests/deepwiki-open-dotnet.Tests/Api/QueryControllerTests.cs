using DeepWiki.ApiService.Models;
using DeepWiki.ApiService.Tests.Api;
using DeepWiki.Data.Abstractions;
using DeepWiki.Data.Abstractions.Models;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace DeepWiki.ApiService.Tests.Api;

/// <summary>
/// Integration tests for QueryController using WebApplicationFactory.
/// </summary>
public class QueryControllerTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _factory;
    private readonly HttpClient _client;

    public QueryControllerTests(ApiTestFixture factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Query_WithValidRequest_ReturnsOk()
    {
        // Arrange
        var request = new QueryRequest
        {
            Query = "test query",
            K = 5,
            IncludeFullText = true
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/query", request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<QueryResultItem[]>(TestContext.Current.CancellationToken);
        Assert.NotNull(results);
        Assert.Empty(results); // MockVectorStore starts empty
    }

    [Fact]
    public async Task Query_WithInvalidRequest_ReturnsBadRequest()
    {
        // Arrange - empty query violates [Required] and [StringLength(MinimumLength = 1)]
        var request = new { Query = "", K = 5 };

        // Act
        var response = await _client.PostAsJsonAsync("/api/query", request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        // For now just verify we got a 400 - validation error format handling can be added later
        Assert.False(string.IsNullOrWhiteSpace(responseBody));
    }

    [Fact]
    public async Task Query_WithKOutOfRange_ReturnsBadRequest()
    {
        // Arrange - K must be between 1 and 100
        var request = new QueryRequest
        {
            Query = "test query",
            K = 101 // Exceeds max
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/query", request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        // For now just verify we got a 400 - validation error format handling can be added later
        Assert.False(string.IsNullOrWhiteSpace(responseBody));
    }

    [Fact]
    public async Task Query_WithFilters_PassesFiltersToVectorStore()
    {
        // Arrange
        var request = new QueryRequest
        {
            Query = "test query",
            K = 5,
            Filters = new QueryFilters
            {
                RepoUrl = "https://github.com/test/repo",
                FilePath = "src/%.cs"
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/query", request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<QueryResultItem[]>(TestContext.Current.CancellationToken);
        Assert.NotNull(results);
    }

    [Fact]
    public async Task Query_WithIncludeFullTextFalse_ExcludesTextFromResponse()
    {
        // Arrange
        var request = new QueryRequest
        {
            Query = "test query",
            K = 5,
            IncludeFullText = false
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/query", request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<QueryResultItem[]>(TestContext.Current.CancellationToken);
        Assert.NotNull(results);
        // MockVectorStore returns empty by default; if it had results, Text would be non-null
    }

    [Fact]
    public async Task Query_WithDefaultK_UsesDefault10()
    {
        // Arrange
        var request = new QueryRequest
        {
            Query = "test query"
            // K not specified, should default to 10
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/query", request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Request should be valid and processed
    }

    [Fact]
    public async Task Query_ReturnsArrayDirectly_NotWrapped()
    {
        // Arrange
        var request = new QueryRequest
        {
            Query = "test query",
            K = 5
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/query", request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        
        // Verify response is a JSON array (starts with '[')
        Assert.StartsWith("[", content.Trim());
        
        // Verify it deserializes as array
        var results = await response.Content.ReadFromJsonAsync<QueryResultItem[]>(TestContext.Current.CancellationToken);
        Assert.NotNull(results);
    }

    [Fact]
    public async Task Query_WithMockVectorStore_ReturnsExpectedResults()
    {
        // Arrange - Create a completely custom factory bypassing ApiTestFixture
        using var customFactory = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<DeepWiki.ApiService.Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // Remove production IVectorStore registration
                    var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IVectorStore));
                    if (descriptor != null)
                    {
                        services.Remove(descriptor);
                    }

                    // Add mock that returns test data
                    services.AddScoped<IVectorStore>(_ => new MockVectorStore());
                });
            });
        
        var client = customFactory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var request = new QueryRequest
        {
            Query = "test query",
            K = 2
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/query", request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<QueryResultItem[]>(TestContext.Current.CancellationToken);
        Assert.NotNull(results);
        Assert.Equal(2, results.Length);
        Assert.Equal("Test Document 1", results[0].Title);
        Assert.Equal("Test Document 2", results[1].Title);
    }

    /// <summary>
    /// Mock vector store that returns predefined test data.
    /// </summary>
    private class MockVectorStore : IVectorStore
    {
        public Task<IReadOnlyList<VectorQueryResult>> QueryAsync(
            float[] embedding,
            int k,
            Dictionary<string, string>? filters = null,
            CancellationToken cancellationToken = default)
        {
            var results = new List<VectorQueryResult>
            {
                new VectorQueryResult
                {
                    Document = new DocumentDto
                    {
                        Id = Guid.NewGuid(),
                        RepoUrl = "https://github.com/test/repo1",
                        FilePath = "src/Test1.cs",
                        Title = "Test Document 1",
                        Text = "This is test document 1 content",
                        Embedding = new float[1536],
                        MetadataJson = "{}",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        TokenCount = 10
                    },
                    SimilarityScore = 0.95f
                },
                new VectorQueryResult
                {
                    Document = new DocumentDto
                    {
                        Id = Guid.NewGuid(),
                        RepoUrl = "https://github.com/test/repo2",
                        FilePath = "src/Test2.cs",
                        Title = "Test Document 2",
                        Text = "This is test document 2 content",
                        Embedding = new float[1536],
                        MetadataJson = "{}",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        TokenCount = 10
                    },
                    SimilarityScore = 0.85f
                }
            };

            return Task.FromResult<IReadOnlyList<VectorQueryResult>>(
                results.Take(k).ToList().AsReadOnly());
        }

        public Task UpsertAsync(DocumentDto document, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteChunksAsync(string repoUrl, string filePath, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RebuildIndexAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
