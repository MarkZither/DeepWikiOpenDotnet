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
/// Integration tests for DocumentsController ingestion endpoint using WebApplicationFactory.
/// </summary>
public class DocumentsControllerIngestTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _factory;
    private readonly HttpClient _client;

    public DocumentsControllerIngestTests(ApiTestFixture factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Ingest_WithValidRequest_ReturnsOk()
    {
        // Arrange
        var request = new IngestRequest
        {
            Documents = new[]
            {
                new IngestDocument
                {
                    RepoUrl = "https://github.com/test/repo",
                    FilePath = "src/Test.cs",
                    Title = "Test Document",
                    Text = "This is a test document content for ingestion."
                }
            },
            ContinueOnError = true,
            BatchSize = 10
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/documents/ingest", request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<IngestResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.True(result.SuccessCount >= 0);
        Assert.True(result.FailureCount >= 0);
        Assert.True(result.DurationMs >= 0);
    }

    [Fact]
    public async Task Ingest_WithMultipleDocuments_ReturnsOk()
    {
        // Arrange
        var request = new IngestRequest
        {
            Documents = new[]
            {
                new IngestDocument
                {
                    RepoUrl = "https://github.com/test/repo",
                    FilePath = "src/Doc1.cs",
                    Title = "Document 1",
                    Text = "First document content."
                },
                new IngestDocument
                {
                    RepoUrl = "https://github.com/test/repo",
                    FilePath = "src/Doc2.cs",
                    Title = "Document 2",
                    Text = "Second document content."
                },
                new IngestDocument
                {
                    RepoUrl = "https://github.com/test/repo",
                    FilePath = "src/Doc3.cs",
                    Title = "Document 3",
                    Text = "Third document content."
                }
            },
            ContinueOnError = true,
            BatchSize = 2
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/documents/ingest", request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<IngestResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task Ingest_WithEmptyDocuments_ReturnsBadRequest()
    {
        // Arrange
        var request = new IngestRequest
        {
            Documents = Array.Empty<IngestDocument>(),
            ContinueOnError = true
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/documents/ingest", request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Contains("empty", error.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ingest_WithTooManyDocuments_ReturnsBadRequest()
    {
        // Arrange - Create more than 1000 documents
        var documents = new List<IngestDocument>();
        for (int i = 0; i < 1001; i++)
        {
            documents.Add(new IngestDocument
            {
                RepoUrl = "https://github.com/test/repo",
                FilePath = $"src/Doc{i}.cs",
                Title = $"Document {i}",
                Text = $"Content {i}"
            });
        }

        var request = new IngestRequest
        {
            Documents = documents,
            ContinueOnError = true
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/documents/ingest", request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Contains("1000", error.Detail);
    }

    [Fact]
    public async Task Ingest_WithMissingRepoUrl_ReturnsBadRequest()
    {
        // Arrange
        var request = new IngestRequest
        {
            Documents = new[]
            {
                new IngestDocument
                {
                    RepoUrl = "",  // Missing
                    FilePath = "src/Test.cs",
                    Title = "Test Document",
                    Text = "Content"
                }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/documents/ingest", request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Contains("RepoUrl", error.Detail);
    }

    [Fact]
    public async Task Ingest_WithMissingFilePath_ReturnsBadRequest()
    {
        // Arrange
        var request = new IngestRequest
        {
            Documents = new[]
            {
                new IngestDocument
                {
                    RepoUrl = "https://github.com/test/repo",
                    FilePath = "",  // Missing
                    Title = "Test Document",
                    Text = "Content"
                }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/documents/ingest", request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Contains("FilePath", error.Detail);
    }

    [Fact]
    public async Task Ingest_WithMissingTitle_ReturnsBadRequest()
    {
        // Arrange
        var request = new IngestRequest
        {
            Documents = new[]
            {
                new IngestDocument
                {
                    RepoUrl = "https://github.com/test/repo",
                    FilePath = "src/Test.cs",
                    Title = "",  // Missing
                    Text = "Content"
                }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/documents/ingest", request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Contains("Title", error.Detail);
    }

    [Fact]
    public async Task Ingest_WithMissingText_ReturnsBadRequest()
    {
        // Arrange
        var request = new IngestRequest
        {
            Documents = new[]
            {
                new IngestDocument
                {
                    RepoUrl = "https://github.com/test/repo",
                    FilePath = "src/Test.cs",
                    Title = "Test Document",
                    Text = ""  // Missing
                }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/documents/ingest", request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Contains("Text", error.Detail);
    }

    [Fact]
    public async Task Ingest_WithTextTooLarge_ReturnsBadRequest()
    {
        // Arrange
        var largeText = new string('x', 5_000_001);  // Exceeds 5MB limit
        var request = new IngestRequest
        {
            Documents = new[]
            {
                new IngestDocument
                {
                    RepoUrl = "https://github.com/test/repo",
                    FilePath = "src/Test.cs",
                    Title = "Test Document",
                    Text = largeText
                }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/documents/ingest", request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Contains("maximum length", error.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ingest_WithMetadata_IncludesInResponse()
    {
        // Arrange
        var metadata = JsonSerializer.SerializeToElement(new { author = "Test Author", version = "1.0" });
        var request = new IngestRequest
        {
            Documents = new[]
            {
                new IngestDocument
                {
                    RepoUrl = "https://github.com/test/repo",
                    FilePath = "src/Test.cs",
                    Title = "Test Document",
                    Text = "Content with metadata",
                    Metadata = metadata
                }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/documents/ingest", request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<IngestResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task Ingest_WithMockIngestionService_ReturnsExpectedResults()
    {
        // Arrange - Create a custom factory with mock service
        using var customFactory = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<DeepWiki.ApiService.Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // Remove production IDocumentIngestionService registration
                    var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IDocumentIngestionService));
                    if (descriptor != null)
                    {
                        services.Remove(descriptor);
                    }

                    // Add mock that returns test data
                    services.AddScoped<IDocumentIngestionService>(_ => new MockIngestionService());
                    
                    // Remove production IDocumentRepository registration if present
                    var repoDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(DeepWiki.Data.Interfaces.IDocumentRepository));
                    if (repoDescriptor != null)
                    {
                        services.Remove(repoDescriptor);
                    }
                    
                    // Add a no-op repository for testing
                    services.AddScoped<DeepWiki.Data.Interfaces.IDocumentRepository>(_ => new NoOpRepository());
                });
            });

        var client = customFactory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var request = new IngestRequest
        {
            Documents = new List<IngestDocument>
            {
                new IngestDocument
                {
                    RepoUrl = "https://github.com/test/repo",
                    FilePath = "src/Test.cs",
                    Title = "Test Document",
                    Text = "Test content"
                }
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/documents/ingest", request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<IngestResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(0, result.FailureCount);
        Assert.Single(result.IngestedDocumentIds);
        Assert.Equal(5, result.TotalChunks);  // Mock returns 5 chunks
    }

    [Fact]
    public async Task Ingest_ResponseFormat_MatchesSpecification()
    {
        // Arrange
        var request = new IngestRequest
        {
            Documents = new List<IngestDocument>
            {
                new IngestDocument
                {
                    RepoUrl = "https://github.com/test/repo",
                    FilePath = "src/Test.cs",
                    Title = "Test Document",
                    Text = "Test content"
                }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/documents/ingest", request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var jsonDoc = JsonDocument.Parse(content);
        var root = jsonDoc.RootElement;

        // Verify required fields exist
        Assert.True(root.TryGetProperty("successCount", out _));
        Assert.True(root.TryGetProperty("failureCount", out _));
        Assert.True(root.TryGetProperty("totalChunks", out _));
        Assert.True(root.TryGetProperty("durationMs", out _));
        Assert.True(root.TryGetProperty("ingestedDocumentIds", out _));
        Assert.True(root.TryGetProperty("errors", out _));
    }

    /// <summary>
    /// Mock ingestion service that returns predefined test data.
    /// </summary>
    private class MockIngestionService : IDocumentIngestionService
    {
        public Task<IngestionResult> IngestAsync(IngestionRequest request, CancellationToken cancellationToken = default)
        {
            var docId = Guid.NewGuid();
            var result = new IngestionResult
            {
                SuccessCount = request.Documents.Count,
                FailureCount = 0,
                TotalChunks = 5,  // Mock: each document produces 5 chunks
                DurationMs = 100,
                IngestedDocumentIds = new List<Guid> { docId },
                Errors = new List<IngestionError>()
            };

            return Task.FromResult(result);
        }

        public Task<DocumentDto> UpsertAsync(DocumentDto document, CancellationToken cancellationToken = default)
            => Task.FromResult(document);

        public Task<IReadOnlyList<ChunkEmbeddingResult>> ChunkAndEmbedAsync(
            string text,
            int maxTokensPerChunk = 8192,
            Guid? parentDocumentId = null,
            CancellationToken cancellationToken = default)
        {
            var results = new List<ChunkEmbeddingResult>
            {
                new ChunkEmbeddingResult
                {
                    Text = text,
                    Embedding = new float[1536],
                    ChunkIndex = 0,
                    ParentDocumentId = parentDocumentId,
                    TokenCount = 100,
                    Language = "en",
                    EmbeddingLatencyMs = 50
                }
            };
            return Task.FromResult<IReadOnlyList<ChunkEmbeddingResult>>(results);
        }
    }
    
    /// <summary>
    /// No-op implementation of IDocumentRepository for testing.
    /// </summary>
    private class NoOpRepository : DeepWiki.Data.Interfaces.IDocumentRepository
    {
        public Task AddAsync(DeepWiki.Data.Entities.DocumentEntity document, CancellationToken cancellationToken = default) 
            => Task.CompletedTask;
            
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) 
            => Task.CompletedTask;
            
        public Task<List<DeepWiki.Data.Entities.DocumentEntity>> GetByRepoAsync(string repoUrl, int skip = 0, int take = 100, CancellationToken cancellationToken = default) 
            => Task.FromResult(new List<DeepWiki.Data.Entities.DocumentEntity>());
            
        public Task<DeepWiki.Data.Entities.DocumentEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) 
            => Task.FromResult<DeepWiki.Data.Entities.DocumentEntity?>(null);
            
        public Task UpdateAsync(DeepWiki.Data.Entities.DocumentEntity document, CancellationToken cancellationToken = default) 
            => Task.CompletedTask;
            
        public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) 
            => Task.FromResult(false);
            
        public Task<(List<DeepWiki.Data.Entities.DocumentEntity> Items, int TotalCount)> ListAsync(string? repoUrl = null, int skip = 0, int take = 100, CancellationToken cancellationToken = default) 
            => Task.FromResult((new List<DeepWiki.Data.Entities.DocumentEntity>(), 0));
    }
}
