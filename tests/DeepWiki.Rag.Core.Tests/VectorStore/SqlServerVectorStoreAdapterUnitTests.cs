using System;
using System.Threading.Tasks;
using DeepWiki.Data.Abstractions.Models;
using DeepWiki.Rag.Core.Tests.TestUtilities;
using DeepWiki.Rag.Core.VectorStore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DeepWiki.Rag.Core.Tests.VectorStore;

public class SqlServerVectorStoreAdapterUnitTests
{
    [Fact]
    public async Task MockVectorStore_QueryAsync_ReturnsUpsertedDocuments()
    {
        var store = new MockVectorStore();
        await store.UpsertAsync(new DocumentDto { RepoUrl = "r", FilePath = "f", Embedding = new float[1536] });
        var results = await store.QueryAsync(new float[1536], 5, null);
        Assert.Single(results);
    }

    [Fact]
    public async Task SqlServerVectorStoreAdapterShim_ThrowsNotSupported()
    {
        var shim = new SqlServerVectorStoreAdapter(NullLogger<SqlServerVectorStoreAdapter>.Instance);

        await Assert.ThrowsAsync<NotSupportedException>(() => shim.QueryAsync(new float[1536], 1, null));
        await Assert.ThrowsAsync<NotSupportedException>(() => shim.UpsertAsync(new DocumentDto { Embedding = new float[1536] }));
        await Assert.ThrowsAsync<NotSupportedException>(() => shim.DeleteAsync(Guid.NewGuid()));
        // RebuildIndexAsync is intentional no-op
        await shim.RebuildIndexAsync();
    }
}
