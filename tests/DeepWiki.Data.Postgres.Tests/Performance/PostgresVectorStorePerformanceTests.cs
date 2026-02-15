using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DeepWiki.Data.Entities;
using DeepWiki.Data.Postgres.Tests.Fixtures;
using DeepWiki.Data.Postgres;
using DeepWiki.Data.Postgres.DbContexts;
using DeepWiki.Data.Postgres.Repositories;
using Xunit;

namespace DeepWiki.Data.Postgres.Tests.Performance;

[Trait("Category","Performance")]
public class PostgresVectorStorePerformanceTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private PostgresVectorDbContext? _context;
    private PostgresVectorStore? _vectorStore;

    public PostgresVectorStorePerformanceTests()
    {
        _fixture = new PostgresFixture();
    }

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _context = _fixture.CreateDbContext();
        _vectorStore = new PostgresVectorStore(_context);
    }

    public async Task DisposeAsync()
    {
        if (_context != null)
        {
            await _context.DisposeAsync();
        }

        await _fixture.DisposeAsync();
    }

    [Fact]
    public async Task QueryLatency_10kDocs_ShouldBeUnderThreshold()
    {
        // Arrange: generate 10k documents with deterministic embeddings
        const int docCount = 10_000;
        var docs = new List<DocumentEntity>(docCount);
        for (int i = 0; i < docCount; i++)
        {
            var emb = new float[1536];
            for (int j = 0; j < emb.Length; j++)
            {
                emb[j] = (i % 1000) * 0.0001f + (float)Math.Sin(j * 0.01f) * 0.00001f;
            }

            docs.Add(new DocumentEntity
            {
                Id = Guid.NewGuid(),
                RepoUrl = "https://github.com/perf/repo",
                FilePath = $"doc/{i}.md",
                Title = "Perf Doc",
                Text = "Perf generated content",
                Embedding = new ReadOnlyMemory<float>(emb),
                FileType = "md",
                IsCode = false,
                IsImplementation = false,
                TokenCount = 1,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                MetadataJson = "{}"
            });
        }

        await _vectorStore!.BulkUpsertAsync(docs, CancellationToken.None);

        // Act: query and measure latency
        var queryEmb = docs[docCount / 2].Embedding.GetValueOrDefault();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var results = await _vectorStore.QueryNearestAsync(queryEmb, 10, null, null, CancellationToken.None);
        sw.Stop();

        // Default threshold is 4000ms (relaxed for local CI/VM variance); override with VECTOR_STORE_LATENCY_MS env var for stricter runners (e.g., dedicated CI agents)
        var thresholdMsStr = Environment.GetEnvironmentVariable("VECTOR_STORE_LATENCY_MS");
        var thresholdMs = int.TryParse(thresholdMsStr, out var t) ? t : 4000;

        Assert.NotEmpty(results);
        Assert.True(sw.ElapsedMilliseconds <= thresholdMs, $"Query latency {sw.ElapsedMilliseconds}ms exceeded threshold {thresholdMs}ms");
    }
}
