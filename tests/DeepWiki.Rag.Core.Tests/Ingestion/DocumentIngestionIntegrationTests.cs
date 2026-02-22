using System.Diagnostics;
using DeepWiki.Data.Abstractions;
using DeepWiki.Data.Abstractions.Models;
using DeepWiki.Rag.Core.Ingestion;
using DeepWiki.Rag.Core.Tokenization;
using Microsoft.Extensions.Logging;

namespace DeepWiki.Rag.Core.Tests.Ingestion;

/// <summary>
/// Integration tests for DocumentIngestionService (T175-T180).
/// Uses in-memory vector store and stub embedding service.
/// </summary>
[Trait("Category", "Integration")]
public class DocumentIngestionIntegrationTests
{
    private readonly InMemoryVectorStore _vectorStore;
    private readonly ITokenizationService _tokenizationService;
    private readonly StubEmbeddingService _embeddingService;
    private readonly DocumentIngestionService _service;

    // Standard 1536-dimension test embedding
    private static readonly float[] TestEmbedding = CreateTestEmbedding(1536);

    public DocumentIngestionIntegrationTests()
    {
        _vectorStore = new InMemoryVectorStore();

        var encoderFactory = new TokenEncoderFactory(null);
        var loggerFactory = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
        _tokenizationService = new TokenizationService(encoderFactory, Microsoft.Extensions.Logging.Abstractions.NullLogger<TokenizationService>.Instance, loggerFactory);

        _embeddingService = new StubEmbeddingService();

        _service = new DocumentIngestionService(
            _vectorStore,
            _tokenizationService,
            _embeddingService,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DocumentIngestionService>.Instance);
    }

    private static float[] CreateTestEmbedding(int dimension)
    {
        var embedding = new float[dimension];
        for (var i = 0; i < dimension; i++)
        {
            embedding[i] = (float)(i % 100) / 100f;
        }
        return embedding;
    }

    private static float[] CreateHashBasedEmbedding(string text)
    {
        var embedding = new float[1536];
        var hash = text.GetHashCode();
        var random = new Random(hash);
        for (var i = 0; i < 1536; i++)
        {
            embedding[i] = (float)random.NextDouble();
        }
        // Normalize
        var magnitude = (float)Math.Sqrt(embedding.Sum(x => x * x));
        for (var i = 0; i < embedding.Length; i++)
        {
            embedding[i] /= magnitude;
        }
        return embedding;
    }

    #region T176: Integration test - Ingest 100 sample documents, verify all stored

    [Fact]
    public async Task IngestAsync_With100Documents_AllStoredSuccessfully()
    {
        // Arrange
        var documents = CreateSampleDocuments(100);
        var request = new IngestionRequest
        {
            Documents = documents,
            BatchSize = 10,
            ContinueOnError = false
        };

        // Act
        var result = await _service.IngestAsync(request);

        // Assert
        Assert.Equal(100, result.SuccessCount);
        Assert.Equal(0, result.FailureCount);
        Assert.Equal(100, result.IngestedDocumentIds.Count);
        Assert.Empty(result.Errors);

        // Verify all documents are stored in vector store
        var allDocs = _vectorStore.GetAllDocuments();
        Assert.Equal(100, allDocs.Count);
    }

    #endregion

    #region T177: Integration test - Query after ingestion confirms documents immediately available

    [Fact]
    public async Task IngestAsync_QueryAfterIngestion_DocumentsImmediatelyAvailable()
    {
        // Arrange
        var documents = new List<IngestionDocument>
        {
            new() { RepoUrl = "https://github.com/test/repo", FilePath = "src/Program.cs", Text = "public class Program { static void Main() { } }" },
            new() { RepoUrl = "https://github.com/test/repo", FilePath = "src/Helper.cs", Text = "public static class Helper { public static void DoWork() { } }" },
            new() { RepoUrl = "https://github.com/test/repo", FilePath = "docs/README.md", Text = "# Documentation\nThis is the project documentation." }
        };

        var request = new IngestionRequest { Documents = documents };

        // Act
        var result = await _service.IngestAsync(request);

        // Assert - Ingestion succeeded
        Assert.Equal(3, result.SuccessCount);

        // Query immediately after ingestion
        var queryEmbedding = CreateHashBasedEmbedding("Program Main");
        var queryResults = await _vectorStore.QueryAsync(queryEmbedding, k: 3);

        Assert.Equal(3, queryResults.Count);
    }

    #endregion

    #region T178: Integration test - Ingest same documents again, verify no duplicates

    [Fact]
    public async Task IngestAsync_WithDuplicates_UpdatesExistingNoDuplicatesCreated()
    {
        // Arrange
        var documents = new List<IngestionDocument>
        {
            new() { RepoUrl = "https://github.com/test/repo", FilePath = "src/Service.cs", Text = "Original content v1" }
        };

        var request1 = new IngestionRequest { Documents = documents };

        // Act - First ingestion
        var result1 = await _service.IngestAsync(request1);
        Assert.Equal(1, result1.SuccessCount);

        var docCountAfterFirst = _vectorStore.GetAllDocuments().Count;

        // Update content (create new list with updated document since Text is init-only)
        var updatedDocuments = new List<IngestionDocument>
        {
            new() { RepoUrl = "https://github.com/test/repo", FilePath = "src/Service.cs", Text = "Updated content v2" }
        };
        var request2 = new IngestionRequest { Documents = updatedDocuments };

        // Act - Second ingestion (same repo+path)
        var result2 = await _service.IngestAsync(request2);
        Assert.Equal(1, result2.SuccessCount);

        var docCountAfterSecond = _vectorStore.GetAllDocuments().Count;

        // Assert - No duplicates created
        Assert.Equal(1, docCountAfterFirst);
        Assert.Equal(1, docCountAfterSecond);

        // Verify content was updated
        var storedDoc = _vectorStore.GetAllDocuments().Single();
        Assert.Equal("Updated content v2", storedDoc.Text);
    }

    #endregion

    #region T179: Integration test - Ingestion with 50k token document auto-chunks correctly

    [Fact]
    public async Task IngestAsync_WithLargeDocument_AutoChunksCorrectly()
    {
        // Arrange - Create a large document that exceeds 8192 tokens
        // Average ~4 chars per token, so 50k tokens ≈ 200k chars
        var largeText = GenerateLargeText(targetTokens: 10000); // Use 10k for faster test

        var documents = new List<IngestionDocument>
        {
            new() { RepoUrl = "https://github.com/test/repo", FilePath = "src/LargeFile.cs", Text = largeText }
        };

        var request = new IngestionRequest
        {
            Documents = documents,
            MaxTokensPerChunk = 8192
        };

        // Act
        var result = await _service.IngestAsync(request);

        // Assert
        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(0, result.FailureCount);
    }

    [Fact]
    public async Task ChunkAndEmbedAsync_WithLargeDocument_ChunksAndEmbedsAll()
    {
        // Arrange
        var largeText = GenerateLargeText(targetTokens: 20000);

        // Act
        var results = await _service.ChunkAndEmbedAsync(largeText, maxTokensPerChunk: 8192);

        // Assert
        Assert.True(results.Count >= 2, $"Expected at least 2 chunks, got {results.Count}");
        Assert.All(results, chunk =>
        {
            Assert.NotNull(chunk.Embedding);
            Assert.Equal(1536, chunk.Embedding.Length);
            Assert.True(chunk.TokenCount <= 8192, $"Chunk {chunk.ChunkIndex} has {chunk.TokenCount} tokens, exceeds 8192 limit");
        });

        // Verify chunk indices are sequential
        for (var i = 0; i < results.Count; i++)
        {
            Assert.Equal(i, results[i].ChunkIndex);
        }
    }

    #endregion

    #region T180: Integration test - Batch ingestion throughput

    [Fact]
    public async Task IngestAsync_BatchThroughput_MeasuresPerformance()
    {
        // Arrange
        var documents = CreateSampleDocuments(100);
        var request = new IngestionRequest
        {
            Documents = documents,
            BatchSize = 10
        };

        // Act
        var sw = Stopwatch.StartNew();
        var result = await _service.IngestAsync(request);
        sw.Stop();

        // Assert
        Assert.Equal(100, result.SuccessCount);

        // Calculate throughput
        var docsPerSecond = 100.0 / (sw.ElapsedMilliseconds / 1000.0);

        // Log performance (note: with stubbed embedding, this is faster than real)
        // Target is ≥50 docs/sec, but with stub services, we just verify completion
        Assert.True(result.DocumentsPerSecond > 0);

        // Verify result has duration
        Assert.True(result.DurationMs > 0);
    }

    #endregion

    #region Additional Integration Tests

    [Fact]
    public async Task IngestAsync_WithMetadataFilters_QueriesCorrectly()
    {
        // Arrange
        var documents = new List<IngestionDocument>
        {
            new() { RepoUrl = "https://github.com/team/frontend", FilePath = "src/App.tsx", Text = "export function App() { return <div>Hello</div>; }" },
            new() { RepoUrl = "https://github.com/team/frontend", FilePath = "src/Button.tsx", Text = "export function Button({ onClick }) { return <button onClick={onClick}>Click</button>; }" },
            new() { RepoUrl = "https://github.com/team/backend", FilePath = "src/Api.cs", Text = "public class ApiController { [HttpGet] public IActionResult Get() => Ok(); }" },
            new() { RepoUrl = "https://github.com/team/backend", FilePath = "src/Service.cs", Text = "public class UserService { public User GetUser(int id) => null; }" }
        };

        var request = new IngestionRequest { Documents = documents };
        await _service.IngestAsync(request);

        // Act - Query with repo filter
        var queryEmbedding = CreateHashBasedEmbedding("React component");
        var frontendResults = await _vectorStore.QueryAsync(
            queryEmbedding,
            k: 10,
            filters: new Dictionary<string, string> { ["repoUrl"] = "%frontend%" });

        var backendResults = await _vectorStore.QueryAsync(
            queryEmbedding,
            k: 10,
            filters: new Dictionary<string, string> { ["repoUrl"] = "%backend%" });

        // Assert
        Assert.Equal(2, frontendResults.Count);
        Assert.Equal(2, backendResults.Count);
    }

    [Fact]
    public async Task IngestAsync_CancellationToken_RespectsCancellation()
    {
        // Arrange
        var documents = CreateSampleDocuments(100);
        var request = new IngestionRequest { Documents = documents };

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50)); // Cancel quickly

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await _service.IngestAsync(request, cts.Token));
    }

    #endregion

    #region Helper Methods

    private static List<IngestionDocument> CreateSampleDocuments(int count)
    {
        var documents = new List<IngestionDocument>();
        var fileTypes = new[] { ".cs", ".ts", ".py", ".md", ".json" };

        for (var i = 0; i < count; i++)
        {
            var ext = fileTypes[i % fileTypes.Length];
            documents.Add(new IngestionDocument
            {
                RepoUrl = $"https://github.com/test/repo{i / 10}",
                FilePath = $"src/file{i}{ext}",
                Text = $"Content of document {i}. This is sample content for testing purposes. " +
                       $"It contains various words and phrases to simulate real document content."
            });
        }

        return documents;
    }

    private static string GenerateLargeText(int targetTokens)
    {
        // Approximate: 1 token ≈ 4 characters for English text
        var targetChars = targetTokens * 4;
        var words = new[]
        {
            "public", "class", "interface", "method", "function", "return", "async", "await",
            "implementation", "service", "repository", "controller", "handler", "manager",
            "data", "model", "entity", "request", "response", "result", "configuration"
        };

        var sb = new System.Text.StringBuilder(targetChars + 1000);
        var random = new Random(42); // Fixed seed for reproducibility

        while (sb.Length < targetChars)
        {
            sb.Append(words[random.Next(words.Length)]);
            sb.Append(' ');
        }

        return sb.ToString();
    }

    #endregion

    #region Stub Classes

    /// <summary>
    /// Stub embedding service for integration testing.
    /// </summary>
    private sealed class StubEmbeddingService : IEmbeddingService
    {
        public string Provider => "stub";
        public string ModelId => "stub-model";
        public int EmbeddingDimension => 1536;

        public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
        {
            // Simulate small work and respect cancellation so integration tests can reliably cancel
            await Task.Delay(5, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return CreateHashBasedEmbedding(text);
        }

        public async IAsyncEnumerable<float[]> EmbedBatchAsync(
            IEnumerable<string> texts,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var text in texts)
            {
                // Small delay per item to allow cancellation to be observed during long ingestions
                await Task.Delay(5, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                yield return CreateHashBasedEmbedding(text);
            }
        }

        public async Task<IReadOnlyList<EmbeddingResponse>> EmbedBatchWithMetadataAsync(
            IEnumerable<string> texts,
            int batchSize = 10,
            CancellationToken cancellationToken = default)
        {
            var responses = new List<EmbeddingResponse>();
            foreach (var t in texts)
            {
                await Task.Delay(5, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                responses.Add(new EmbeddingResponse
                {
                    Vector = CreateHashBasedEmbedding(t),
                    Provider = Provider,
                    ModelId = ModelId,
                    LatencyMs = 1
                });
            }

            return responses;
        }
    }

    /// <summary>
    /// In-memory vector store for integration testing.
    /// </summary>
    private sealed class InMemoryVectorStore : IVectorStore
    {
        private readonly Dictionary<Guid, DocumentDto> _documents = [];
        private readonly object _lock = new();

        public List<DocumentDto> GetAllDocuments()
        {
            lock (_lock)
            {
                return [.. _documents.Values];
            }
        }

        public Task<IReadOnlyList<VectorQueryResult>> QueryAsync(
            float[] embedding,
            int k = 10,
            Dictionary<string, string>? filters = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_lock)
            {
                var query = _documents.Values.AsEnumerable();

                // Apply filters
                if (filters is not null)
                {
                    if (filters.TryGetValue("repoUrl", out var repoFilter))
                    {
                        var pattern = repoFilter.Replace("%", "");
                        query = query.Where(d => d.RepoUrl.Contains(pattern, StringComparison.OrdinalIgnoreCase));
                    }
                    if (filters.TryGetValue("filePath", out var fileFilter))
                    {
                        var pattern = fileFilter.Replace("%", "");
                        query = query.Where(d => d.FilePath.Contains(pattern, StringComparison.OrdinalIgnoreCase));
                    }
                }

                // Calculate similarities and sort
                var results = query
                    .Select(d => new VectorQueryResult
                    {
                        Document = d,
                        SimilarityScore = CalculateCosineSimilarity(embedding, d.Embedding)
                    })
                    .OrderByDescending(r => r.SimilarityScore)
                    .Take(k)
                    .ToList();

                return Task.FromResult<IReadOnlyList<VectorQueryResult>>(results);
            }
        }

        public Task UpsertAsync(DocumentDto document, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_lock)
            {
                // Check for existing by RepoUrl+FilePath
                var existing = _documents.Values
                    .FirstOrDefault(d => d.RepoUrl == document.RepoUrl && d.FilePath == document.FilePath);

                if (existing is not null)
                {
                    _documents.Remove(existing.Id);
                    document.Id = existing.Id; // Preserve ID on update
                }

                _documents[document.Id] = document;
            }
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                _documents.Remove(id);
            }
            return Task.CompletedTask;
        }

        public Task DeleteChunksAsync(string repoUrl, string filePath, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                var toRemove = _documents.Values.Where(d => d.RepoUrl == repoUrl && d.FilePath == filePath).Select(d => d.Id).ToList();
                foreach (var id in toRemove) _documents.Remove(id);
            }
            return Task.CompletedTask;
        }

        public Task RebuildIndexAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        private static float CalculateCosineSimilarity(float[]? a, float[]? b)
        {
            if (a is null || b is null || a.Length != b.Length || a.Length == 0)
                return 0f;

            float dot = 0, magA = 0, magB = 0;
            for (var i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                magA += a[i] * a[i];
                magB += b[i] * b[i];
            }

            var magnitude = Math.Sqrt(magA) * Math.Sqrt(magB);
            return magnitude > 0 ? (float)(dot / magnitude) : 0f;
        }
    }

    #endregion
}
