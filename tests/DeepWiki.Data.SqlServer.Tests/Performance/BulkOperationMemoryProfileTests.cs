using DeepWiki.Data.Entities;
using DeepWiki.Data.SqlServer.DbContexts;
using DeepWiki.Data.SqlServer.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using Testcontainers.MsSql;
using Xunit;

namespace DeepWiki.Data.SqlServer.Tests.Performance;

/// <summary>
/// Memory profiling tests for bulk operations at scale.
/// Verifies that BulkUpsertAsync remains memory efficient with 1000+ documents.
/// </summary>
public class BulkOperationMemoryProfileTests : IAsyncLifetime
{
    private MsSqlContainer? _container;
    private string? _connectionString;

    public async Task InitializeAsync()
    {
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-latest")
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("MSSQL_SA_PASSWORD", "Strong@Password123")
            .Build();

        await _container.StartAsync();
        _connectionString = _container.GetConnectionString();

        // Apply migrations
        var options = new DbContextOptionsBuilder<SqlServerVectorDbContext>()
            .UseSqlServer(_connectionString)
            .Options;

        using (var context = new SqlServerVectorDbContext(options))
        {
            await context.Database.MigrateAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (_container != null)
        {
            await _container.StopAsync();
            await _container.DisposeAsync();
        }
    }

    /// <summary>
    /// Profiles memory usage when bulk upserting 1000 documents.
    /// Target: Peak memory should remain under 500MB.
    /// </summary>
    [Fact(Skip = "Performance profiling test - run manually")]
    public async Task BulkUpsert_1000Documents_MemoryUsageShouldBeEfficient()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<SqlServerVectorDbContext>()
            .UseSqlServer(_connectionString)
            .Options;

        var documents = GenerateTestDocuments(1000);
        var memoryBefore = GC.GetTotalMemory(true) / 1024 / 1024; // MB

        var stopwatch = Stopwatch.StartNew();
        var gcCountBefore = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);

        // Act
        using (var context = new SqlServerVectorDbContext(options))
        {
            var vectorStore = new SqlServerVectorStore(context);
            await vectorStore.BulkUpsertAsync(documents);
        }

        stopwatch.Stop();
        var gcCountAfter = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);
        var memoryAfter = GC.GetTotalMemory(true) / 1024 / 1024; // MB

        // Assert
        var peakMemoryMB = memoryAfter - memoryBefore;
        var gcCollectionsTriggered = gcCountAfter - gcCountBefore;

        // Document performance characteristics
        var output = $@"
Memory Profiling Results (1000 documents):
============================================
Elapsed Time: {stopwatch.ElapsedMilliseconds}ms
Peak Memory Usage: {peakMemoryMB}MB
GC Collections: {gcCollectionsTriggered}
Memory Before: {memoryBefore}MB
Memory After: {memoryAfter}MB

Target: < 500MB peak memory
Result: {(peakMemoryMB < 500 ? "PASS" : "FAIL")} ({peakMemoryMB}MB)
";

        Assert.True(peakMemoryMB < 500, output);
        Assert.True(gcCollectionsTriggered <= 10, $"Too many GC collections: {gcCollectionsTriggered}");
    }

    /// <summary>
    /// Profiles memory usage with incremental batch sizes to find optimal batch size.
    /// Tests 100, 250, 500 document batches within the 1000 document bulk operation.
    /// </summary>
    [Fact(Skip = "Performance profiling test - run manually")]
    public async Task BulkUpsert_VariableBatchSizes_ShouldOptimizeMemory()
    {
        var options = new DbContextOptionsBuilder<SqlServerVectorDbContext>()
            .UseSqlServer(_connectionString)
            .Options;

        var allDocuments = GenerateTestDocuments(1000);
        var batchSizes = new[] { 100, 250, 500 };
        var results = new Dictionary<int, (long TimeMs, double PeakMemoryMB, int GCCollections)>();

        foreach (var batchSize in batchSizes)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var memoryBefore = GC.GetTotalMemory(false) / 1024 / 1024;
            var gcCountBefore = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);
            var stopwatch = Stopwatch.StartNew();

            // Act: Bulk upsert with this batch size
            using (var context = new SqlServerVectorDbContext(options))
            {
                var vectorStore = new SqlServerVectorStore(context);
                // Process in batches
                for (int i = 0; i < allDocuments.Count; i += batchSize)
                {
                    var batch = allDocuments.Skip(i).Take(batchSize);
                    await vectorStore.BulkUpsertAsync(batch);
                }
            }

            stopwatch.Stop();
            var memoryAfter = GC.GetTotalMemory(true) / 1024 / 1024;
            var gcCountAfter = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);

            var peakMemory = memoryAfter - memoryBefore;
            var gcCollections = gcCountAfter - gcCountBefore;

            results[batchSize] = (stopwatch.ElapsedMilliseconds, peakMemory, gcCollections);
        }

        // Output results
        var output = @"
Batch Size Memory Profile (1000 documents total):
===================================================";

        foreach (var (batchSize, (timeMs, peakMemMB, gcCount)) in results)
        {
            output += $@"
Batch Size: {batchSize}
  Elapsed Time: {timeMs}ms
  Peak Memory: {peakMemMB}MB
  GC Collections: {gcCount}";
        }

        Assert.True(results.Values.All(r => r.PeakMemoryMB < 500), 
            $"At least one batch size exceeded 500MB memory: {output}");
    }

    /// <summary>
    /// Profiles concurrent bulk operations to verify memory isolation between contexts.
    /// </summary>
    [Fact(Skip = "Performance profiling test - run manually")]
    public async Task BulkUpsert_ConcurrentOperations_MemoryShouldBeIsolated()
    {
        var options = new DbContextOptionsBuilder<SqlServerVectorDbContext>()
            .UseSqlServer(_connectionString)
            .Options;

        var documentsSet1 = GenerateTestDocuments(500, "repo1");
        var documentsSet2 = GenerateTestDocuments(500, "repo2");

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var memoryBefore = GC.GetTotalMemory(false) / 1024 / 1024;
        var stopwatch = Stopwatch.StartNew();

        // Act: Run concurrent bulk upserts
        var task1 = Task.Run(async () =>
        {
            using (var context = new SqlServerVectorDbContext(options))
            {
                var vectorStore = new SqlServerVectorStore(context);
                await vectorStore.BulkUpsertAsync(documentsSet1);
            }
        });

        var task2 = Task.Run(async () =>
        {
            using (var context = new SqlServerVectorDbContext(options))
            {
                var vectorStore = new SqlServerVectorStore(context);
                await vectorStore.BulkUpsertAsync(documentsSet2);
            }
        });

        await Task.WhenAll(task1, task2);
        stopwatch.Stop();

        var memoryAfter = GC.GetTotalMemory(true) / 1024 / 1024;
        var peakMemory = memoryAfter - memoryBefore;

        // Assert
        Assert.True(peakMemory < 600, $"Concurrent memory usage too high: {peakMemory}MB (target <600MB)");
    }

    private List<DocumentEntity> GenerateTestDocuments(int count, string? repoUrl = null)
    {
        var documents = new List<DocumentEntity>();
        var baseRepo = repoUrl ?? "https://github.com/test/repo";

        for (int i = 0; i < count; i++)
        {
            var embedding = new float[1536];
            var random = new Random(i);
            for (int j = 0; j < embedding.Length; j++)
            {
                embedding[j] = (float)random.NextDouble();
            }

            documents.Add(new DocumentEntity
            {
                Id = Guid.NewGuid(),
                RepoUrl = baseRepo,
                FilePath = $"src/file{i}.cs",
                Title = $"File {i}",
                Text = $"Content for file {i}",
                Embedding = new ReadOnlyMemory<float>(embedding),
                FileType = "cs",
                IsCode = true,
                TokenCount = 100 + i
            });
        }

        return documents;
    }
}
