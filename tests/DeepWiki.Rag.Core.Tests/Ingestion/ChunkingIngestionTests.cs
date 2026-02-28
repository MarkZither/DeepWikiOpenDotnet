using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DeepWiki.Data.Abstractions;
using DeepWiki.Data.Abstractions.Models;
using DeepWiki.Rag.Core.Ingestion;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DeepWiki.Rag.Core.Tests.Ingestion;

/// <summary>
/// Tests for sliding-window chunking behaviour in DocumentIngestionService (T083-T086).
///
/// These tests target the FUTURE refactored IngestAsync that:
///   - Calls ChunkAndEmbedAsync with ChunkOptions.ChunkSize
///   - Caps at MaxChunksPerFile (logs warning if capped)
///   - Calls IVectorStore.DeleteChunksAsync before upserting
///   - Upserts one DocumentDto per chunk with ChunkIndex + TotalChunks
///   - Counts success/failure per FILE (not per chunk)
///
/// Tests are expected to FAIL until T091 / T095 are implemented.
/// </summary>
public class ChunkingIngestionTests
{
    // 1536-dimension zero-filled embedding used by all stubs
    private static readonly float[] TestEmbedding = new float[1536];

    // =========================================================================
    // T083 — Large doc produces multiple upserts with sequential ChunkIndex
    // =========================================================================

    /// <summary>
    /// T083: A document whose tokeniser returns 10 000 tokens is fed to IngestAsync.
    /// With ChunkSize=512 the service must produce multiple chunks and upsert
    /// one DocumentDto per chunk.  Each upserted row must carry:
    ///   • sequential ChunkIndex (0, 1, 2 …)
    ///   • identical TotalChunks equal to the actual chunk count
    ///   • same RepoUrl + FilePath across all chunks
    /// </summary>
    [Fact]
    public async Task IngestAsync_LargeDocument_ProducesMultipleChunksWithSequentialChunkIndex()
    {
        // Arrange — tokeniser reports 10 000 tokens, ChunkSize=512 → ≥19 chunks
        const int totalTokens = 10_000;
        const int chunkSize = 512;
        int expectedChunkCount = (int)Math.Ceiling((double)totalTokens / chunkSize);

        var tokenizer = new FixedTokenCountTokenizer(totalTokens: totalTokens, chunkSize: chunkSize);
        var embeddingService = new StubEmbeddingService();
        var vectorStore = new ChunkAwareVectorStore();

        var chunkOptions = Options.Create(new ChunkOptions
        {
            ChunkSize = chunkSize,
            ChunkOverlap = 128,
            MaxChunksPerFile = 200
        });

        var service = CreateService(vectorStore, tokenizer, embeddingService, chunkOptions);

        var request = new IngestionRequest
        {
            Documents =
            [
                new IngestionDocument
                {
                    RepoUrl = "https://github.com/test/repo",
                    FilePath = "src/LargeFile.cs",
                    Text = new string('a', 100_000) // large text; token count is stubbed
                }
            ]
        };

        // Act
        var result = await service.IngestAsync(request);

        // Assert — success is counted per file (1 file → SuccessCount = 1)
        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(0, result.FailureCount);
        Assert.Empty(result.Errors);

        // Multiple rows must have been upserted
        var upserted = vectorStore.GetChunksFor("https://github.com/test/repo", "src/LargeFile.cs");
        Assert.Equal(expectedChunkCount, upserted.Count);

        // Verify sequential ChunkIndex
        for (int i = 0; i < upserted.Count; i++)
        {
            Assert.Equal(i, upserted[i].ChunkIndex);
        }

        // Verify all rows carry the same TotalChunks
        Assert.All(upserted, d => Assert.Equal(expectedChunkCount, d.TotalChunks));

        // Verify all rows share RepoUrl + FilePath
        Assert.All(upserted, d =>
        {
            Assert.Equal("https://github.com/test/repo", d.RepoUrl);
            Assert.Equal("src/LargeFile.cs", d.FilePath);
        });
    }

    // =========================================================================
    // T084 — Small doc (≤ ChunkSize tokens) produces exactly 1 chunk
    // =========================================================================

    /// <summary>
    /// T084: A document at or under ChunkSize tokens must result in exactly one
    /// upsert with ChunkIndex=0 and TotalChunks=1.
    /// </summary>
    [Fact]
    public async Task IngestAsync_SmallDocument_ProducesExactlyOneChunk()
    {
        // Arrange — tokeniser reports exactly ChunkSize tokens
        const int chunkSize = 512;

        var tokenizer = new FixedTokenCountTokenizer(totalTokens: chunkSize, chunkSize: chunkSize);
        var vectorStore = new ChunkAwareVectorStore();

        var chunkOptions = Options.Create(new ChunkOptions
        {
            ChunkSize = chunkSize,
            ChunkOverlap = 128,
            MaxChunksPerFile = 200
        });

        var service = CreateService(vectorStore, tokenizer, chunkOptions: chunkOptions);

        var request = new IngestionRequest
        {
            Documents =
            [
                new IngestionDocument
                {
                    RepoUrl = "https://github.com/test/repo",
                    FilePath = "src/SmallFile.cs",
                    Text = "small content"
                }
            ]
        };

        // Act
        var result = await service.IngestAsync(request);

        // Assert
        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(0, result.FailureCount);

        var upserted = vectorStore.GetChunksFor("https://github.com/test/repo", "src/SmallFile.cs");
        Assert.Single(upserted);
        Assert.Equal(0, upserted[0].ChunkIndex);
        Assert.Equal(1, upserted[0].TotalChunks);
    }

    // =========================================================================
    // T085 — Oversized doc is capped at MaxChunksPerFile; warning is logged;
    //         no exception; SuccessCount = 1 (per file)
    // =========================================================================

    /// <summary>
    /// T085: When a document would produce more chunks than MaxChunksPerFile the
    /// service must:
    ///   • cap the output at MaxChunksPerFile chunks
    ///   • log a warning (captured via a counting logger)
    ///   • NOT throw an exception
    ///   • return IngestionResult.SuccessCount = 1
    /// </summary>
    [Fact]
    public async Task IngestAsync_DocumentExceedingMaxChunksPerFile_IsCappedWithWarning()
    {
        // Arrange — 200 × 512 + 1 token forces capping
        const int chunkSize = 512;
        const int maxChunks = 200;
        int oversizedTokens = maxChunks * chunkSize + 1;

        var tokenizer = new FixedTokenCountTokenizer(totalTokens: oversizedTokens, chunkSize: chunkSize);
        var vectorStore = new ChunkAwareVectorStore();
        var warningLogger = new WarningCountingLogger<DocumentIngestionService>();

        var chunkOptions = Options.Create(new ChunkOptions
        {
            ChunkSize = chunkSize,
            ChunkOverlap = 128,
            MaxChunksPerFile = maxChunks
        });

        var service = new DocumentIngestionService(
            vectorStore,
            tokenizer,
            new StubEmbeddingService(),
            warningLogger,
            chunkOptions);

        var request = new IngestionRequest
        {
            Documents =
            [
                new IngestionDocument
                {
                    RepoUrl = "https://github.com/test/repo",
                    FilePath = "src/HugeFile.cs",
                    Text = new string('x', 200_000)
                }
            ]
        };

        // Act — must NOT throw
        var result = await service.IngestAsync(request);

        // Assert
        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(0, result.FailureCount);
        Assert.Empty(result.Errors);

        var upserted = vectorStore.GetChunksFor("https://github.com/test/repo", "src/HugeFile.cs");
        Assert.Equal(maxChunks, upserted.Count);

        // A warning must have been emitted about capping
        Assert.True(warningLogger.WarningCount > 0,
            "Expected at least one warning log about chunk cap being reached");
    }

    // =========================================================================
    // T086 — Re-ingestion calls DeleteChunksAsync; store ends with exactly the
    //         new chunk count (stale chunks removed)
    // =========================================================================

    /// <summary>
    /// T086: Re-ingesting a file that previously produced 5 chunks but now
    /// produces 3 must call IVectorStore.DeleteChunksAsync before upserting so
    /// that the store ends up with exactly 3 rows for that (RepoUrl, FilePath).
    /// </summary>
    [Fact]
    public async Task IngestAsync_ReIngest_CallsDeleteChunksBeforeUpsert_StoreHasExactlyNewChunkCount()
    {
        // Arrange — first ingest: 5 chunks; second ingest: 3 chunks
        const string repoUrl = "https://github.com/test/repo";
        const string filePath = "src/ChangedFile.cs";
        const int chunkSize = 512;

        var vectorStore = new ChunkAwareVectorStore();
        var chunkOptions = Options.Create(new ChunkOptions
        {
            ChunkSize = chunkSize,
            ChunkOverlap = 128,
            MaxChunksPerFile = 200
        });

        // First ingest — produces 5 chunks
        var tokenizer5 = new FixedTokenCountTokenizer(totalTokens: 5 * chunkSize, chunkSize: chunkSize);
        var service5 = CreateService(vectorStore, tokenizer5, chunkOptions: chunkOptions);

        await service5.IngestAsync(new IngestionRequest
        {
            Documents =
            [
                new IngestionDocument { RepoUrl = repoUrl, FilePath = filePath, Text = "version1" }
            ]
        });

        Assert.Equal(5, vectorStore.GetChunksFor(repoUrl, filePath).Count);

        // Second ingest — produces 3 chunks
        var tokenizer3 = new FixedTokenCountTokenizer(totalTokens: 3 * chunkSize, chunkSize: chunkSize);
        var service3 = CreateService(vectorStore, tokenizer3, chunkOptions: chunkOptions);

        var result = await service3.IngestAsync(new IngestionRequest
        {
            Documents =
            [
                new IngestionDocument { RepoUrl = repoUrl, FilePath = filePath, Text = "version2" }
            ]
        });

        // Assert — stale chunks removed, only 3 remain
        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(0, result.FailureCount);

        var finalChunks = vectorStore.GetChunksFor(repoUrl, filePath);
        Assert.Equal(3, finalChunks.Count);

        // DeleteChunksAsync must have been called at least once
        Assert.True(vectorStore.DeleteChunksCallCount > 0,
            "Expected DeleteChunksAsync to be called during re-ingestion");
    }

    // =========================================================================
    // Factory helpers
    // =========================================================================

    private static DocumentIngestionService CreateService(
        IVectorStore vectorStore,
        ITokenizationService? tokenizationService = null,
        IEmbeddingService? embeddingService = null,
        IOptions<ChunkOptions>? chunkOptions = null)
    {
        return new DocumentIngestionService(
            vectorStore,
            tokenizationService ?? new StubEmbeddingTokenizer(),
            embeddingService ?? new StubEmbeddingService(),
            NullLogger<DocumentIngestionService>.Instance,
            chunkOptions ?? Options.Create(new ChunkOptions()));
    }

    // =========================================================================
    // Stub / fake helpers
    // =========================================================================

    /// <summary>
    /// Tokenizer that simulates a document with a fixed total token count.
    /// ChunkAsync divides the text into ceil(totalTokens / chunkSize) equal chunks.
    /// </summary>
    private sealed class FixedTokenCountTokenizer : ITokenizationService
    {
        private readonly int _totalTokens;
        private readonly int _chunkSize;

        public FixedTokenCountTokenizer(int totalTokens, int chunkSize)
        {
            _totalTokens = totalTokens;
            _chunkSize = chunkSize;
        }

        public Task<int> CountTokensAsync(string text, string modelId, CancellationToken ct = default)
            => Task.FromResult(_totalTokens);

        public Task<IReadOnlyList<TextChunk>> ChunkAsync(
            string text,
            int maxTokens = 8192,
            string? modelId = null,
            Guid? parentId = null,
            CancellationToken ct = default)
        {
            int count = (int)Math.Ceiling((double)_totalTokens / _chunkSize);
            var chunks = Enumerable.Range(0, count).Select(i => new TextChunk
            {
                Text = $"chunk-{i}",
                ChunkIndex = i,
                TokenCount = Math.Min(_chunkSize, _totalTokens - i * _chunkSize),
                ParentId = parentId
            }).ToList();
            return Task.FromResult<IReadOnlyList<TextChunk>>(chunks);
        }

        public int GetMaxTokens(string modelId) => 8192;
    }

    /// <summary>
    /// Minimal tokenizer for non-chunking scenarios.
    /// </summary>
    private sealed class StubEmbeddingTokenizer : ITokenizationService
    {
        public Task<int> CountTokensAsync(string text, string modelId, CancellationToken ct = default)
            => Task.FromResult(100);

        public Task<IReadOnlyList<TextChunk>> ChunkAsync(
            string text, int maxTokens = 8192, string? modelId = null,
            Guid? parentId = null, CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<TextChunk>>([
                new TextChunk { Text = text, ChunkIndex = 0, TokenCount = 100 }
            ]);
        }

        public int GetMaxTokens(string modelId) => 8192;
    }

    private sealed class StubEmbeddingService : IEmbeddingService
    {
        public string Provider => "stub";
        public string ModelId => "stub-model";
        public int EmbeddingDimension => 1536;

        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
            => Task.FromResult(TestEmbedding);

        public async IAsyncEnumerable<float[]> EmbedBatchAsync(
            IEnumerable<string> texts,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var _ in texts)
            {
                yield return TestEmbedding;
                await Task.CompletedTask;
            }
        }

        public Task<IReadOnlyList<EmbeddingResponse>> EmbedBatchWithMetadataAsync(
            IEnumerable<string> texts, int batchSize = 10, CancellationToken ct = default)
        {
            var results = texts.Select(_ => new EmbeddingResponse
            {
                Vector = TestEmbedding,
                Provider = Provider,
                ModelId = ModelId,
                LatencyMs = 0
            }).ToList();
            return Task.FromResult<IReadOnlyList<EmbeddingResponse>>(results);
        }
    }

    /// <summary>
    /// In-memory vector store that is chunk-aware:
    ///   • UpsertAsync keys on (RepoUrl, FilePath, ChunkIndex) so chunks co-exist
    ///   • DeleteChunksAsync removes ALL rows for a (RepoUrl, FilePath) pair
    ///   • Exposes GetChunksFor() and DeleteChunksCallCount for assertions
    /// </summary>
    private sealed class ChunkAwareVectorStore : IVectorStore
    {
        private readonly List<DocumentDto> _docs = [];
        public int DeleteChunksCallCount { get; private set; }

        public List<DocumentDto> GetChunksFor(string repoUrl, string filePath)
            => _docs.Where(d => d.RepoUrl == repoUrl && d.FilePath == filePath)
                    .OrderBy(d => d.ChunkIndex)
                    .ToList();

        public Task UpsertAsync(DocumentDto document, CancellationToken ct = default)
        {
            var existing = _docs.FindIndex(d =>
                d.RepoUrl == document.RepoUrl &&
                d.FilePath == document.FilePath &&
                d.ChunkIndex == document.ChunkIndex);

            if (existing >= 0)
                _docs[existing] = document;
            else
                _docs.Add(document);

            return Task.CompletedTask;
        }

        /// <summary>
        /// DeleteChunksAsync is the new method required by T093. It will be added
        /// to IVectorStore as part of US6 implementation. For now we expose it
        /// directly so the tests can compile and express the expected contract.
        /// </summary>
        public Task DeleteChunksAsync(string repoUrl, string filePath, CancellationToken ct = default)
        {
            DeleteChunksCallCount++;
            _docs.RemoveAll(d => d.RepoUrl == repoUrl && d.FilePath == filePath);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<VectorQueryResult>> QueryAsync(
            float[] embedding, int k, Dictionary<string, string>? filters = null,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<VectorQueryResult>>([]);

        public Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            _docs.RemoveAll(d => d.Id == id);
            return Task.CompletedTask;
        }

        public Task RebuildIndexAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>
    /// ILogger implementation that counts Warning-level calls.
    /// </summary>
    private sealed class WarningCountingLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public int WarningCount { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == Microsoft.Extensions.Logging.LogLevel.Warning)
                WarningCount++;
        }
    }
}
