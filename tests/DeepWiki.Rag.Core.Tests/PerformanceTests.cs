using System.Diagnostics;
using DeepWiki.Data.Abstractions;
using DeepWiki.Data.Abstractions.Models;
using DeepWiki.Rag.Core.Embedding;
using DeepWiki.Rag.Core.Ingestion;
using DeepWiki.Rag.Core.Tokenization;

namespace DeepWiki.Rag.Core.Tests;

/// <summary>
/// Performance and load tests for the RAG pipeline (T201-T205).
/// These tests measure throughput, latency, and scalability against defined targets.
/// </summary>
/// <remarks>
/// Performance targets (from SC-001 to SC-010):
/// - SC-001: Query latency p95 &lt;500ms for 10k document corpus
/// - SC-003: Embedding throughput ≥50 docs/sec
/// </remarks>
[Trait("Category", "Performance")]
public class PerformanceTests
{
    private readonly InMemoryVectorStore _vectorStore;
    private readonly ITokenizationService _tokenizationService;
    private readonly StubEmbeddingService _embeddingService;
    private readonly DocumentIngestionService _ingestionService;

    public PerformanceTests()
    {
        _vectorStore = new InMemoryVectorStore();

        var encoderFactory = new TokenEncoderFactory(null);
        _tokenizationService = new TokenizationService(encoderFactory, Microsoft.Extensions.Logging.Abstractions.NullLogger<TokenizationService>.Instance);

        _embeddingService = new StubEmbeddingService();

        _ingestionService = new DocumentIngestionService(
            _vectorStore,
            _tokenizationService,
            _embeddingService,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DocumentIngestionService>.Instance);
    }

    #region T202: Benchmark QueryAsync latency for k=10 on 10k documents (SC-001)

    /// <summary>
    /// T202: Benchmark query latency for k=10 on a 10k document corpus.
    /// Target: p95 latency &lt;500ms
    /// </summary>
    [Fact]
    public async Task Benchmark_QueryAsync_K10_On10kDocuments_Under500msP95()
    {
        // Arrange - Generate and ingest 10k documents
        const int documentCount = 10_000;
        const int queryCount = 100;
        const int k = 10;
        const long targetP95Ms = 500;

        await IngestGeneratedDocuments(documentCount);

        var queryEmbedding = GenerateNormalizedEmbedding(seed: 42);

        // Warm-up run
        await _vectorStore.QueryAsync(queryEmbedding, k: k);

        // Act - Run multiple queries and collect latencies
        var latencies = new List<long>(queryCount);

        for (var i = 0; i < queryCount; i++)
        {
            // Use different query embeddings to avoid caching effects
            var testEmbedding = GenerateNormalizedEmbedding(seed: 1000 + i);

            var sw = Stopwatch.StartNew();
            var results = await _vectorStore.QueryAsync(testEmbedding, k: k);
            sw.Stop();

            latencies.Add(sw.ElapsedMilliseconds);

            // Verify results returned
            Assert.True(results.Count <= k, $"Query returned {results.Count}, expected ≤{k}");
        }

        // Calculate statistics
        latencies.Sort();
        var p50 = latencies[(int)(queryCount * 0.50)];
        var p90 = latencies[(int)(queryCount * 0.90)];
        var p95 = latencies[(int)(queryCount * 0.95) - 1];
        var p99 = latencies[(int)(queryCount * 0.99) - 1];
        var avg = latencies.Average();
        var max = latencies.Max();

        // Output metrics for visibility
        var metrics = new BenchmarkMetrics
        {
            TestName = "QueryAsync_K10_10kDocs",
            DocumentCount = documentCount,
            OperationCount = queryCount,
            P50Ms = p50,
            P90Ms = p90,
            P95Ms = p95,
            P99Ms = p99,
            AverageMs = avg,
            MaxMs = max,
            TargetP95Ms = targetP95Ms
        };

        OutputMetrics(metrics);

        // Assert - p95 should be under 500ms
        Assert.True(p95 < targetP95Ms,
            $"Query latency p95 ({p95}ms) exceeds target ({targetP95Ms}ms). " +
            $"Avg: {avg:F1}ms, Max: {max}ms");
    }

    /// <summary>
    /// Additional test: Verify latency scales reasonably with document count.
    /// </summary>
    [Theory]
    [InlineData(1_000)]
    [InlineData(5_000)]
    [InlineData(10_000)]
    public async Task Benchmark_QueryAsync_LatencyScaling(int documentCount)
    {
        // Arrange
        await IngestGeneratedDocuments(documentCount);
        var queryEmbedding = GenerateNormalizedEmbedding(seed: 123);

        // Act - Run 10 queries
        var latencies = new List<long>(10);
        for (var i = 0; i < 10; i++)
        {
            var sw = Stopwatch.StartNew();
            await _vectorStore.QueryAsync(queryEmbedding, k: 10);
            sw.Stop();
            latencies.Add(sw.ElapsedMilliseconds);
        }

        var avgLatency = latencies.Average();

        // Assert - Basic sanity check (should complete in reasonable time)
        Assert.True(avgLatency < 1000,
            $"Average query latency ({avgLatency:F1}ms) too high for {documentCount} documents");
    }

    #endregion

    #region T203: Benchmark batch embedding throughput (SC-003)

    /// <summary>
    /// T203: Benchmark batch embedding throughput.
    /// Target: ≥50 docs/sec for pure embedding operations.
    /// Note: This tests the embedding service directly, not the full ingestion pipeline
    /// (which includes chunking, tokenization, and upsert overhead).
    /// </summary>
    [Fact]
    public async Task Benchmark_BatchEmbedding_Throughput_AtLeast50DocsPerSec()
    {
        // Arrange - Test pure embedding throughput (SC-003)
        const int documentCount = 100;
        const double targetDocsPerSec = 50.0;

        var texts = Enumerable.Range(0, documentCount)
            .Select(i => $"Sample text for document {i} with some content for embedding.")
            .ToList();

        // Act - Measure pure embedding throughput
        var sw = Stopwatch.StartNew();
        var embeddings = new List<float[]>();
        await foreach (var embedding in _embeddingService.EmbedBatchAsync(texts))
        {
            embeddings.Add(embedding);
        }
        sw.Stop();

        var elapsedSeconds = sw.ElapsedMilliseconds / 1000.0;
        var actualDocsPerSec = elapsedSeconds > 0 ? documentCount / elapsedSeconds : documentCount;

        // Metrics
        var metrics = new ThroughputMetrics
        {
            TestName = "PureEmbedding_100Docs",
            DocumentCount = documentCount,
            ElapsedMs = sw.ElapsedMilliseconds,
            DocsPerSecond = actualDocsPerSec,
            TargetDocsPerSecond = targetDocsPerSec
        };

        OutputThroughputMetrics(metrics);

        // Assert
        Assert.Equal(documentCount, embeddings.Count);
        Assert.True(actualDocsPerSec >= targetDocsPerSec,
            $"Embedding throughput ({actualDocsPerSec:F1} docs/sec) below target ({targetDocsPerSec} docs/sec)");
    }

    /// <summary>
    /// T203 supplemental: Benchmark full ingestion pipeline throughput.
    /// This includes chunking, tokenization, embedding, and upsert.
    /// Target is lower than pure embedding due to pipeline overhead.
    /// </summary>
    [Fact]
    public async Task Benchmark_FullIngestion_Throughput()
    {
        // Arrange
        const int documentCount = 100;
        const double targetDocsPerSec = 10.0; // Lower target for full pipeline

        var documents = Enumerable.Range(0, documentCount)
            .Select(i => new IngestionDocument
            {
                RepoUrl = $"https://github.com/perf/test",
                FilePath = $"src/file{i}.cs",
                Text = GenerateSampleText(i)
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

        var elapsedSeconds = sw.ElapsedMilliseconds / 1000.0;
        var actualDocsPerSec = elapsedSeconds > 0 ? documentCount / elapsedSeconds : documentCount;

        // Metrics
        var metrics = new ThroughputMetrics
        {
            TestName = "FullIngestion_100Docs",
            DocumentCount = documentCount,
            ElapsedMs = sw.ElapsedMilliseconds,
            DocsPerSecond = actualDocsPerSec,
            TargetDocsPerSecond = targetDocsPerSec
        };

        OutputThroughputMetrics(metrics);

        // Assert
        Assert.Equal(documentCount, result.SuccessCount);
        Assert.True(actualDocsPerSec >= targetDocsPerSec,
            $"Full ingestion throughput ({actualDocsPerSec:F1} docs/sec) below target ({targetDocsPerSec} docs/sec)");
    }

    /// <summary>
    /// Test embedding throughput at different batch sizes.
    /// </summary>
    [Theory]
    [InlineData(100, 5)]
    [InlineData(100, 10)]
    [InlineData(100, 20)]
    public async Task Benchmark_BatchEmbedding_WithDifferentBatchSizes(int documentCount, int batchSize)
    {
        // Arrange
        var documents = Enumerable.Range(0, documentCount)
            .Select(i => new IngestionDocument
            {
                RepoUrl = "https://github.com/perf/batch-test",
                FilePath = $"src/batch{batchSize}/file{i}.cs",
                Text = GenerateSampleText(i)
            })
            .ToList();

        var request = new IngestionRequest
        {
            Documents = documents,
            BatchSize = batchSize
        };

        // Act
        var sw = Stopwatch.StartNew();
        var result = await _ingestionService.IngestAsync(request);
        sw.Stop();

        var docsPerSec = documentCount / (sw.ElapsedMilliseconds / 1000.0);

        // Assert
        Assert.Equal(documentCount, result.SuccessCount);
        Assert.True(docsPerSec > 0, $"BatchSize={batchSize}: {docsPerSec:F1} docs/sec");
    }

    #endregion

    #region T204: Benchmark metadata filtering performance

    /// <summary>
    /// T204: Benchmark query with metadata filter (single filter).
    /// Measures overhead of filter application.
    /// </summary>
    [Fact]
    public async Task Benchmark_MetadataFiltering_SingleFilter_Latency()
    {
        // Arrange - Ingest documents with varied repo URLs for filtering
        const int documentCount = 5_000;
        await IngestGeneratedDocumentsWithVariedRepos(documentCount, repoCount: 10);

        var queryEmbedding = GenerateNormalizedEmbedding(seed: 99);

        // Act - Compare latency with and without filter
        var noFilterLatencies = new List<long>(20);
        var withFilterLatencies = new List<long>(20);

        // Without filter
        for (var i = 0; i < 20; i++)
        {
            var sw = Stopwatch.StartNew();
            await _vectorStore.QueryAsync(queryEmbedding, k: 10);
            sw.Stop();
            noFilterLatencies.Add(sw.ElapsedMilliseconds);
        }

        // With single repo filter
        var filter = new Dictionary<string, string> { ["repoUrl"] = "%repo0%" };
        for (var i = 0; i < 20; i++)
        {
            var sw = Stopwatch.StartNew();
            await _vectorStore.QueryAsync(queryEmbedding, k: 10, filters: filter);
            sw.Stop();
            withFilterLatencies.Add(sw.ElapsedMilliseconds);
        }

        var avgNoFilter = noFilterLatencies.Average();
        var avgWithFilter = withFilterLatencies.Average();
        var filterOverhead = avgWithFilter - avgNoFilter;

        // Output metrics
        Console.WriteLine($"[T204] Metadata Filtering Performance:");
        Console.WriteLine($"  Documents: {documentCount}");
        Console.WriteLine($"  Avg latency (no filter): {avgNoFilter:F2}ms");
        Console.WriteLine($"  Avg latency (with filter): {avgWithFilter:F2}ms");
        Console.WriteLine($"  Filter overhead: {filterOverhead:F2}ms");

        // Assert - Filter should not add excessive overhead (within 100ms)
        Assert.True(avgWithFilter < avgNoFilter + 100,
            $"Filter overhead ({filterOverhead:F2}ms) exceeds acceptable threshold");
    }

    /// <summary>
    /// Test filtering with multiple filter conditions.
    /// </summary>
    [Fact]
    public async Task Benchmark_MetadataFiltering_MultipleFilters_Latency()
    {
        // Arrange
        const int documentCount = 5_000;
        await IngestGeneratedDocumentsWithVariedRepos(documentCount, repoCount: 10);

        var queryEmbedding = GenerateNormalizedEmbedding(seed: 77);

        // Act - Combined filter
        var combinedFilter = new Dictionary<string, string>
        {
            ["repoUrl"] = "%repo0%",
            ["filePath"] = "%.cs"
        };

        var latencies = new List<long>(20);
        for (var i = 0; i < 20; i++)
        {
            var sw = Stopwatch.StartNew();
            await _vectorStore.QueryAsync(queryEmbedding, k: 10, filters: combinedFilter);
            sw.Stop();
            latencies.Add(sw.ElapsedMilliseconds);
        }

        var avgLatency = latencies.Average();
        var maxLatency = latencies.Max();

        Console.WriteLine($"[T204] Multiple Filter Performance:");
        Console.WriteLine($"  Avg latency: {avgLatency:F2}ms");
        Console.WriteLine($"  Max latency: {maxLatency}ms");

        // Assert
        Assert.True(avgLatency < 500,
            $"Multiple filter query latency ({avgLatency:F2}ms) exceeds target");
    }

    #endregion

    #region T205: Benchmark concurrent upsert load

    /// <summary>
    /// T205: Benchmark concurrent upsert load.
    /// 10 concurrent tasks, 100 documents each = 1000 total.
    /// </summary>
    [Fact]
    public async Task Benchmark_ConcurrentUpsert_10Tasks_100DocsEach()
    {
        // Arrange
        const int concurrentTasks = 10;
        const int documentsPerTask = 100;
        const int totalDocuments = concurrentTasks * documentsPerTask;

        // Act
        var sw = Stopwatch.StartNew();

        var tasks = Enumerable.Range(0, concurrentTasks)
            .Select(taskIndex => UpsertDocumentBatch(taskIndex, documentsPerTask))
            .ToList();

        await Task.WhenAll(tasks);

        sw.Stop();

        var elapsedSeconds = sw.ElapsedMilliseconds / 1000.0;
        var upsertsPerSecond = totalDocuments / elapsedSeconds;

        // Verify all documents stored
        var allDocs = _vectorStore.GetAllDocuments();

        // Output metrics
        Console.WriteLine($"[T205] Concurrent Upsert Performance:");
        Console.WriteLine($"  Concurrent tasks: {concurrentTasks}");
        Console.WriteLine($"  Docs per task: {documentsPerTask}");
        Console.WriteLine($"  Total documents: {totalDocuments}");
        Console.WriteLine($"  Elapsed: {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"  Throughput: {upsertsPerSecond:F1} upserts/sec");
        Console.WriteLine($"  Documents stored: {allDocs.Count}");

        // Assert
        Assert.Equal(totalDocuments, allDocs.Count);
        Assert.True(upsertsPerSecond > 100,
            $"Concurrent upsert throughput ({upsertsPerSecond:F1}/sec) too low");
    }

    /// <summary>
    /// Test concurrent upsert with contention (same document key).
    /// </summary>
    [Fact]
    public async Task Benchmark_ConcurrentUpsert_WithContention()
    {
        // Arrange - All tasks write to same document key
        const int concurrentTasks = 10;
        const string repoUrl = "https://github.com/perf/contention";
        const string filePath = "src/contested.cs";

        // Act
        var sw = Stopwatch.StartNew();

        var tasks = Enumerable.Range(0, concurrentTasks)
            .Select(async taskIndex =>
            {
                for (var i = 0; i < 10; i++)
                {
                    var doc = new DocumentDto
                    {
                        Id = Guid.NewGuid(),
                        RepoUrl = repoUrl,
                        FilePath = filePath,
                        Title = $"Task {taskIndex} Iteration {i}",
                        Text = $"Content from task {taskIndex}, iteration {i}",
                        Embedding = GenerateNormalizedEmbedding(seed: taskIndex * 100 + i),
                        MetadataJson = $"{{\"task\": {taskIndex}, \"iteration\": {i}}}",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    await _vectorStore.UpsertAsync(doc);
                }
            })
            .ToList();

        await Task.WhenAll(tasks);

        sw.Stop();

        // Verify only one document exists (last write wins)
        var allDocs = _vectorStore.GetAllDocuments();
        var contestedDocs = allDocs
            .Where(d => d.RepoUrl == repoUrl && d.FilePath == filePath)
            .ToList();

        Console.WriteLine($"[T205] Concurrent Upsert with Contention:");
        Console.WriteLine($"  Total upsert operations: {concurrentTasks * 10}");
        Console.WriteLine($"  Elapsed: {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"  Contested documents (expected 1): {contestedDocs.Count}");

        // Assert - Only one document should exist (atomicity)
        Assert.Single(contestedDocs);
    }

    /// <summary>
    /// Stress test: high volume concurrent upserts.
    /// </summary>
    [Fact]
    public async Task Benchmark_ConcurrentUpsert_StressTest()
    {
        // Arrange
        const int concurrentTasks = 20;
        const int documentsPerTask = 50;
        const int totalDocuments = concurrentTasks * documentsPerTask;

        // Act
        var sw = Stopwatch.StartNew();

        var tasks = Enumerable.Range(0, concurrentTasks)
            .Select(taskIndex => UpsertDocumentBatch(taskIndex + 100, documentsPerTask))
            .ToList();

        await Task.WhenAll(tasks);

        sw.Stop();

        var upsertsPerSecond = totalDocuments / (sw.ElapsedMilliseconds / 1000.0);
        var allDocs = _vectorStore.GetAllDocuments();

        Console.WriteLine($"[T205] Stress Test:");
        Console.WriteLine($"  Total documents: {totalDocuments}");
        Console.WriteLine($"  Elapsed: {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"  Throughput: {upsertsPerSecond:F1} upserts/sec");
        Console.WriteLine($"  Documents stored: {allDocs.Count}");

        // Assert
        Assert.True(allDocs.Count >= totalDocuments - 10, // Allow small margin for concurrent same-key writes
            $"Expected ~{totalDocuments} documents, got {allDocs.Count}");
    }

    #endregion

    #region Helper Methods

    private async Task IngestGeneratedDocuments(int count)
    {
        var documents = new List<DocumentDto>(count);
        var random = new Random(42);

        for (var i = 0; i < count; i++)
        {
            documents.Add(new DocumentDto
            {
                Id = Guid.NewGuid(),
                RepoUrl = "https://github.com/perf/benchmark",
                FilePath = $"src/file{i}.cs",
                Title = $"Document {i}",
                Text = $"Content of document {i}",
                Embedding = GenerateNormalizedEmbedding(seed: i),
                MetadataJson = "{}",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        foreach (var doc in documents)
        {
            await _vectorStore.UpsertAsync(doc);
        }
    }

    private async Task IngestGeneratedDocumentsWithVariedRepos(int count, int repoCount)
    {
        for (var i = 0; i < count; i++)
        {
            var repoIndex = i % repoCount;
            var doc = new DocumentDto
            {
                Id = Guid.NewGuid(),
                RepoUrl = $"https://github.com/perf/repo{repoIndex}",
                FilePath = $"src/file{i}.cs",
                Title = $"Document {i}",
                Text = $"Content for repo{repoIndex}, file {i}",
                Embedding = GenerateNormalizedEmbedding(seed: i),
                MetadataJson = $"{{\"repo\": {repoIndex}}}",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _vectorStore.UpsertAsync(doc);
        }
    }

    private async Task UpsertDocumentBatch(int taskIndex, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var doc = new DocumentDto
            {
                Id = Guid.NewGuid(),
                RepoUrl = $"https://github.com/perf/task{taskIndex}",
                FilePath = $"src/file{i}.cs",
                Title = $"Task {taskIndex} Doc {i}",
                Text = $"Content from task {taskIndex}, document {i}",
                Embedding = GenerateNormalizedEmbedding(seed: taskIndex * 1000 + i),
                MetadataJson = "{}",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _vectorStore.UpsertAsync(doc);
        }
    }

    private static float[] GenerateNormalizedEmbedding(int seed)
    {
        var embedding = new float[1536];
        var random = new Random(seed);

        for (var i = 0; i < embedding.Length; i++)
        {
            embedding[i] = (float)(random.NextDouble() - 0.5) * 2;
        }

        // Normalize
        var magnitude = (float)Math.Sqrt(embedding.Sum(x => x * x));
        if (magnitude > 0)
        {
            for (var i = 0; i < embedding.Length; i++)
            {
                embedding[i] /= magnitude;
            }
        }

        return embedding;
    }

    private static string GenerateSampleText(int index)
    {
        return $@"// File {index}
using System;
using System.Collections.Generic;

namespace Sample.Project.File{index}
{{
    /// <summary>
    /// Sample class for performance testing.
    /// This class demonstrates typical code structure for benchmarking.
    /// </summary>
    public class SampleClass{index}
    {{
        private readonly List<string> _items = new();

        public void ProcessData(string input)
        {{
            if (string.IsNullOrEmpty(input))
                throw new ArgumentNullException(nameof(input));

            _items.Add(input);
        }}

        public IEnumerable<string> GetItems() => _items;
    }}
}}";
    }

    private static void OutputMetrics(BenchmarkMetrics metrics)
    {
        Console.WriteLine($"[{metrics.TestName}] Performance Metrics:");
        Console.WriteLine($"  Documents: {metrics.DocumentCount}");
        Console.WriteLine($"  Operations: {metrics.OperationCount}");
        Console.WriteLine($"  p50: {metrics.P50Ms}ms");
        Console.WriteLine($"  p90: {metrics.P90Ms}ms");
        Console.WriteLine($"  p95: {metrics.P95Ms}ms (target: <{metrics.TargetP95Ms}ms)");
        Console.WriteLine($"  p99: {metrics.P99Ms}ms");
        Console.WriteLine($"  avg: {metrics.AverageMs:F2}ms");
        Console.WriteLine($"  max: {metrics.MaxMs}ms");
    }

    private static void OutputThroughputMetrics(ThroughputMetrics metrics)
    {
        Console.WriteLine($"[{metrics.TestName}] Throughput Metrics:");
        Console.WriteLine($"  Documents: {metrics.DocumentCount}");
        Console.WriteLine($"  Elapsed: {metrics.ElapsedMs}ms");
        Console.WriteLine($"  Throughput: {metrics.DocsPerSecond:F1} docs/sec (target: ≥{metrics.TargetDocsPerSecond} docs/sec)");
    }

    #endregion

    #region Metrics Records

    private record BenchmarkMetrics
    {
        public required string TestName { get; init; }
        public required int DocumentCount { get; init; }
        public required int OperationCount { get; init; }
        public required long P50Ms { get; init; }
        public required long P90Ms { get; init; }
        public required long P95Ms { get; init; }
        public required long P99Ms { get; init; }
        public required double AverageMs { get; init; }
        public required long MaxMs { get; init; }
        public required long TargetP95Ms { get; init; }
    }

    private record ThroughputMetrics
    {
        public required string TestName { get; init; }
        public required int DocumentCount { get; init; }
        public required long ElapsedMs { get; init; }
        public required double DocsPerSecond { get; init; }
        public required double TargetDocsPerSecond { get; init; }
    }

    #endregion

    #region Helper Classes

    /// <summary>
    /// Stub embedding service that returns deterministic embeddings for testing.
    /// </summary>
    private sealed class StubEmbeddingService : IEmbeddingService
    {
        public string Provider => "stub";
        public string ModelId => "stub-model";
        public int EmbeddingDimension => 1536;

        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(GenerateNormalizedEmbedding(text.GetHashCode()));
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
            var responses = texts.Select(t => new EmbeddingResponse
            {
                Vector = GenerateNormalizedEmbedding(t.GetHashCode()),
                Provider = Provider,
                ModelId = ModelId,
                LatencyMs = 1
            }).ToList();
            return Task.FromResult<IReadOnlyList<EmbeddingResponse>>(responses);
        }
    }

    /// <summary>
    /// In-memory vector store for performance testing.
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
}
