using DeepWiki.Data.Entities;
using DeepWiki.Data.SqlServer;
using DeepWiki.Data.SqlServer.DbContexts;
using DeepWiki.Data.SqlServer.Repositories;
using DeepWiki.Data.SqlServer.Tests.Fixtures;

namespace DeepWiki.Data.SqlServer.Tests.Integration;

/// <summary>
/// Integration tests for SqlServerVectorStore using Testcontainers.
/// Tests vector similarity operations against real SQL Server.
/// </summary>
public class SqlServerVectorStoreTests : IAsyncLifetime
{
    private readonly SqlServerFixture _fixture;
    private SqlServerVectorDbContext? _context;
    private SqlServerVectorStore? _vectorStore;

    public SqlServerVectorStoreTests()
    {
        _fixture = new SqlServerFixture();
    }

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _context = _fixture.CreateDbContext();
        _vectorStore = new SqlServerVectorStore(_context);
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
        var retrieved = await _vectorStore.QueryNearestAsync(doc.Embedding.GetValueOrDefault(), 1, null, CancellationToken.None);
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
        var retrieved = await _vectorStore.QueryNearestAsync(doc.Embedding.GetValueOrDefault(), 1, null, CancellationToken.None);
        Assert.Single(retrieved);
        Assert.Equal("Updated Title", retrieved[0].Title);
    }

    [Fact]
    public async Task QueryNearestAsync_ShouldReturnEmptyForEmptyStore()
    {
        // Act
        var results = await _vectorStore!.QueryNearestAsync(new ReadOnlyMemory<float>(CreateEmbedding(0.5f)), 10, null, CancellationToken.None);

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
        var results = await _vectorStore.QueryNearestAsync(new ReadOnlyMemory<float>(CreateEmbedding(0.5f)), 2, null, CancellationToken.None);

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
        var results = await _vectorStore.QueryNearestAsync(new ReadOnlyMemory<float>(CreateEmbedding(0.5f)), 2, null, CancellationToken.None);

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
        var results = await _vectorStore.QueryNearestAsync(new ReadOnlyMemory<float>(CreateEmbedding(0.5f)), 10, repo1, CancellationToken.None);

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
        var results = await _vectorStore.QueryNearestAsync(doc.Embedding ?? new ReadOnlyMemory<float>(), 10, null, CancellationToken.None);
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
        var results = await _vectorStore.QueryNearestAsync(new ReadOnlyMemory<float>(CreateEmbedding(0.5f)), 10, repoUrl, CancellationToken.None);
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
}
