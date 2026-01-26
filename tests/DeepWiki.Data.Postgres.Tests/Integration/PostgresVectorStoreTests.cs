using DeepWiki.Data.Entities;
using DeepWiki.Data.Postgres;
using DeepWiki.Data.Postgres.DbContexts;
using DeepWiki.Data.Postgres.Repositories;
using DeepWiki.Data.Postgres.Tests.Fixtures;
using Xunit;

namespace DeepWiki.Data.Postgres.Tests.Integration;

[Trait("Category","Integration")]

/// <summary>
/// Integration tests for PostgresVectorStore using Testcontainers.
/// Tests vector similarity operations against real PostgreSQL with pgvector.
/// These tests are identical to SqlServerVectorStoreTests to ensure 100% parity.
/// </summary>
public class PostgresVectorStoreTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private PostgresVectorDbContext? _context;
    private PostgresVectorStore? _vectorStore;

    public PostgresVectorStoreTests()
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

    private static float[] CreateEmbedding(float baseValue)
    {
        var embedding = new float[1536];
        for (int i = 0; i < embedding.Length; i++)
        {
            embedding[i] = baseValue + (float)Math.Sin(i * 0.01f) * 0.1f;
        }
        return embedding;
    }

    private DocumentEntity CreateTestDocument(string repoUrl = "https://github.com/test/repo", string filePath = "src/test.cs", float embeddingValue = 0.5f)
    {
        return new DocumentEntity
        {
            Id = Guid.NewGuid(),
            RepoUrl = repoUrl,
            FilePath = filePath,
            Title = "Test Document",
            Text = "This is test content",
            Embedding = new ReadOnlyMemory<float>(CreateEmbedding(embeddingValue)),
            FileType = "csharp",
            IsCode = true,
            IsImplementation = false,
            TokenCount = 100,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            MetadataJson = "{}"
        };
    }

    [Fact]
    public async Task UpsertAsync_ShouldInsertNewDocument()
    {
        // Arrange
        var doc = CreateTestDocument();

        // Act
        await _vectorStore!.UpsertAsync(doc, CancellationToken.None);

        // Assert
        var retrieved = await _vectorStore.QueryNearestAsync(doc.Embedding.GetValueOrDefault(), 1, null, null, CancellationToken.None);
        Assert.Single(retrieved);
        Assert.Equal(doc.Id, retrieved[0].Id);
    }

    [Fact]
    public async Task UpsertAsync_ShouldUpdateExistingDocument()
    {
        // Arrange
        var doc = CreateTestDocument();
        await _vectorStore!.UpsertAsync(doc, CancellationToken.None);

        // Act
        doc.Title = "Updated Title";
        doc.Text = "Updated content";
        await _vectorStore.UpsertAsync(doc, CancellationToken.None);

        // Assert
        var retrieved = await _vectorStore.QueryNearestAsync(doc.Embedding.GetValueOrDefault(), 1, null, null, CancellationToken.None);
        Assert.Single(retrieved);
        Assert.Equal("Updated Title", retrieved[0].Title);
    }

    [Fact]
    public async Task QueryNearestAsync_ShouldReturnEmptyForEmptyStore()
    {
        // Act
        var results = await _vectorStore!.QueryNearestAsync(new ReadOnlyMemory<float>(CreateEmbedding(0.5f)), 10, null, null, CancellationToken.None);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task QueryNearestAsync_ShouldReturnNearestVectors()
    {
        // Arrange
        var doc1 = CreateTestDocument(embeddingValue: 0.5f);
        var doc2 = CreateTestDocument("https://github.com/test/repo", "src/file2.cs", embeddingValue: 0.6f);
        var doc3 = CreateTestDocument("https://github.com/test/repo", "src/file3.cs", embeddingValue: 0.9f);

        await _vectorStore!.UpsertAsync(doc1, CancellationToken.None);
        await _vectorStore.UpsertAsync(doc2, CancellationToken.None);
        await _vectorStore.UpsertAsync(doc3, CancellationToken.None);

        // Act - Query with embedding similar to doc1
        var results = await _vectorStore.QueryNearestAsync(new ReadOnlyMemory<float>(CreateEmbedding(0.5f)), 2, null, null, CancellationToken.None);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Contains(doc1.Id, results.Select(r => r.Id));
    }

    [Fact]
    public async Task QueryNearestAsync_ShouldRespectK()
    {
        // Arrange
        var doc1 = CreateTestDocument(embeddingValue: 0.5f);
        var doc2 = CreateTestDocument("https://github.com/test/repo", "src/file2.cs", embeddingValue: 0.6f);
        var doc3 = CreateTestDocument("https://github.com/test/repo", "src/file3.cs", embeddingValue: 0.7f);

        await _vectorStore!.UpsertAsync(doc1, CancellationToken.None);
        await _vectorStore.UpsertAsync(doc2, CancellationToken.None);
        await _vectorStore.UpsertAsync(doc3, CancellationToken.None);

        // Act
        var results = await _vectorStore.QueryNearestAsync(new ReadOnlyMemory<float>(CreateEmbedding(0.5f)), 2, null, null, CancellationToken.None);

        // Assert
        Assert.True(results.Count <= 2);
    }

    [Fact]
    public async Task QueryNearestAsync_ShouldFilterByRepository()
    {
        // Arrange
        const string repo1 = "https://github.com/org/repo1";
        const string repo2 = "https://github.com/org/repo2";

        var doc1 = CreateTestDocument(repo1, "src/file1.cs", 0.5f);
        var doc2 = CreateTestDocument(repo2, "src/file2.cs", 0.5f);

        await _vectorStore!.UpsertAsync(doc1, CancellationToken.None);
        await _vectorStore.UpsertAsync(doc2, CancellationToken.None);

        // Act
        var results = await _vectorStore.QueryNearestAsync(new ReadOnlyMemory<float>(CreateEmbedding(0.5f)), 10, repo1, null, CancellationToken.None);

        // Assert
        Assert.Single(results);
        Assert.Equal(repo1, results[0].RepoUrl);
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveDocument()
    {
        // Arrange
        var doc = CreateTestDocument();
        await _vectorStore!.UpsertAsync(doc, CancellationToken.None);

        // Act
        await _vectorStore.DeleteAsync(doc.Id, CancellationToken.None);

        // Assert
        var results = await _vectorStore.QueryNearestAsync(doc.Embedding ?? new ReadOnlyMemory<float>(), 10, null, null, CancellationToken.None);
        Assert.Empty(results);
    }

    [Fact]
    public async Task DeleteByRepoAsync_ShouldRemoveAllDocumentsForRepository()
    {
        // Arrange
        const string repoUrl = "https://github.com/test/repo";
        var doc1 = CreateTestDocument(repoUrl, "src/file1.cs");
        var doc2 = CreateTestDocument(repoUrl, "src/file2.cs");

        await _vectorStore!.UpsertAsync(doc1, CancellationToken.None);
        await _vectorStore.UpsertAsync(doc2, CancellationToken.None);

        // Act
        await _vectorStore.DeleteByRepoAsync(repoUrl, CancellationToken.None);

        // Assert
        var results = await _vectorStore.QueryNearestAsync(new ReadOnlyMemory<float>(CreateEmbedding(0.5f)), 10, repoUrl, null, CancellationToken.None);
        Assert.Empty(results);
    }

    [Fact]
    public async Task CountAsync_ShouldReturnTotalCount()
    {
        // Arrange
        var doc1 = CreateTestDocument();
        var doc2 = CreateTestDocument("https://github.com/test/repo", "src/file2.cs");
        var doc3 = CreateTestDocument("https://github.com/test/repo", "src/file3.cs");

        await _vectorStore!.UpsertAsync(doc1, CancellationToken.None);
        await _vectorStore.UpsertAsync(doc2, CancellationToken.None);
        await _vectorStore.UpsertAsync(doc3, CancellationToken.None);

        // Act
        var count = await _vectorStore.CountAsync(null, CancellationToken.None);

        // Assert
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task CountAsync_ShouldFilterByRepository()
    {
        // Arrange
        const string repo1 = "https://github.com/org/repo1";
        const string repo2 = "https://github.com/org/repo2";

        var doc1 = CreateTestDocument(repo1);
        var doc2 = CreateTestDocument(repo2);

        await _vectorStore!.UpsertAsync(doc1, CancellationToken.None);
        await _vectorStore.UpsertAsync(doc2, CancellationToken.None);

        // Act
        var count = await _vectorStore.CountAsync(repo1, CancellationToken.None);

        // Assert
        Assert.Equal(1, count);
    }
    [Fact]
    public async Task UpsertFromFixtures_ShouldInsertAndQueryUsingRealEmbeddings()
    {
        // Arrange: load fixtures from repo
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var docsPath = Path.Combine(repoRoot, "tests", "DeepWiki.Rag.Core.Tests", "fixtures", "embedding-samples", "sample-documents.json");
        var embsPath = Path.Combine(repoRoot, "tests", "DeepWiki.Rag.Core.Tests", "fixtures", "embedding-samples", "sample-embeddings.json");

        var docsJson = await File.ReadAllTextAsync(docsPath);
        var embsJson = await File.ReadAllTextAsync(embsPath);

        var docs = System.Text.Json.JsonSerializer.Deserialize<List<FixtureDoc>>(docsJson) ?? new List<FixtureDoc>();
        var embs = System.Text.Json.JsonSerializer.Deserialize<List<FixtureEmb>>(embsJson) ?? new List<FixtureEmb>();

        // Upsert fixtures (pad/truncate embeddings to 1536 dims)
        var fixedMap = new Dictionary<string, (float[] Emb, string FilePath)>();
        foreach (var emb in embs)
        {
            var docSrc = docs.FirstOrDefault(d => d.Id == emb.Id);
            if (docSrc == null) continue;

            var raw = emb.Embedding.ToArray();
            var fixedEmb = new float[1536];
            Array.Fill(fixedEmb, 0f);
            Array.Copy(raw, fixedEmb, Math.Min(raw.Length, 1536));

            fixedMap[emb.Id] = (fixedEmb, docSrc.FilePath);

            var doc = new DocumentEntity
            {
                Id = Guid.Parse(docSrc.Id),
                RepoUrl = docSrc.RepoUrl,
                FilePath = docSrc.FilePath,
                Title = docSrc.Title,
                Text = docSrc.Text,
                Embedding = new ReadOnlyMemory<float>(fixedEmb),
                FileType = Path.GetExtension(docSrc.FilePath).TrimStart('.'),
                IsCode = docSrc.FilePath.EndsWith(".cs"),
                IsImplementation = true,
                TokenCount = 1,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                MetadataJson = "{}"
            };

            await _vectorStore!.UpsertAsync(doc, CancellationToken.None);
        }

        // Act: verify insertion count
        var total = await _vectorStore!.CountAsync(null, CancellationToken.None);
        Assert.Equal(fixedMap.Count, total);

        // Assert: at least half of the upserted docs return themselves (by FilePath) within top-3 nearest
        var successes = 0;
        foreach (var kvp in fixedMap)
        {
            var queryEmb = new ReadOnlyMemory<float>(kvp.Value.Emb);
            var results = await _vectorStore!.QueryNearestAsync(queryEmb, 3, null, null, CancellationToken.None);
            if (results.Any(r => r.FilePath == kvp.Value.FilePath)) successes++;
        }

        Assert.True(successes >= Math.Max(1, fixedMap.Count / 2), $"Expected at least {Math.Max(1, fixedMap.Count / 2)} matches, got {successes}");
    }

    // Performance tests moved to `tests/DeepWiki.Data.Postgres.Tests/Performance/PostgresVectorStorePerformanceTests.cs`

    private record FixtureDoc(
        [property: System.Text.Json.Serialization.JsonPropertyName("id")] string Id,
        [property: System.Text.Json.Serialization.JsonPropertyName("repoUrl")] string RepoUrl,
        [property: System.Text.Json.Serialization.JsonPropertyName("filePath")] string FilePath,
        [property: System.Text.Json.Serialization.JsonPropertyName("title")] string Title,
        [property: System.Text.Json.Serialization.JsonPropertyName("text")] string Text,
        [property: System.Text.Json.Serialization.JsonPropertyName("metadata")] object Metadata);

    private record FixtureEmb(
        [property: System.Text.Json.Serialization.JsonPropertyName("id")] string Id,
        [property: System.Text.Json.Serialization.JsonPropertyName("embedding")] List<float> Embedding);
}

