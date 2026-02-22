using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DeepWiki.Data.Entities;
using DeepWiki.Data.Interfaces;
using DeepWiki.Data.SqlServer.VectorStore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DeepWiki.Data.SqlServer.Tests.VectorStore;

public class SqlServerVectorStoreAdapterTests
{
    private class FakeProviderStore : IPersistenceVectorStore
    {
        public DeepWiki.Data.Entities.DocumentEntity? LastUpsert;
        public ReadOnlyMemory<float> LastQueryEmbedding;

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
            LastUpsert = null;
            return Task.CompletedTask;
        }

        public Task DeleteByRepoAsync(string repoUrl, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task DeleteChunksAsync(string repoUrl, string filePath, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RebuildIndexAsync(CancellationToken cancellationToken = default)
        {
            // Test double: no-op implementation
            return Task.CompletedTask;
        }

        public Task<List<DeepWiki.Data.Entities.DocumentEntity>> QueryNearestAsync(ReadOnlyMemory<float> queryEmbedding, int k = 10, string? repoUrlFilter = null, string? filePathFilter = null, CancellationToken cancellationToken = default)
        {
            LastQueryEmbedding = queryEmbedding;
            var doc = new DeepWiki.Data.Entities.DocumentEntity
            {
                Id = Guid.NewGuid(),
                RepoUrl = "https://github.com/test/repo",
                FilePath = "file.cs",
                Text = "test",
                Embedding = queryEmbedding
            };
            return Task.FromResult(new List<DeepWiki.Data.Entities.DocumentEntity> { doc });
        }

        public Task UpsertAsync(DeepWiki.Data.Entities.DocumentEntity document, CancellationToken cancellationToken = default)
        {
            LastUpsert = document;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task UpsertAsync_MapsAndCallsProvider()
    {
        var fake = new FakeProviderStore();
        var adapter = new SqlServerVectorStoreAdapter(fake, NullLogger<SqlServerVectorStoreAdapter>.Instance);

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
    public async Task QueryAsync_CallsProviderAndReturnsMappedResult()
    {
        var fake = new FakeProviderStore();
        var adapter = new SqlServerVectorStoreAdapter(fake, NullLogger<SqlServerVectorStoreAdapter>.Instance);

        var queryEmb = new float[1536];
        var results = await adapter.QueryAsync(queryEmb, 1);

        Assert.Single(results);
        Assert.Equal("https://github.com/test/repo", results[0].Document.RepoUrl);
        Assert.Equal(0f, results[0].SimilarityScore);
    }
}
