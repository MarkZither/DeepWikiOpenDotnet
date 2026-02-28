using System.Threading.Tasks;
using DeepWiki.Rag.Core.Tests.TestUtilities;
using Xunit;

namespace DeepWiki.Rag.Core.Tests.VectorStore;

public class MockVectorStoreTests
{
    [Fact]
    public async Task QueryAsync_ReturnsUpsertedDocuments()
    {
        var store = new MockVectorStore();
        await store.UpsertAsync(new DeepWiki.Data.Abstractions.Models.DocumentDto
            { RepoUrl = "r", FilePath = "f", Embedding = new float[1536] });

        var res = await store.QueryAsync(new float[1536], 10);
        Assert.Single(res);
    }

    [Fact]
    public async Task QueryAsync_EmptyStore_ReturnsEmpty()
    {
        var store = new MockVectorStore();
        var res = await store.QueryAsync(new float[1536], 10);
        Assert.Empty(res);
    }

    [Fact]
    public async Task UpsertAsync_TracksCallCount()
    {
        var store = new MockVectorStore();
        await store.UpsertAsync(new DeepWiki.Data.Abstractions.Models.DocumentDto
            { RepoUrl = "r", FilePath = "f", Embedding = new float[1536] });
        Assert.Equal(1, store.UpsertCallCount);
    }
}
