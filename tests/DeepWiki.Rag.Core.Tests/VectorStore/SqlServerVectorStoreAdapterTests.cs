using System;
using System.Threading.Tasks;
using DeepWiki.Rag.Core.VectorStore;
using Xunit;

namespace DeepWiki.Rag.Core.Tests.VectorStore;

public class NoOpVectorStoreTests
{
    [Fact]
    public async Task QueryAsync_ReturnsEmptyList()
    {
        var store = new NoOpVectorStore();
        var res = await store.QueryAsync(new float[1536], 10);
        Assert.Empty(res);
    }

    [Fact]
    public async Task UpsertAsync_DoesNotThrow()
    {
        var store = new NoOpVectorStore();
        await store.UpsertAsync(new DeepWiki.Data.Abstractions.Models.DocumentDto { RepoUrl = "x", FilePath = "y", Embedding = new float[1536] });
    }
}
