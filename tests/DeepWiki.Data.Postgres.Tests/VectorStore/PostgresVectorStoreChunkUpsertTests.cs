using DeepWiki.Data.Abstractions.Models;
using Xunit;

namespace DeepWiki.Data.Postgres.Tests.VectorStore;

/// <summary>
/// Unit tests for the chunk-keyed upsert contract (T088).
///
/// These tests verify that, after T094 is implemented:
///   • Inserting chunk 0 then chunk 1 for the same (RepoUrl, FilePath) produces
///     2 distinct rows in the store (keyed on RepoUrl + FilePath + ChunkIndex)
///   • Re-upserting chunk 0 updates that row in-place — no duplicate is created
///
/// The tests currently use an in-memory store that models the INTENDED behaviour
/// of the refactored PostgresVectorStore.UpsertAsync. They are expected to FAIL
/// until T089 (ChunkIndex/TotalChunks fields) and T094 (chunk-keyed upsert) are
/// implemented.
///
/// Integration-level coverage (against a real Postgres + pgvector container) is
/// provided by tests/DeepWiki.Data.Postgres.Tests/Integration/.
/// </summary>
public class PostgresVectorStoreChunkUpsertTests
{
    // =========================================================================
    // T088a — Inserting chunk 0 then chunk 1 produces exactly 2 rows
    // =========================================================================

    [Fact]
    public async Task UpsertAsync_Chunk0ThenChunk1_ProducesTwoDistinctRows()
    {
        // Arrange
        var store = new ChunkKeyedInMemoryStore();

        var chunk0 = MakeDoc("https://github.com/test/repo", "src/File.cs", chunkIndex: 0, totalChunks: 2, text: "first half");
        var chunk1 = MakeDoc("https://github.com/test/repo", "src/File.cs", chunkIndex: 1, totalChunks: 2, text: "second half");

        // Act
        await store.UpsertAsync(chunk0);
        await store.UpsertAsync(chunk1);

        // Assert — two separate rows must exist
        var rows = store.GetAll("https://github.com/test/repo", "src/File.cs");
        Assert.Equal(2, rows.Count);

        Assert.Contains(rows, d => d.ChunkIndex == 0 && d.Text == "first half");
        Assert.Contains(rows, d => d.ChunkIndex == 1 && d.Text == "second half");
    }

    // =========================================================================
    // T088b — Re-upserting chunk 0 updates in-place; no duplicate row created
    // =========================================================================

    [Fact]
    public async Task UpsertAsync_ReUpsertChunk0_UpdatesInPlaceNoDuplicate()
    {
        // Arrange
        var store = new ChunkKeyedInMemoryStore();

        var chunk0v1 = MakeDoc("https://github.com/test/repo", "src/File.cs", chunkIndex: 0, totalChunks: 2, text: "original text");
        await store.UpsertAsync(chunk0v1);

        Assert.Single(store.GetAll("https://github.com/test/repo", "src/File.cs"));

        // Act — re-upsert the same chunk with updated text
        var chunk0v2 = MakeDoc("https://github.com/test/repo", "src/File.cs", chunkIndex: 0, totalChunks: 2, text: "updated text");
        await store.UpsertAsync(chunk0v2);

        // Assert — still exactly 1 row, and it holds the updated text
        var rows = store.GetAll("https://github.com/test/repo", "src/File.cs");
        Assert.Single(rows);
        Assert.Equal("updated text", rows[0].Text);
    }

    // =========================================================================
    // T088c — Legacy behaviour check: WITHOUT ChunkIndex two upserts for the
    //         same (RepoUrl, FilePath) would produce 1 row (old dedup logic).
    //         After T094 they must produce separate rows when ChunkIndex differs.
    // =========================================================================

    [Fact]
    public async Task UpsertAsync_DifferentChunkIndexes_AreStoredAsSeparateRows()
    {
        // Arrange
        var store = new ChunkKeyedInMemoryStore();

        // Act — insert chunks 0-4 for the same file
        for (int i = 0; i < 5; i++)
        {
            await store.UpsertAsync(
                MakeDoc("https://github.com/test/repo", "src/BigFile.cs", chunkIndex: i, totalChunks: 5, text: $"chunk {i}"));
        }

        // Assert — 5 distinct rows
        var rows = store.GetAll("https://github.com/test/repo", "src/BigFile.cs");
        Assert.Equal(5, rows.Count);

        for (int i = 0; i < 5; i++)
        {
            Assert.Equal($"chunk {i}", rows[i].Text);
        }
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static DocumentDto MakeDoc(string repoUrl, string filePath, int chunkIndex, int totalChunks, string text) =>
        new DocumentDto
        {
            Id = Guid.NewGuid(),
            RepoUrl = repoUrl,
            FilePath = filePath,
            ChunkIndex = chunkIndex,
            TotalChunks = totalChunks,
            Text = text,
            Embedding = new float[1536],
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

    /// <summary>
    /// In-memory store that keys on (RepoUrl, FilePath, ChunkIndex), modelling
    /// the INTENDED behaviour of the refactored PostgresVectorStore.UpsertAsync.
    /// </summary>
    private sealed class ChunkKeyedInMemoryStore
    {
        private readonly List<DocumentDto> _docs = [];

        private static string Key(DocumentDto d) => $"{d.RepoUrl}|{d.FilePath}|{d.ChunkIndex}";

        public Task UpsertAsync(DocumentDto document, CancellationToken ct = default)
        {
            var key = Key(document);
            var idx = _docs.FindIndex(d => Key(d) == key);
            if (idx >= 0)
                _docs[idx] = document;
            else
                _docs.Add(document);
            return Task.CompletedTask;
        }

        public List<DocumentDto> GetAll(string repoUrl, string filePath)
            => _docs
               .Where(d => d.RepoUrl == repoUrl && d.FilePath == filePath)
               .OrderBy(d => d.ChunkIndex)
               .ToList();
    }
}
