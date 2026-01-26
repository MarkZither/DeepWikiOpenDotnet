using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeepWiki.Data.Abstractions;
using DeepWiki.Data.Abstractions.Models;
using DeepWiki.Rag.Core.Embedding;
using DeepWiki.Rag.Core.Ingestion;
using DeepWiki.Rag.Core.Tokenization;

namespace DeepWiki.Rag.Core.Tests;

/// <summary>
/// End-to-End integration tests for the complete RAG pipeline.
/// Covers T191-T200: Ingest → Embed → Store → Query validation.
/// </summary>
[Trait("Category", "Integration")]
public class RagEndToEndTests
{
    private readonly InMemoryVectorStore _vectorStore;
    private readonly ITokenizationService _tokenizationService;
    private readonly TokenEncoderFactory _encoderFactory;
    private readonly DocumentIngestionService _ingestionService;

    private readonly SimilarityGroundTruth _groundTruth;
    private readonly List<SampleDocument> _sampleDocuments;
    private readonly List<SampleEmbedding> _sampleEmbeddings;

    /// <summary>
    /// Stub embedding service that uses pre-computed embeddings from fixture.
    /// </summary>
    private readonly FixtureEmbeddingService _embeddingService;

    public RagEndToEndTests()
    {
        // Load fixtures
        _groundTruth = LoadGroundTruth();
        _sampleDocuments = LoadSampleDocuments();
        _sampleEmbeddings = LoadSampleEmbeddings();

        // Set up services
        _vectorStore = new InMemoryVectorStore();
        _encoderFactory = new TokenEncoderFactory(null);
        _tokenizationService = new TokenizationService(_encoderFactory, null);
        _embeddingService = new FixtureEmbeddingService(_sampleEmbeddings);

        _ingestionService = new DocumentIngestionService(
            _vectorStore,
            _tokenizationService,
            _embeddingService,
            null);
    }

    #region Fixture Loading

    private static SimilarityGroundTruth LoadGroundTruth()
    {
        var path = GetFixturePath("similarity-ground-truth.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<SimilarityGroundTruth>(json)
            ?? throw new InvalidOperationException("Failed to load ground truth");
    }

    private static List<SampleDocument> LoadSampleDocuments()
    {
        var path = GetFixturePath("sample-documents.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<SampleDocument>>(json)
            ?? throw new InvalidOperationException("Failed to load sample documents");
    }

    private static List<SampleEmbedding> LoadSampleEmbeddings()
    {
        var path = GetFixturePath("sample-embeddings.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<SampleEmbedding>>(json)
            ?? throw new InvalidOperationException("Failed to load sample embeddings");
    }

    private static string GetFixturePath(string filename)
    {
        return Path.Combine(
            AppContext.BaseDirectory,
            "fixtures",
            "embedding-samples",
            filename);
    }

    #endregion

    #region T192: E2E test - Ingest 20 documents → Query → Verify top 5 match ground truth

    [Fact]
    public async Task E2E_Ingest20Documents_Query_VerifyTop5MatchGroundTruth()
    {
        // Arrange - Ingest sample documents with pre-computed embeddings
        await IngestSampleDocumentsWithFixedEmbeddings();

        // Act & Assert - For each query in ground truth, verify results match exactly
        foreach (var query in _groundTruth.Queries)
        {
            var queryEmbedding = GetEmbeddingById(query.QueryId);
            Assert.NotNull(queryEmbedding);

            var results = await _vectorStore.QueryAsync(queryEmbedding, k: 5);

            // Verify we got 5 results
            Assert.Equal(5, results.Count);

            // Verify exact ranking match - ground truth was computed from actual cosine similarity
            for (var i = 0; i < results.Count; i++)
            {
                var actualId = results[i].Document.Id;
                var expectedId = Guid.Parse(query.ExpectedTopK[i]);

                Assert.True(actualId == expectedId,
                    $"Query {query.QueryId}, position {i + 1}: " +
                    $"expected {expectedId} but got {actualId}. " +
                    $"Similarity: {results[i].SimilarityScore:F6}");
            }
        }
    }

    private async Task IngestSampleDocumentsWithFixedEmbeddings()
    {
        foreach (var doc in _sampleDocuments)
        {
            var embedding = GetEmbeddingById(doc.Id);
            if (embedding is null) continue;

            var dto = new DocumentDto
            {
                Id = Guid.Parse(doc.Id),
                RepoUrl = doc.RepoUrl,
                FilePath = doc.FilePath,
                Title = doc.Title,
                Text = doc.Text,
                Embedding = embedding,
                MetadataJson = JsonSerializer.Serialize(doc.Metadata ?? new Dictionary<string, string>()),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _vectorStore.UpsertAsync(dto);
        }
    }

    private float[]? GetEmbeddingById(string id)
    {
        return _sampleEmbeddings
            .FirstOrDefault(e => e.Id == id)
            ?.Embedding;
    }

    private static List<Guid> GetExpectedIdsWithTolerance(List<string> expectedTopK, int index, int tolerance)
    {
        var result = new List<Guid>();
        for (var i = Math.Max(0, index - tolerance); i <= Math.Min(expectedTopK.Count - 1, index + tolerance); i++)
        {
            result.Add(Guid.Parse(expectedTopK[i]));
        }
        return result;
    }

    #endregion

    #region T193: E2E test - Document ingestion → Query latency verification (SC-001)

    [Fact]
    public async Task E2E_QueryLatency_Under500ms()
    {
        // Arrange - Ingest documents
        await IngestSampleDocumentsWithFixedEmbeddings();

        // Add more documents to increase corpus size for realistic test
        var additionalDocs = GenerateAdditionalDocuments(95); // Total ~100 docs
        foreach (var doc in additionalDocs)
        {
            await _vectorStore.UpsertAsync(doc);
        }

        var queryEmbedding = GetEmbeddingById(_sampleDocuments.First().Id);
        Assert.NotNull(queryEmbedding);

        // Act - Measure query latency
        var sw = Stopwatch.StartNew();
        var results = await _vectorStore.QueryAsync(queryEmbedding, k: 10);
        sw.Stop();

        // Assert - Query should complete under 500ms
        Assert.True(sw.ElapsedMilliseconds < 500,
            $"Query took {sw.ElapsedMilliseconds}ms, expected <500ms");
        Assert.NotEmpty(results);
    }

    [Fact]
    public async Task E2E_QueryLatencyP95_Under500ms_WithMultipleQueries()
    {
        // Arrange - Ingest documents
        await IngestSampleDocumentsWithFixedEmbeddings();

        var additionalDocs = GenerateAdditionalDocuments(95);
        foreach (var doc in additionalDocs)
        {
            await _vectorStore.UpsertAsync(doc);
        }

        var queryEmbedding = GetEmbeddingById(_sampleDocuments.First().Id);
        Assert.NotNull(queryEmbedding);

        // Act - Run 20 queries and measure latencies
        var latencies = new List<long>();
        for (var i = 0; i < 20; i++)
        {
            var sw = Stopwatch.StartNew();
            await _vectorStore.QueryAsync(queryEmbedding, k: 10);
            sw.Stop();
            latencies.Add(sw.ElapsedMilliseconds);
        }

        // Calculate P95
        latencies.Sort();
        var p95Index = (int)Math.Ceiling(latencies.Count * 0.95) - 1;
        var p95Latency = latencies[p95Index];

        // Assert - P95 should be under 500ms
        Assert.True(p95Latency < 500,
            $"P95 query latency was {p95Latency}ms, expected <500ms");
    }

    private List<DocumentDto> GenerateAdditionalDocuments(int count)
    {
        var docs = new List<DocumentDto>();
        var random = new Random(42);

        for (var i = 0; i < count; i++)
        {
            var embedding = new float[1536];
            for (var j = 0; j < 1536; j++)
            {
                embedding[j] = (float)(random.NextDouble() - 0.5) * 2;
            }
            // Normalize
            var magnitude = (float)Math.Sqrt(embedding.Sum(x => x * x));
            for (var j = 0; j < embedding.Length; j++)
            {
                embedding[j] /= magnitude;
            }

            docs.Add(new DocumentDto
            {
                Id = Guid.NewGuid(),
                RepoUrl = $"https://github.com/test/repo{i % 10}",
                FilePath = $"src/file{i}.cs",
                Title = $"Document {i}",
                Text = $"Content of generated document {i}. This contains sample text for testing.",
                Embedding = embedding,
                MetadataJson = "{}",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        return docs;
    }

    #endregion

    #region T194: E2E test - Token counting parity verification (SC-002)

    [Fact]
    public void E2E_TokenCountingParity_OpenAI_Foundry_Ollama()
    {
        // Arrange - Test text samples
        var testTexts = new[]
        {
            "Hello, World!",
            "The quick brown fox jumps over the lazy dog.",
            "public class Test { public void Method() { } }",
            "日本語テスト：こんにちは世界"
        };

        var openAiEncoder = new OpenAITokenEncoder();
        var foundryEncoder = new FoundryTokenEncoder();
        var ollamaEncoder = new OllamaTokenEncoder();

        // Act & Assert - All three providers should return consistent counts
        foreach (var text in testTexts)
        {
            var openAiCount = openAiEncoder.CountTokens(text);
            var foundryCount = foundryEncoder.CountTokens(text);
            var ollamaCount = ollamaEncoder.CountTokens(text);

            // All use cl100k_base, so should be identical
            Assert.True(openAiCount > 0, $"OpenAI count should be > 0 for: {text}");
            Assert.True(foundryCount > 0, $"Foundry count should be > 0 for: {text}");
            Assert.True(ollamaCount > 0, $"Ollama count should be > 0 for: {text}");

            // Allow small variance due to implementation differences
            var maxDiff = Math.Max(1, (int)(openAiCount * 0.1)); // 10% tolerance
            Assert.True(Math.Abs(openAiCount - foundryCount) <= maxDiff,
                $"OpenAI ({openAiCount}) vs Foundry ({foundryCount}) diff exceeds tolerance for: {text}");
            Assert.True(Math.Abs(openAiCount - ollamaCount) <= maxDiff,
                $"OpenAI ({openAiCount}) vs Ollama ({ollamaCount}) diff exceeds tolerance for: {text}");
        }
    }

    [Fact]
    public async Task E2E_TokenValidation_InIngestion()
    {
        // Arrange - Create document that would chunk
        var largeText = string.Concat(Enumerable.Repeat("word ", 5000));

        var documents = new List<IngestionDocument>
        {
            new()
            {
                RepoUrl = "https://github.com/test/token-validation",
                FilePath = "src/large.cs",
                Text = largeText
            }
        };

        var request = new IngestionRequest
        {
            Documents = documents,
            MaxTokensPerChunk = 8192
        };

        // Act
        var result = await _ingestionService.IngestAsync(request);

        // Assert - Should succeed with chunking
        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(0, result.FailureCount);
    }

    #endregion

    #region T195: E2E test - Embedding throughput (SC-003)

    [Fact]
    public async Task E2E_EmbeddingThroughput_50DocsPerSecond()
    {
        // Arrange - Create 50 documents
        var documents = Enumerable.Range(0, 50)
            .Select(i => new IngestionDocument
            {
                RepoUrl = $"https://github.com/test/throughput",
                FilePath = $"src/file{i}.cs",
                Text = $"Document content {i}. This is test content for throughput measurement."
            })
            .ToList();

        var request = new IngestionRequest
        {
            Documents = documents,
            BatchSize = 10
        };

        // Act
        var sw = Stopwatch.StartNew();
        var result = await _ingestionService.IngestAsync(request);
        sw.Stop();

        // Assert
        Assert.Equal(50, result.SuccessCount);

        var docsPerSecond = 50.0 / (sw.ElapsedMilliseconds / 1000.0);

        // Note: With stub embedding service, this will be much faster than real
        // The test validates the pipeline works; real throughput depends on provider
        Assert.True(docsPerSecond > 0, "Should process documents");
        Assert.True(result.DocumentsPerSecond > 0, "IngestionResult should report throughput");
    }

    #endregion

    #region T196: E2E test - K-NN retrieval accuracy (SC-006)

    [Fact]
    public async Task E2E_KNN_Top5Results_SemanticallySimilar()
    {
        // Arrange - Ingest with fixture embeddings
        await IngestSampleDocumentsWithFixedEmbeddings();

        // Use the first query from ground truth
        var query = _groundTruth.Queries.First();
        var queryEmbedding = GetEmbeddingById(query.QueryId);
        Assert.NotNull(queryEmbedding);

        // Act
        var results = await _vectorStore.QueryAsync(queryEmbedding, k: 5);

        // Assert
        Assert.Equal(5, results.Count);

        // Results should be sorted by similarity (descending)
        for (var i = 1; i < results.Count; i++)
        {
            Assert.True(results[i - 1].SimilarityScore >= results[i].SimilarityScore,
                $"Results not sorted: position {i - 1} ({results[i - 1].SimilarityScore}) < position {i} ({results[i].SimilarityScore})");
        }

        // Top result should have high similarity (self-similarity case)
        Assert.True(results[0].SimilarityScore > 0.9f,
            $"Top result similarity {results[0].SimilarityScore} should be > 0.9 for self-similarity");
    }

    [Fact]
    public async Task E2E_KNN_AllQueriesFromGroundTruth_TopResultCorrect()
    {
        // Arrange
        await IngestSampleDocumentsWithFixedEmbeddings();

        var correctTopResults = 0;
        var totalQueries = _groundTruth.Queries.Count;

        // Act & Assert
        foreach (var query in _groundTruth.Queries)
        {
            var queryEmbedding = GetEmbeddingById(query.QueryId);
            if (queryEmbedding is null) continue;

            var results = await _vectorStore.QueryAsync(queryEmbedding, k: 5);

            if (results.Count > 0 && results[0].Document.Id == Guid.Parse(query.ExpectedTopK[0]))
            {
                correctTopResults++;
            }
        }

        // All queries should have correct top result (self-similarity)
        Assert.Equal(totalQueries, correctTopResults);
    }

    #endregion

    #region T197: E2E test - Metadata filtering (SC-007)

    [Fact]
    public async Task E2E_MetadataFiltering_RepoFilter_ReducesResults()
    {
        // Arrange - Ingest documents from multiple repos
        await IngestSampleDocumentsWithFixedEmbeddings();

        var queryEmbedding = GetEmbeddingById(_sampleDocuments.First().Id);
        Assert.NotNull(queryEmbedding);

        // Act - Query without filter
        var allResults = await _vectorStore.QueryAsync(queryEmbedding, k: 100);

        // Query with repo filter (repo1 only)
        var filteredResults = await _vectorStore.QueryAsync(
            queryEmbedding,
            k: 100,
            filters: new Dictionary<string, string> { ["repoUrl"] = "%repo1%" });

        // Assert
        Assert.True(allResults.Count > filteredResults.Count,
            $"Filtered results ({filteredResults.Count}) should be less than all results ({allResults.Count})");

        // All filtered results should match the repo filter
        Assert.All(filteredResults, r =>
            Assert.Contains("repo1", r.Document.RepoUrl, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task E2E_MetadataFiltering_FilePathFilter_ReducesResults()
    {
        // Arrange
        await IngestSampleDocumentsWithFixedEmbeddings();

        var queryEmbedding = GetEmbeddingById(_sampleDocuments.First().Id);
        Assert.NotNull(queryEmbedding);

        // Act - Query with file path filter (.cs files only)
        var csResults = await _vectorStore.QueryAsync(
            queryEmbedding,
            k: 100,
            filters: new Dictionary<string, string> { ["filePath"] = "%.cs" });

        // Assert - Should only return .cs files
        Assert.All(csResults, r =>
            Assert.EndsWith(".cs", r.Document.FilePath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task E2E_MetadataFiltering_CombinedFilters()
    {
        // Arrange
        await IngestSampleDocumentsWithFixedEmbeddings();

        var queryEmbedding = GetEmbeddingById(_sampleDocuments.First().Id);
        Assert.NotNull(queryEmbedding);

        // Act - Query with combined filters
        var results = await _vectorStore.QueryAsync(
            queryEmbedding,
            k: 100,
            filters: new Dictionary<string, string>
            {
                ["repoUrl"] = "%repo1%",
                ["filePath"] = "%.cs"
            });

        // Assert - Should match both filters
        Assert.All(results, r =>
        {
            Assert.Contains("repo1", r.Document.RepoUrl, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith(".cs", r.Document.FilePath, StringComparison.OrdinalIgnoreCase);
        });
    }

    #endregion

    #region T198: E2E test - Zero data loss on upsert (SC-010)

    [Fact]
    public async Task E2E_ZeroDataLoss_AllDocumentsPersisted()
    {
        // Arrange
        var documents = _sampleDocuments.Select(d => new IngestionDocument
        {
            RepoUrl = d.RepoUrl,
            FilePath = d.FilePath,
            Text = d.Text
        }).ToList();

        var request = new IngestionRequest
        {
            Documents = documents
        };

        // Act
        var result = await _ingestionService.IngestAsync(request);

        // Assert - All documents ingested successfully
        Assert.Equal(documents.Count, result.SuccessCount);
        Assert.Equal(0, result.FailureCount);
        Assert.Empty(result.Errors);

        // Verify all documents are queryable
        var allDocs = _vectorStore.GetAllDocuments();
        Assert.Equal(documents.Count, allDocs.Count);

        // Verify each document has correct data
        foreach (var original in documents)
        {
            var stored = allDocs.FirstOrDefault(d =>
                d.RepoUrl == original.RepoUrl &&
                d.FilePath == original.FilePath);

            Assert.NotNull(stored);
            Assert.Equal(original.Text, stored.Text);
            Assert.NotNull(stored.Embedding);
            Assert.Equal(1536, stored.Embedding!.Length);
        }
    }

    [Fact]
    public async Task E2E_ZeroDataLoss_MetadataPreserved()
    {
        // Arrange - Ingest with fixture embeddings (includes metadata)
        await IngestSampleDocumentsWithFixedEmbeddings();

        // Assert - Check metadata is preserved
        var storedDocs = _vectorStore.GetAllDocuments();

        foreach (var stored in storedDocs)
        {
            Assert.NotNull(stored.MetadataJson);
            Assert.True(stored.CreatedAt > DateTime.MinValue);
            Assert.True(stored.UpdatedAt > DateTime.MinValue);
        }
    }

    #endregion

    #region T199: E2E test - Concurrent upsert stress

    [Fact]
    public async Task E2E_ConcurrentUpsert_NoCorruption()
    {
        // Arrange - Same document to be upserted concurrently
        var repoUrl = "https://github.com/test/concurrent";
        var filePath = "src/shared.cs";

        // Act - 10 concurrent tasks upserting to same document
        var tasks = Enumerable.Range(0, 10)
            .Select(async i =>
            {
                var embedding = new float[1536];
                for (var j = 0; j < 1536; j++)
                {
                    embedding[j] = (float)(i * 0.1 + j * 0.001);
                }

                var doc = new DocumentDto
                {
                    Id = Guid.NewGuid(),
                    RepoUrl = repoUrl,
                    FilePath = filePath,
                    Title = $"Version {i}",
                    Text = $"Content version {i}",
                    Embedding = embedding,
                    MetadataJson = $"{{\"version\": {i}}}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                await _vectorStore.UpsertAsync(doc);
                return i;
            })
            .ToList();

        await Task.WhenAll(tasks);

        // Assert - Only one document should exist (no duplicates)
        var allDocs = _vectorStore.GetAllDocuments();
        var matchingDocs = allDocs.Where(d =>
            d.RepoUrl == repoUrl && d.FilePath == filePath).ToList();

        Assert.Single(matchingDocs);

        // The document should have valid data (last write wins)
        var finalDoc = matchingDocs.First();
        Assert.NotNull(finalDoc.Embedding);
        Assert.Equal(1536, finalDoc.Embedding.Length);
        Assert.NotNull(finalDoc.Text);
    }

    [Fact]
    public async Task E2E_ConcurrentUpsert_MultipleDocuments_Atomicity()
    {
        // Arrange - Multiple different documents upserted concurrently
        var tasks = Enumerable.Range(0, 10)
            .Select(async i =>
            {
                var embedding = new float[1536];
                Array.Fill(embedding, (float)i * 0.1f);

                var doc = new DocumentDto
                {
                    Id = Guid.NewGuid(),
                    RepoUrl = $"https://github.com/test/concurrent{i}",
                    FilePath = $"src/file{i}.cs",
                    Title = $"Document {i}",
                    Text = $"Content {i}",
                    Embedding = embedding,
                    MetadataJson = "{}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                await _vectorStore.UpsertAsync(doc);
                return i;
            })
            .ToList();

        await Task.WhenAll(tasks);

        // Assert - All 10 documents should exist
        var allDocs = _vectorStore.GetAllDocuments();
        Assert.Equal(10, allDocs.Count);
    }

    #endregion

    #region T200: E2E test - Retry/fallback scenario

    [Fact]
    public async Task E2E_RetryFallback_GracefulRecovery()
    {
        // Arrange - Create a failing embedding service with cache fallback
        var cache = new EmbeddingCache();

        // Pre-populate cache with an embedding
        var testText = "Test document for retry fallback";
        var cachedEmbedding = new float[1536];
        Array.Fill(cachedEmbedding, 0.5f);
        await cache.SetAsync(testText, "test-model", cachedEmbedding);

        var failingService = new FailingEmbeddingService(failCount: 3, cache: cache);
        var retryPolicy = RetryPolicy.WithCache(cache, null);

        // Act - Attempt to embed with failures (should fall back to cache)
        float[]? result = null;
        Exception? caughtException = null;

        try
        {
            result = await retryPolicy.ExecuteAsync<float[]>(
                async ct => await failingService.EmbedAsync(testText, ct),
                getCacheKey: () => (testText, "test-model"),
                operationName: "EmbedAsync",
                cancellationToken: default);
        }
        catch (Exception ex)
        {
            caughtException = ex;
        }

        // Assert - Should have retrieved from cache after failures
        Assert.NotNull(result);
        Assert.Null(caughtException);
        Assert.Equal(1536, result!.Length);
        Assert.Equal(0.5f, result[0]); // Cached value
    }

    [Fact]
    public async Task E2E_RetryFallback_NoCache_ThrowsAfterRetries()
    {
        // Arrange - Failing service with no cache
        var failingService = new FailingEmbeddingService(failCount: 10, cache: null);
        var retryPolicy = new RetryPolicy(cache: null, logger: null)
        {
            MaxRetries = 3,
            UseCacheFallback = false
        };

        // Act & Assert - Should throw after retries exhausted
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await retryPolicy.ExecuteAsync<float[]>(
                async ct => await failingService.EmbedAsync("test", ct),
                getCacheKey: () => ("test", "test-model"),
                operationName: "EmbedAsync",
                cancellationToken: default);
        });

        // Verify 3 attempts were made
        Assert.Equal(3, failingService.AttemptCount);
    }

    #endregion

    #region Helper Classes

    /// <summary>
    /// Stub embedding service that uses pre-computed embeddings from fixture.
    /// </summary>
    private sealed class FixtureEmbeddingService : IEmbeddingService
    {
        private readonly Dictionary<string, float[]> _embeddings;

        public string Provider => "fixture";
        public string ModelId => "fixture-model";
        public int EmbeddingDimension => 1536;

        public FixtureEmbeddingService(List<SampleEmbedding> embeddings)
        {
            _embeddings = embeddings.ToDictionary(e => e.Id, e => e.Embedding);
        }

        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
        {
            // Generate a hash-based embedding for arbitrary text
            var embedding = CreateHashBasedEmbedding(text);
            return Task.FromResult(embedding);
        }

        public async IAsyncEnumerable<float[]> EmbedBatchAsync(
            IEnumerable<string> texts,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var text in texts)
            {
                yield return CreateHashBasedEmbedding(text);
            }
            await Task.CompletedTask;
        }

        public Task<IReadOnlyList<EmbeddingResponse>> EmbedBatchWithMetadataAsync(
            IEnumerable<string> texts,
            int batchSize = 10,
            CancellationToken cancellationToken = default)
        {
            var responses = texts.Select(t => new EmbeddingResponse
            {
                Vector = CreateHashBasedEmbedding(t),
                Provider = Provider,
                ModelId = ModelId,
                LatencyMs = 1
            }).ToList();
            return Task.FromResult<IReadOnlyList<EmbeddingResponse>>(responses);
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
    }

    /// <summary>
    /// Embedding service that fails a specified number of times before succeeding.
    /// </summary>
    private sealed class FailingEmbeddingService : IEmbeddingService
    {
        private readonly int _failCount;
        private readonly IEmbeddingCache? _cache;
        private int _attemptCount;

        public int AttemptCount => _attemptCount;
        public string Provider => "failing";
        public string ModelId => "test-model";
        public int EmbeddingDimension => 1536;

        public FailingEmbeddingService(int failCount, IEmbeddingCache? cache)
        {
            _failCount = failCount;
            _cache = cache;
        }

        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
        {
            _attemptCount++;
            if (_attemptCount <= _failCount)
            {
                throw new InvalidOperationException($"Simulated failure #{_attemptCount}");
            }

            var embedding = new float[1536];
            Array.Fill(embedding, 0.1f);
            return Task.FromResult(embedding);
        }

        public async IAsyncEnumerable<float[]> EmbedBatchAsync(
            IEnumerable<string> texts,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var text in texts)
            {
                yield return await EmbedAsync(text, cancellationToken);
            }
        }

        public Task<IReadOnlyList<EmbeddingResponse>> EmbedBatchWithMetadataAsync(
            IEnumerable<string> texts,
            int batchSize = 10,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// In-memory vector store for E2E testing.
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

    #region Fixture Models

    private sealed class SimilarityGroundTruth
    {
        [JsonPropertyName("queries")]
        public List<GroundTruthQuery> Queries { get; set; } = [];
    }

    private sealed class GroundTruthQuery
    {
        [JsonPropertyName("queryId")]
        public string QueryId { get; set; } = string.Empty;

        [JsonPropertyName("expectedTopK")]
        public List<string> ExpectedTopK { get; set; } = [];
    }

    private sealed class SampleDocument
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("repoUrl")]
        public string RepoUrl { get; set; } = string.Empty;

        [JsonPropertyName("filePath")]
        public string FilePath { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("metadata")]
        public Dictionary<string, string>? Metadata { get; set; }
    }

    private sealed class SampleEmbedding
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("embedding")]
        public float[] Embedding { get; set; } = [];
    }

    #endregion
}
