using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DeepWiki.Data.Entities;
using DeepWiki.Data.Interfaces;
using DeepWiki.Data.SqlServer.VectorStore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DeepWiki.Data.SqlServer.Tests.VectorStore;

public class SqlServerVectorStoreAdapterUnitTests
{
    private class FakeProviderStore : IPersistenceVectorStore
    {
        public DeepWiki.Data.Entities.DocumentEntity? LastUpsert;
        public Guid? LastDeletedId;
        public ReadOnlyMemory<float> LastQueryEmbedding;
        public string? LastRepoFilter;
        public string? LastFilePathFilter;

        public Task BulkUpsertAsync(IEnumerable<DeepWiki.Data.Entities.DocumentEntity> documents, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<int> CountAsync(string? repoUrlFilter = null, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            LastDeletedId = id;
            return Task.CompletedTask;
        }

        public Task DeleteByRepoAsync(string repoUrl, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task DeleteChunksAsync(string repoUrl, string filePath, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<List<DeepWiki.Data.Entities.DocumentEntity>> QueryNearestAsync(ReadOnlyMemory<float> queryEmbedding, int k = 10, string? repoUrlFilter = null, string? filePathFilter = null, CancellationToken cancellationToken = default)
        {
            LastQueryEmbedding = queryEmbedding;
            LastRepoFilter = repoUrlFilter;
            LastFilePathFilter = filePathFilter;

            // Create three simple documents with embeddings designed for deterministic similarity with the query
            var d1 = new DeepWiki.Data.Entities.DocumentEntity { Id = Guid.NewGuid(), RepoUrl = "r", FilePath = "p/a.cs", Title = "A", Text = "a", Embedding = CreateEmbedding(1f) };
            var d2 = new DeepWiki.Data.Entities.DocumentEntity { Id = Guid.NewGuid(), RepoUrl = "r", FilePath = "p/b.cs", Title = "B", Text = "b", Embedding = CreateEmbedding(0.5f) };
            var d3 = new DeepWiki.Data.Entities.DocumentEntity { Id = Guid.NewGuid(), RepoUrl = "r", FilePath = "p/c.cs", Title = "C", Text = "c", Embedding = CreateEmbedding(0f) };

            var list = new List<DeepWiki.Data.Entities.DocumentEntity> { d1, d2, d3 };

            return Task.FromResult(list.Take(k).ToList());
        }

        public Task UpsertAsync(DeepWiki.Data.Entities.DocumentEntity document, CancellationToken cancellationToken = default)
        {
            LastUpsert = document;
            return Task.CompletedTask;
        }

        private static ReadOnlyMemory<float> CreateEmbedding(float v)
        {
            var arr = new float[1536];
            for (int i = 0; i < arr.Length; i++) arr[i] = v + (float)Math.Sin(i * 0.01f) * 0.001f;
            return new ReadOnlyMemory<float>(arr);
        }

        public Task RebuildIndexAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task QueryAsync_ReturnsKDocumentsRankedBySimilarity()
    {
        var fake = new FakeProviderStore();
        var adapter = new DeepWiki.Data.SqlServer.VectorStore.SqlServerVectorStoreAdapter(fake, NullLogger<DeepWiki.Data.SqlServer.VectorStore.SqlServerVectorStoreAdapter>.Instance);

        // Query embedding similar to the highest-value embedding (d1)
        var query = new float[1536];
        for (int i = 0; i < query.Length; i++) query[i] = 1.0f + (float)Math.Sin(i * 0.01f) * 0.001f;

        var results = await adapter.QueryAsync(query, 3, null);

        Assert.Equal(3, results.Count);
        // Ensure similarity scores are set and ordered descending
        var scores = results.Select(r => r.SimilarityScore).ToList();
        Assert.True(scores.SequenceEqual(scores.OrderByDescending(s => s)));
    }

    [Fact]
    public async Task QueryAsync_EmptyResult_ReturnsEmpty()
    {
        var fake = new FakeProviderStoreEmpty();
        var adapter = new DeepWiki.Data.SqlServer.VectorStore.SqlServerVectorStoreAdapter(fake, NullLogger<DeepWiki.Data.SqlServer.VectorStore.SqlServerVectorStoreAdapter>.Instance);

        var query = new float[1536];
        var results = await adapter.QueryAsync(query, 3, null);

        Assert.Empty(results);
    }

    private class FakeProviderStoreEmpty : IPersistenceVectorStore
    {
        public Task BulkUpsertAsync(IEnumerable<DocumentEntity> documents, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> CountAsync(string? repoUrlFilter = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteByRepoAsync(string repoUrl, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeleteChunksAsync(string repoUrl, string filePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<List<DocumentEntity>> QueryNearestAsync(ReadOnlyMemory<float> queryEmbedding, int k = 10, string? repoUrlFilter = null, string? filePathFilter = null, CancellationToken cancellationToken = default) => Task.FromResult(new List<DocumentEntity>());
        public Task UpsertAsync(DocumentEntity document, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RebuildIndexAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task QueryAsync_WithMetadataFilter_PassesFiltersToProvider()
    {
        var fake = new FakeProviderStore();
        var adapter = new DeepWiki.Data.SqlServer.VectorStore.SqlServerVectorStoreAdapter(fake, NullLogger<DeepWiki.Data.SqlServer.VectorStore.SqlServerVectorStoreAdapter>.Instance);

        var query = new float[1536];
        var filters = new Dictionary<string, string> { { "repoUrl", "https://github.com/org/%" }, { "filePath", "src/some/%" } };

        var results = await adapter.QueryAsync(query, 3, filters);

        Assert.NotNull(fake.LastRepoFilter);
        // Adapter passes filters through unchanged to provider
        Assert.Equal("https://github.com/org/%", fake.LastRepoFilter);
        Assert.Equal("src/some/%", fake.LastFilePathFilter);
    }

    [Fact]
    public async Task UpsertAsync_MapsAndCallsProvider()
    {
        var fake = new FakeProviderStore();
        var adapter = new DeepWiki.Data.SqlServer.VectorStore.SqlServerVectorStoreAdapter(fake, NullLogger<DeepWiki.Data.SqlServer.VectorStore.SqlServerVectorStoreAdapter>.Instance);

        var doc = new DeepWiki.Data.Abstractions.Models.DocumentDto
        {
            RepoUrl = "https://github.com/test/repo",
            FilePath = "file.cs",
            Title = "Title",
            Text = "Text",
            Embedding = new float[1536]
        };

        await adapter.UpsertAsync(doc);

        Assert.NotNull(fake.LastUpsert);
        Assert.Equal(doc.RepoUrl, fake.LastUpsert!.RepoUrl);
        Assert.Equal(doc.FilePath, fake.LastUpsert!.FilePath);
        Assert.Equal(1536, fake.LastUpsert!.Embedding?.Length);
    }

    [Fact]
    public async Task UpsertAsync_InvalidEmbedding_ThrowsArgumentException()
    {
        var fake = new FakeProviderStore();
        var adapter = new DeepWiki.Data.SqlServer.VectorStore.SqlServerVectorStoreAdapter(fake, NullLogger<DeepWiki.Data.SqlServer.VectorStore.SqlServerVectorStoreAdapter>.Instance);

        var doc = new DeepWiki.Data.Abstractions.Models.DocumentDto
        {
            RepoUrl = "https://github.com/test/repo",
            FilePath = "file.cs",
            Title = "Title",
            Text = "Text",
            Embedding = new float[10]
        };

        await Assert.ThrowsAsync<ArgumentException>(() => adapter.UpsertAsync(doc));
    }

    [Fact]
    public async Task DeleteAsync_CallsProvider()
    {
        var fake = new FakeProviderStore();
        var adapter = new DeepWiki.Data.SqlServer.VectorStore.SqlServerVectorStoreAdapter(fake, NullLogger<DeepWiki.Data.SqlServer.VectorStore.SqlServerVectorStoreAdapter>.Instance);

        var id = Guid.NewGuid();
        await adapter.DeleteAsync(id);

        Assert.Equal(id, fake.LastDeletedId);
    }

    [Fact]
    public async Task RebuildIndexAsync_CompletesWithoutError()
    {
        var fake = new FakeProviderStore();
        var adapter = new DeepWiki.Data.SqlServer.VectorStore.SqlServerVectorStoreAdapter(fake, NullLogger<DeepWiki.Data.SqlServer.VectorStore.SqlServerVectorStoreAdapter>.Instance);

        await adapter.RebuildIndexAsync();
    }

    [Fact]
    public async Task QueryAsync_InvalidEmbedding_ThrowsArgumentException()
    {
        var fake = new FakeProviderStore();
        var adapter = new DeepWiki.Data.SqlServer.VectorStore.SqlServerVectorStoreAdapter(fake, NullLogger<DeepWiki.Data.SqlServer.VectorStore.SqlServerVectorStoreAdapter>.Instance);

        var query = new float[10];
        await Assert.ThrowsAsync<ArgumentException>(() => adapter.QueryAsync(query, 3, null));
    }
}