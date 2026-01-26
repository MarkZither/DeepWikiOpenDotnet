using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using DeepWiki.Data.Entities;
using DeepWiki.Data.SqlServer.DbContexts;
using DeepWiki.Data.SqlServer.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DeepWiki.Data.SqlServer.Tests.VectorStore;

public class SqlServerVectorStoreUnitTests
{
    private static SqlServerVectorDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<SqlServerVectorDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var ctx = new SqlServerVectorDbContext(options);
        return ctx;
    }

    private static ReadOnlyMemory<float> MakeEmbedding(params float[] values)
    {
        // Create a 1536-dim array with provided values at start and zeros for remainder
        var arr = new float[1536];
        for (int i = 0; i < values.Length && i < arr.Length; i++) arr[i] = values[i];
        return new ReadOnlyMemory<float>(arr);
    }

    [Fact]
    public async Task QueryNearestAsync_ReturnsTopKOrderedBySimilarity()
    {
        using var ctx = CreateInMemoryContext();
        var store = new SqlServerVectorStore(ctx);

        var docA = new DocumentEntity { RepoUrl = "r1", FilePath = "f1", Text = "a", Title = "A", Embedding = MakeEmbedding(1,0,0) };
        var docB = new DocumentEntity { RepoUrl = "r1", FilePath = "f2", Text = "b", Title = "B", Embedding = MakeEmbedding(0,1,0) };
        ctx.Documents.AddRange(docA, docB);
        await ctx.SaveChangesAsync();

        var result = await store.QueryNearestAsync(MakeEmbedding(1,0,0), k:2);

        Assert.Equal(2, result.Count);
        Assert.Equal("f1", result[0].FilePath); // docA should be first
    }

    [Fact]
    public async Task QueryNearestAsync_WithNoMatches_ReturnsEmpty()
    {
        using var ctx = CreateInMemoryContext();
        var store = new SqlServerVectorStore(ctx);

        // no documents
        var result = await store.QueryNearestAsync(MakeEmbedding(1,0,0), k:5);
        Assert.Empty(result);
    }

    [Fact]
    public async Task QueryNearestAsync_RepoFilter_ExactMatch_Works()
    {
        using var ctx = CreateInMemoryContext();
        var store = new SqlServerVectorStore(ctx);

        var doc1 = new DocumentEntity { RepoUrl = "https://github.com/org/repo1", FilePath = "src/a.cs", Text = "a", Title = "A", Embedding = MakeEmbedding(1,0,0) };
        var doc2 = new DocumentEntity { RepoUrl = "https://github.com/other/repo2", FilePath = "src/b.cs", Text = "b", Title = "B", Embedding = MakeEmbedding(1,0,0) };
        ctx.Documents.AddRange(doc1, doc2);
        await ctx.SaveChangesAsync();

        var result = await store.QueryNearestAsync(MakeEmbedding(1,0,0), k:10, repoUrlFilter: "https://github.com/org/repo1");

        Assert.Single(result);
        Assert.Equal("https://github.com/org/repo1", result[0].RepoUrl);
    }

    [Fact]
    public async Task QueryNearestAsync_MultipleFilters_ExactMatch_ReturnsIntersection()
    {
        using var ctx = CreateInMemoryContext();
        var store = new SqlServerVectorStore(ctx);

        var doc1 = new DocumentEntity { RepoUrl = "https://github.com/org/repo1", FilePath = "src/a.cs", Text = "a", Title = "A", Embedding = MakeEmbedding(1,0,0) };
        var doc2 = new DocumentEntity { RepoUrl = "https://github.com/org/repo1", FilePath = "docs/readme.md", Text = "b", Title = "B", Embedding = MakeEmbedding(1,0,0) };
        ctx.Documents.AddRange(doc1, doc2);
        await ctx.SaveChangesAsync();

        var result = await store.QueryNearestAsync(MakeEmbedding(1,0,0), k:10, repoUrlFilter: "https://github.com/org/repo1", filePathFilter: "src/a.cs");

        Assert.Single(result);
        Assert.Equal("src/a.cs", result[0].FilePath);
    }

    [Fact]
    public async Task UpsertAsync_InsertsNewDocument()
    {
        using var ctx = CreateInMemoryContext();
        var store = new SqlServerVectorStore(ctx);

        var doc = new DocumentEntity { RepoUrl = "r1", FilePath = "f1", Text = "text", Title = "T", Embedding = MakeEmbedding(1,0,0) };
        await store.UpsertAsync(doc);

        var count = await ctx.Documents.CountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task UpsertAsync_UpdatesExisting_NoDuplicate()
    {
        using var ctx = CreateInMemoryContext();
        var store = new SqlServerVectorStore(ctx);

        var doc1 = new DocumentEntity { RepoUrl = "r1", FilePath = "f1", Text = "old", Title = "Old", Embedding = MakeEmbedding(1,0,0) };
        ctx.Documents.Add(doc1);
        await ctx.SaveChangesAsync();

        var doc2 = new DocumentEntity { RepoUrl = "r1", FilePath = "f1", Text = "new", Title = "New", Embedding = MakeEmbedding(0,1,0) };
        await store.UpsertAsync(doc2);

        var all = await ctx.Documents.Where(d => d.RepoUrl == "r1" && d.FilePath == "f1").ToListAsync();
        Assert.Single(all);
        Assert.Equal("new", all[0].Text);
    }

    [Fact]
    public async Task UpsertAsync_InvalidEmbeddingDimension_ThrowsAndDoesNotPersist()
    {
        using var ctx = CreateInMemoryContext();
        var store = new SqlServerVectorStore(ctx);

        // Create an invalid embedding (length != 1536)
        var invalid = new ReadOnlyMemory<float>(new float[10]);
        var doc = new DocumentEntity { RepoUrl = "r1", FilePath = "f1", Text = "text", Title = "T", Embedding = invalid };

        await Assert.ThrowsAsync<ArgumentException>(() => store.UpsertAsync(doc));
        Assert.Equal(0, await ctx.Documents.CountAsync());
    }

    [Fact(Skip = "Integration test - requires real database to verify concurrency behavior")]
    public async Task UpsertAsync_ConcurrentWrites_NoDuplicates()
    {
        // Integration-only test: verify with real DB provider and appropriate isolation levels
    }

    [Fact]
    public async Task DeleteAsync_RemovesDocumentById()
    {
        using var ctx = CreateInMemoryContext();
        var store = new SqlServerVectorStore(ctx);

        var doc = new DocumentEntity { RepoUrl = "r1", FilePath = "f1", Text = "t", Title = "T", Embedding = MakeEmbedding(1,0,0) };
        ctx.Documents.Add(doc);
        await ctx.SaveChangesAsync();

        await store.DeleteAsync(doc.Id);
        Assert.Equal(0, await ctx.Documents.CountAsync());
    }

    [Fact(Skip = "Integration test - verify execution of ALTER INDEX against a real SQL Server instance")]
    public async Task RebuildIndexAsync_CompletesWithoutThrowing()
    {
        // Integration-only test: verify RebuildIndexAsync executes or swallows provider-specific SQL errors on real DB
    }

    [Fact]
    public async Task QueryNearestAsync_InvalidQueryEmbedding_Throws()
    {
        using var ctx = CreateInMemoryContext();
        var store = new SqlServerVectorStore(ctx);

        var invalid = new ReadOnlyMemory<float>(new float[10]);
        await Assert.ThrowsAsync<ArgumentException>(() => store.QueryNearestAsync(invalid));
    }
}
