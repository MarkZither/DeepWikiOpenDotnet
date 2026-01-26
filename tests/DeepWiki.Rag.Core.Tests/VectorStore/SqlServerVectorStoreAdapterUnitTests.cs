using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DeepWiki.Data.Abstractions.Models;
using DeepWiki.Rag.Core.VectorStore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DeepWiki.Rag.Core.Tests.VectorStore;

public class SqlServerVectorStoreAdapterUnitTests
{
    [Fact]
    public async Task NoOpVectorStore_QueryAsync_ReturnsEmpty()
    {
        var store = new NoOpVectorStore();
        var results = await store.QueryAsync(new float[1536], 5, null);
        Assert.Empty(results);
    }

    [Fact]
    public async Task SqlServerVectorStoreAdapterShim_ThrowsNotSupported()
    {
        var shim = new SqlServerVectorStoreAdapter(new Microsoft.Extensions.Logging.Abstractions.NullLogger<SqlServerVectorStoreAdapter>());

        await Assert.ThrowsAsync<NotSupportedException>(() => shim.QueryAsync(new float[1536], 1, null));
        await Assert.ThrowsAsync<NotSupportedException>(() => shim.UpsertAsync(new DeepWiki.Data.Abstractions.Models.DocumentDto { Embedding = new float[1536] }));
        await Assert.ThrowsAsync<NotSupportedException>(() => shim.DeleteAsync(Guid.NewGuid()));
        // RebuildIndexAsync should be a no-op
        await shim.RebuildIndexAsync();
    }
}