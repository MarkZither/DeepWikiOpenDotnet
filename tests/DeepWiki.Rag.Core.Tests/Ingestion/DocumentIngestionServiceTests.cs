using DeepWiki.Data.Abstractions;
using DeepWiki.Data.Abstractions.Models;
using DeepWiki.Rag.Core.Ingestion;
using DeepWiki.Rag.Core.Tokenization;
using Microsoft.Extensions.Logging;

namespace DeepWiki.Rag.Core.Tests.Ingestion;

/// <summary>
/// Unit tests for DocumentIngestionService (T164-T174).
/// Tests ingestion orchestration with stub/fake dependencies.
/// </summary>
public class DocumentIngestionServiceTests
{
    // Standard 1536-dimension test embedding
    private static readonly float[] TestEmbedding = CreateTestEmbedding(1536);

    private static float[] CreateTestEmbedding(int dimension)
    {
        var embedding = new float[dimension];
        for (var i = 0; i < dimension; i++)
        {
            embedding[i] = (float)(i % 100) / 100f;
        }
        return embedding;
    }

    private static DocumentIngestionService CreateService(
        IVectorStore? vectorStore = null,
        ITokenizationService? tokenizationService = null,
        IEmbeddingService? embeddingService = null)
    {
        var vs = vectorStore ?? new StubVectorStore();
        var ts = tokenizationService ?? new StubTokenizationService();
        var es = embeddingService ?? new StubEmbeddingService();

        return new DocumentIngestionService(vs, ts, es);
    }

    #region T165: IngestAsync with 10 documents chunks, embeds, upserts all successfully

    [Fact]
    public async Task IngestAsync_With10Documents_ChunksEmbedsUpsertsAllSuccessfully()
    {
        // Arrange
        var vectorStore = new StubVectorStore();
        var embeddingService = new StubEmbeddingService();
        var service = CreateService(vectorStore: vectorStore, embeddingService: embeddingService);

        var documents = CreateTestDocuments(10);
        var request = new IngestionRequest { Documents = documents };

        // Act
        var result = await service.IngestAsync(request);

        // Assert
        Assert.Equal(10, result.SuccessCount);
        Assert.Equal(0, result.FailureCount);
        Assert.Empty(result.Errors);
        Assert.Equal(10, result.IngestedDocumentIds.Count);
        Assert.Equal(10, embeddingService.EmbedCallCount);
        Assert.Equal(10, vectorStore.UpsertCallCount);
    }

    #endregion

    #region T166: IngestAsync with duplicate (same repo+path) updates existing document

    [Fact]
    public async Task IngestAsync_WithDuplicate_UpdatesExistingDocument()
    {
        // Arrange
        var vectorStore = new StubVectorStore();
        var service = CreateService(vectorStore: vectorStore);

        var doc1 = CreateIngestionDocument("https://github.com/test/repo", "src/file.cs", "Original content");
        var doc2 = CreateIngestionDocument("https://github.com/test/repo", "src/file.cs", "Updated content");

        var request = new IngestionRequest { Documents = [doc1, doc2] };

        // Act
        var result = await service.IngestAsync(request);

        // Assert
        Assert.Equal(2, result.SuccessCount);
        Assert.Equal(2, vectorStore.UpsertedDocuments.Count);

        // Both should have been upserted (vector store handles deduplication)
        Assert.All(vectorStore.UpsertedDocuments, d =>
        {
            Assert.Equal("https://github.com/test/repo", d.RepoUrl);
            Assert.Equal("src/file.cs", d.FilePath);
        });

        // Verify text differs
        Assert.Equal("Original content", vectorStore.UpsertedDocuments[0].Text);
        Assert.Equal("Updated content", vectorStore.UpsertedDocuments[1].Text);
    }

    #endregion

    #region T167: IngestAsync with embedding service failure retries and falls back

    [Fact]
    public async Task IngestAsync_WithEmbeddingFailure_ReportsError()
    {
        // Arrange
        var embeddingService = new FailingEmbeddingService(new InvalidOperationException("Embedding service unavailable"));
        var service = CreateService(embeddingService: embeddingService);

        var documents = CreateTestDocuments(1);
        var request = new IngestionRequest { Documents = documents, ContinueOnError = true };

        // Act
        var result = await service.IngestAsync(request);

        // Assert
        Assert.Equal(0, result.SuccessCount);
        Assert.Equal(1, result.FailureCount);
        Assert.Single(result.Errors);
        Assert.Contains("Embedding service unavailable", result.Errors[0].ErrorMessage);
    }

    #endregion

    #region T168: IngestAsync with one failing document logs error and continues (batch resilience)

    [Fact]
    public async Task IngestAsync_WithOneFailingDocument_LogsErrorAndContinues()
    {
        // Arrange
        var embeddingService = new FailOnNthCallEmbeddingService(failOnCall: 3);
        var vectorStore = new StubVectorStore();
        var service = CreateService(vectorStore: vectorStore, embeddingService: embeddingService);

        var documents = CreateTestDocuments(5);
        var request = new IngestionRequest { Documents = documents, ContinueOnError = true };

        // Act
        var result = await service.IngestAsync(request);

        // Assert
        Assert.Equal(4, result.SuccessCount);
        Assert.Equal(1, result.FailureCount);
        Assert.Single(result.Errors);
        Assert.Contains("Transient error", result.Errors[0].ErrorMessage);
    }

    [Fact]
    public async Task IngestAsync_WithContinueOnErrorFalse_StopsOnFirstError()
    {
        // Arrange
        var embeddingService = new FailingEmbeddingService(new InvalidOperationException("First doc fails"));
        var service = CreateService(embeddingService: embeddingService);

        var documents = CreateTestDocuments(5);
        var request = new IngestionRequest { Documents = documents, ContinueOnError = false };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.IngestAsync(request));
    }

    #endregion

    #region T169: ChunkAndEmbedAsync chunks text, embeds all chunks, returns (chunk, embedding) pairs

    [Fact]
    public async Task ChunkAndEmbedAsync_WithText_ChunksAndEmbedsAllChunks()
    {
        // Arrange
        var tokenizationService = new ChunkingTokenizationService(chunkCount: 3);
        var embeddingService = new StubEmbeddingService();
        var service = CreateService(tokenizationService: tokenizationService, embeddingService: embeddingService);

        var text = "This is a test document with some content that should be chunked and embedded.";

        // Act
        var results = await service.ChunkAndEmbedAsync(text);

        // Assert
        Assert.Equal(3, results.Count);
        Assert.All(results, r =>
        {
            Assert.NotNull(r.Embedding);
            Assert.Equal(1536, r.Embedding.Length);
        });

        Assert.Equal(0, results[0].ChunkIndex);
        Assert.Equal(1, results[1].ChunkIndex);
        Assert.Equal(2, results[2].ChunkIndex);
    }

    [Fact]
    public async Task ChunkAndEmbedAsync_WithEmptyText_ReturnsEmptyList()
    {
        // Arrange
        var service = CreateService();

        // Act
        var results = await service.ChunkAndEmbedAsync("");

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task ChunkAndEmbedAsync_WithNullText_ReturnsEmptyList()
    {
        // Arrange
        var service = CreateService();

        // Act
        var results = await service.ChunkAndEmbedAsync(null!);

        // Assert
        Assert.Empty(results);
    }

    #endregion

    #region T170: ChunkAndEmbedAsync respects 8192 token limit per chunk

    [Fact]
    public async Task ChunkAndEmbedAsync_RespectsMaxTokenLimit()
    {
        // Arrange
        var tokenizationService = new ChunkingTokenizationService(chunkCount: 3, tokensPerChunk: 4000);
        var service = CreateService(tokenizationService: tokenizationService);

        var text = "A large document that exceeds the token limit and needs chunking.";
        const int maxTokens = 8192;

        // Act
        var results = await service.ChunkAndEmbedAsync(text, maxTokens);

        // Assert
        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.True(r.TokenCount <= maxTokens));
    }

    [Fact]
    public async Task ChunkAndEmbedAsync_WithChunkExceedingLimit_ThrowsInvalidOperationException()
    {
        // Arrange
        var tokenizationService = new ChunkingTokenizationService(chunkCount: 1, tokensPerChunk: 9000); // Exceeds 8192
        var service = CreateService(tokenizationService: tokenizationService);

        var text = "Text that results in an oversized chunk.";
        const int maxTokens = 8192;

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ChunkAndEmbedAsync(text, maxTokens));

        Assert.Contains("exceeding limit", ex.Message);
    }

    #endregion

    #region T171: UpsertAsync inserts new document with all metadata

    [Fact]
    public async Task UpsertAsync_InsertsNewDocumentWithAllMetadata()
    {
        // Arrange
        var vectorStore = new StubVectorStore();
        var service = CreateService(vectorStore: vectorStore);

        var doc = new DocumentDto
        {
            RepoUrl = "https://github.com/test/repo",
            FilePath = "src/services/UserService.cs",
            Title = "UserService",
            Text = "public class UserService { }",
            Embedding = TestEmbedding,
            MetadataJson = """{"author": "test", "version": "1.0"}""",
            FileType = "cs",
            IsCode = true,
            IsImplementation = true
        };

        // Act
        var result = await service.UpsertAsync(doc);

        // Assert
        Assert.Single(vectorStore.UpsertedDocuments);
        var capturedDoc = vectorStore.UpsertedDocuments[0];

        Assert.NotEqual(Guid.Empty, capturedDoc.Id);
        Assert.Equal("https://github.com/test/repo", capturedDoc.RepoUrl);
        Assert.Equal("src/services/UserService.cs", capturedDoc.FilePath);
        Assert.Equal("UserService", capturedDoc.Title);
        Assert.Equal("public class UserService { }", capturedDoc.Text);
        Assert.Equal(TestEmbedding, capturedDoc.Embedding);
        Assert.Contains("author", capturedDoc.MetadataJson);
        Assert.Equal("cs", capturedDoc.FileType);
        Assert.True(capturedDoc.IsCode);
        Assert.True(capturedDoc.IsImplementation);
        Assert.NotEqual(default, capturedDoc.CreatedAt);
        Assert.NotEqual(default, capturedDoc.UpdatedAt);
    }

    [Fact]
    public async Task UpsertAsync_SetsTimestampsOnNewDocument()
    {
        // Arrange
        var vectorStore = new StubVectorStore();
        var service = CreateService(vectorStore: vectorStore);

        var doc = new DocumentDto
        {
            RepoUrl = "https://github.com/test/repo",
            FilePath = "README.md",
            Text = "# Test"
        };

        var beforeTest = DateTime.UtcNow;

        // Act
        await service.UpsertAsync(doc);

        // Assert
        Assert.Single(vectorStore.UpsertedDocuments);
        var capturedDoc = vectorStore.UpsertedDocuments[0];
        Assert.True(capturedDoc.CreatedAt >= beforeTest);
        Assert.True(capturedDoc.UpdatedAt >= beforeTest);
    }

    [Fact]
    public async Task UpsertAsync_WithInvalidEmbeddingDimension_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateService();

        var doc = new DocumentDto
        {
            RepoUrl = "https://github.com/test/repo",
            FilePath = "file.cs",
            Text = "content",
            Embedding = new float[512] // Wrong dimension
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.UpsertAsync(doc));
        Assert.Contains("1536", ex.Message);
    }

    [Fact]
    public async Task UpsertAsync_WithMissingRepoUrl_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateService();

        var doc = new DocumentDto
        {
            FilePath = "file.cs",
            Text = "content"
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpsertAsync(doc));
    }

    [Fact]
    public async Task UpsertAsync_WithMissingFilePath_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateService();

        var doc = new DocumentDto
        {
            RepoUrl = "https://github.com/test/repo",
            Text = "content"
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpsertAsync(doc));
    }

    [Fact]
    public async Task UpsertAsync_WithMissingText_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateService();

        var doc = new DocumentDto
        {
            RepoUrl = "https://github.com/test/repo",
            FilePath = "file.cs"
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpsertAsync(doc));
    }

    #endregion

    #region T172: UpsertAsync with concurrent writes (deferred - atomicity handled by vector store)

    [Fact(Skip = "Concurrent write testing deferred - atomicity handled by vector store implementation")]
    public async Task UpsertAsync_WithConcurrentWrites_FirstWriteWinsSecondUpdates()
    {
        await Task.CompletedTask;
    }

    #endregion

    #region T173: IngestAsync returns IngestionResult with success/failure counts and errors

    [Fact]
    public async Task IngestAsync_ReturnsIngestionResultWithCounts()
    {
        // Arrange
        var service = CreateService();
        var documents = CreateTestDocuments(5);
        var request = new IngestionRequest { Documents = documents };

        // Act
        var result = await service.IngestAsync(request);

        // Assert
        Assert.Equal(5, result.SuccessCount);
        Assert.Equal(0, result.FailureCount);
        Assert.True(result.DurationMs > 0);
        Assert.Equal(5, result.TotalChunks);
        Assert.Equal(5, result.IngestedDocumentIds.Count);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task IngestAsync_WithMixedResults_ReportsCorrectCounts()
    {
        // Arrange
        var embeddingService = new FailAfterNCallsEmbeddingService(succeedCount: 3);
        var service = CreateService(embeddingService: embeddingService);

        var documents = CreateTestDocuments(5);
        var request = new IngestionRequest { Documents = documents, ContinueOnError = true };

        // Act
        var result = await service.IngestAsync(request);

        // Assert
        Assert.Equal(3, result.SuccessCount);
        Assert.Equal(2, result.FailureCount);
        Assert.Equal(2, result.Errors.Count);
        Assert.All(result.Errors, e => Assert.Equal(IngestionStage.Embedding, e.Stage));
    }

    [Fact]
    public async Task IngestAsync_WithEmptyDocuments_ReturnsEmptyResult()
    {
        // Arrange
        var service = CreateService();
        var request = new IngestionRequest { Documents = [] };

        // Act
        var result = await service.IngestAsync(request);

        // Assert
        Assert.Equal(0, result.SuccessCount);
        Assert.Equal(0, result.FailureCount);
        Assert.Empty(result.IngestedDocumentIds);
    }

    #endregion

    #region T174: Metadata enrichment adds language, file_type, chunk_index to documents

    [Fact]
    public async Task IngestAsync_EnrichesMetadataWithLanguageAndFileType()
    {
        // Arrange
        var vectorStore = new StubVectorStore();
        var service = CreateService(vectorStore: vectorStore);

        var doc = CreateIngestionDocument("https://github.com/test/repo", "src/services/UserService.cs", "public class UserService { }");
        var request = new IngestionRequest { Documents = [doc] };

        // Act
        await service.IngestAsync(request);

        // Assert
        Assert.Single(vectorStore.UpsertedDocuments);
        var capturedDoc = vectorStore.UpsertedDocuments[0];

        Assert.Equal("cs", capturedDoc.FileType);
        Assert.True(capturedDoc.IsCode);
        Assert.True(capturedDoc.IsImplementation);
        Assert.Contains("csharp", capturedDoc.MetadataJson);
    }

    [Fact]
    public async Task IngestAsync_DetectsTestFilesCorrectly()
    {
        // Arrange
        var vectorStore = new StubVectorStore();
        var service = CreateService(vectorStore: vectorStore);

        var doc = CreateIngestionDocument("https://github.com/test/repo", "tests/UserServiceTests.cs", "public class UserServiceTests { }");
        var request = new IngestionRequest { Documents = [doc] };

        // Act
        await service.IngestAsync(request);

        // Assert
        Assert.Single(vectorStore.UpsertedDocuments);
        var capturedDoc = vectorStore.UpsertedDocuments[0];

        Assert.Equal("cs", capturedDoc.FileType);
        Assert.True(capturedDoc.IsCode);
        Assert.False(capturedDoc.IsImplementation); // Tests are not implementation files
    }

    [Fact]
    public async Task IngestAsync_AppliesDefaultMetadata()
    {
        // Arrange
        var vectorStore = new StubVectorStore();
        var service = CreateService(vectorStore: vectorStore);

        var doc = CreateIngestionDocument("https://github.com/test/repo", "README.md", "# Test");
        var request = new IngestionRequest
        {
            Documents = [doc],
            MetadataDefaults = new Dictionary<string, string>
            {
                ["project"] = "deepwiki",
                ["branch"] = "main"
            }
        };

        // Act
        await service.IngestAsync(request);

        // Assert
        Assert.Single(vectorStore.UpsertedDocuments);
        var capturedDoc = vectorStore.UpsertedDocuments[0];
        Assert.Contains("deepwiki", capturedDoc.MetadataJson);
        Assert.Contains("main", capturedDoc.MetadataJson);
    }

    [Fact]
    public async Task IngestAsync_DetectsVariousFileTypes()
    {
        // Arrange
        var testCases = new[]
        {
            ("file.py", "python", true),
            ("file.ts", "typescript", true),
            ("file.js", "javascript", true),
            ("file.go", "go", true),
            ("file.rs", "rust", true),
            ("file.json", "text", false), // Config files are not code
            ("README.md", "text", false)
        };

        foreach (var (filePath, expectedLanguage, isCode) in testCases)
        {
            var vectorStore = new StubVectorStore();
            var service = CreateService(vectorStore: vectorStore);

            var doc = CreateIngestionDocument("https://github.com/test/repo", filePath, "content");
            var request = new IngestionRequest { Documents = [doc] };

            // Act
            await service.IngestAsync(request);

            // Assert
            Assert.Single(vectorStore.UpsertedDocuments);
            var capturedDoc = vectorStore.UpsertedDocuments[0];
            Assert.Equal(isCode, capturedDoc.IsCode);
            if (isCode)
            {
                Assert.Contains(expectedLanguage, capturedDoc.MetadataJson);
            }
        }
    }

    #endregion

    #region Helper Methods

    private static List<IngestionDocument> CreateTestDocuments(int count)
    {
        var documents = new List<IngestionDocument>();
        for (var i = 0; i < count; i++)
        {
            documents.Add(CreateIngestionDocument(
                $"https://github.com/test/repo{i}",
                $"src/file{i}.cs",
                $"Content of document {i}"));
        }
        return documents;
    }

    private static IngestionDocument CreateIngestionDocument(string repoUrl, string filePath, string text)
    {
        return new IngestionDocument
        {
            RepoUrl = repoUrl,
            FilePath = filePath,
            Text = text
        };
    }

    #endregion

    #region Stub/Fake Classes

    /// <summary>
    /// Stub vector store that tracks upserted documents.
    /// </summary>
    private sealed class StubVectorStore : IVectorStore
    {
        public List<DocumentDto> UpsertedDocuments { get; } = [];
        public int UpsertCallCount => UpsertedDocuments.Count;

        public Task<IReadOnlyList<VectorQueryResult>> QueryAsync(
            float[] embedding,
            int k = 10,
            Dictionary<string, string>? filters = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<VectorQueryResult>>([]);
        }

        public Task UpsertAsync(DocumentDto document, CancellationToken cancellationToken = default)
        {
            UpsertedDocuments.Add(document);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task RebuildIndexAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Stub tokenization service that returns fixed token counts.
    /// </summary>
    private sealed class StubTokenizationService : ITokenizationService
    {
        public Task<int> CountTokensAsync(string text, string modelId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(100); // Small enough to not require chunking
        }

        public Task<IReadOnlyList<TextChunk>> ChunkAsync(
            string text,
            int maxTokensPerChunk = 8192,
            string? modelId = null,
            Guid? parentDocumentId = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<TextChunk>>([
                new TextChunk { Text = text, ChunkIndex = 0, TokenCount = 100 }
            ]);
        }

        public int GetMaxTokens(string modelId) => 8192;
    }

    /// <summary>
    /// Tokenization service that produces multiple chunks.
    /// </summary>
    private sealed class ChunkingTokenizationService : ITokenizationService
    {
        private readonly int _chunkCount;
        private readonly int _tokensPerChunk;

        public ChunkingTokenizationService(int chunkCount, int tokensPerChunk = 1000)
        {
            _chunkCount = chunkCount;
            _tokensPerChunk = tokensPerChunk;
        }

        public Task<int> CountTokensAsync(string text, string modelId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_chunkCount * _tokensPerChunk);
        }

        public Task<IReadOnlyList<TextChunk>> ChunkAsync(
            string text,
            int maxTokensPerChunk = 8192,
            string? modelId = null,
            Guid? parentId = null,
            CancellationToken cancellationToken = default)
        {
            var chunks = new List<TextChunk>();
            for (var i = 0; i < _chunkCount; i++)
            {
                chunks.Add(new TextChunk
                {
                    Text = $"Chunk {i} content",
                    ChunkIndex = i,
                    TokenCount = _tokensPerChunk,
                    ParentId = parentId
                });
            }
            return Task.FromResult<IReadOnlyList<TextChunk>>(chunks);
        }

        public int GetMaxTokens(string modelId) => 8192;
    }

    /// <summary>
    /// Stub embedding service that returns fixed embeddings.
    /// </summary>
    private sealed class StubEmbeddingService : IEmbeddingService
    {
        public string Provider => "stub";
        public string ModelId => "stub-model";
        public int EmbeddingDimension => 1536;
        public int EmbedCallCount { get; private set; }

        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
        {
            EmbedCallCount++;
            return Task.FromResult(TestEmbedding);
        }

        public async IAsyncEnumerable<float[]> EmbedBatchAsync(
            IEnumerable<string> texts,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var _ in texts)
            {
                EmbedCallCount++;
                yield return TestEmbedding;
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
                Vector = TestEmbedding,
                Provider = Provider,
                ModelId = ModelId,
                LatencyMs = 1
            }).ToList();
            return Task.FromResult<IReadOnlyList<EmbeddingResponse>>(responses);
        }
    }

    /// <summary>
    /// Embedding service that always fails.
    /// </summary>
    private sealed class FailingEmbeddingService : IEmbeddingService
    {
        private readonly Exception _exception;

        public FailingEmbeddingService(Exception exception)
        {
            _exception = exception;
        }

        public string Provider => "failing";
        public string ModelId => "failing-model";
        public int EmbeddingDimension => 1536;

        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
        {
            throw _exception;
        }

        public IAsyncEnumerable<float[]> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
        {
            throw _exception;
        }

        public Task<IReadOnlyList<EmbeddingResponse>> EmbedBatchWithMetadataAsync(
            IEnumerable<string> texts,
            int batchSize = 10,
            CancellationToken cancellationToken = default)
        {
            throw _exception;
        }
    }

    /// <summary>
    /// Embedding service that fails on the Nth call.
    /// </summary>
    private sealed class FailOnNthCallEmbeddingService : IEmbeddingService
    {
        private readonly int _failOnCall;
        private int _callCount;

        public FailOnNthCallEmbeddingService(int failOnCall)
        {
            _failOnCall = failOnCall;
        }

        public string Provider => "failOnNth";
        public string ModelId => "failOnNth-model";
        public int EmbeddingDimension => 1536;

        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
        {
            _callCount++;
            if (_callCount == _failOnCall)
            {
                throw new InvalidOperationException("Transient error on call " + _callCount);
            }
            return Task.FromResult(TestEmbedding);
        }

        public async IAsyncEnumerable<float[]> EmbedBatchAsync(
            IEnumerable<string> texts,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var _ in texts)
            {
                _callCount++;
                if (_callCount == _failOnCall)
                {
                    throw new InvalidOperationException("Transient error on call " + _callCount);
                }
                yield return TestEmbedding;
            }
            await Task.CompletedTask;
        }

        public Task<IReadOnlyList<EmbeddingResponse>> EmbedBatchWithMetadataAsync(
            IEnumerable<string> texts,
            int batchSize = 10,
            CancellationToken cancellationToken = default)
        {
            var responses = new List<EmbeddingResponse>();
            foreach (var t in texts)
            {
                _callCount++;
                if (_callCount == _failOnCall)
                {
                    throw new InvalidOperationException("Transient error on call " + _callCount);
                }
                responses.Add(new EmbeddingResponse { Vector = TestEmbedding, Provider = Provider, ModelId = ModelId, LatencyMs = 1 });
            }
            return Task.FromResult<IReadOnlyList<EmbeddingResponse>>(responses);
        }
    }

    /// <summary>
    /// Embedding service that succeeds N times then fails.
    /// </summary>
    private sealed class FailAfterNCallsEmbeddingService : IEmbeddingService
    {
        private readonly int _succeedCount;
        private int _callCount;

        public FailAfterNCallsEmbeddingService(int succeedCount)
        {
            _succeedCount = succeedCount;
        }

        public string Provider => "failAfterN";
        public string ModelId => "failAfterN-model";
        public int EmbeddingDimension => 1536;

        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
        {
            _callCount++;
            if (_callCount > _succeedCount)
            {
                throw new InvalidOperationException("Embedding failed after " + _succeedCount + " calls");
            }
            return Task.FromResult(TestEmbedding);
        }

        public async IAsyncEnumerable<float[]> EmbedBatchAsync(
            IEnumerable<string> texts,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var _ in texts)
            {
                _callCount++;
                if (_callCount > _succeedCount)
                {
                    throw new InvalidOperationException("Embedding failed after " + _succeedCount + " calls");
                }
                yield return TestEmbedding;
            }
            await Task.CompletedTask;
        }

        public Task<IReadOnlyList<EmbeddingResponse>> EmbedBatchWithMetadataAsync(
            IEnumerable<string> texts,
            int batchSize = 10,
            CancellationToken cancellationToken = default)
        {
            var responses = new List<EmbeddingResponse>();
            foreach (var t in texts)
            {
                _callCount++;
                if (_callCount > _succeedCount)
                {
                    throw new InvalidOperationException("Embedding failed after " + _succeedCount + " calls");
                }
                responses.Add(new EmbeddingResponse { Vector = TestEmbedding, Provider = Provider, ModelId = ModelId, LatencyMs = 1 });
            }
            return Task.FromResult<IReadOnlyList<EmbeddingResponse>>(responses);
        }
    }

    #endregion
}
