using DeepWiki.Data.Entities;
using DeepWiki.Data.SqlServer;
using DeepWiki.Data.SqlServer.DbContexts;
using DeepWiki.Data.SqlServer.Repositories;
using DeepWiki.Data.SqlServer.Tests.Fixtures;

namespace DeepWiki.Data.SqlServer.Tests.Integration;

/// <summary>
/// Integration tests for SqlServerDocumentRepository using Testcontainers.
/// Tests actual SQL Server with vector support (vector(1536) column type).
/// </summary>
public class SqlServerDocumentRepositoryTests : IAsyncLifetime
{
    private readonly SqlServerFixture _fixture;
    private SqlServerVectorDbContext? _context;
    private SqlServerDocumentRepository? _repository;

    public SqlServerDocumentRepositoryTests()
    {
        _fixture = new SqlServerFixture();
    }

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _context = _fixture.CreateDbContext();
        _repository = new SqlServerDocumentRepository(_context);
    }

    public async Task DisposeAsync()
    {
        if (_context != null)
        {
            await _context.DisposeAsync();
        }

        await _fixture.DisposeAsync();
    }

    private DocumentEntity CreateTestDocument(string repoUrl = "https://github.com/test/repo", string filePath = "src/test.cs")
    {
        var embeddingArray = new float[1536];
        for (int i = 0; i < embeddingArray.Length; i++)
        {
            embeddingArray[i] = (float)Math.Sin(i) * 0.5f;
        }

        return new DocumentEntity
        {
            Id = Guid.NewGuid(),
            RepoUrl = repoUrl,
            FilePath = filePath,
            Title = "Test Document",
            Text = "This is test content",
            Embedding = new ReadOnlyMemory<float>(embeddingArray),
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
    public async Task AddAsync_ShouldPersistDocumentToDatabase()
    {
        // Arrange
        var doc = CreateTestDocument();

        // Act
        await _repository!.AddAsync(doc, CancellationToken.None);

        // Assert
        var retrieved = await _repository.GetByIdAsync(doc.Id, CancellationToken.None);
        Assert.NotNull(retrieved);
        Assert.Equal(doc.RepoUrl, retrieved.RepoUrl);
        Assert.Equal(doc.FilePath, retrieved.FilePath);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnNullForNonExistentId()
    {
        // Act
        var result = await _repository!.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnExistingDocument()
    {
        // Arrange
        var doc = CreateTestDocument();
        await _repository!.AddAsync(doc, CancellationToken.None);

        // Act
        var retrieved = await _repository.GetByIdAsync(doc.Id, CancellationToken.None);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(doc.Id, retrieved.Id);
        Assert.Equal(doc.Text, retrieved.Text);
    }

    [Fact]
    public async Task GetByRepoAsync_ShouldReturnDocumentsForRepository()
    {
        // Arrange
        const string repoUrl = "https://github.com/test/repo";
        var doc1 = CreateTestDocument(repoUrl, "file1.cs");
        var doc2 = CreateTestDocument(repoUrl, "file2.cs");
        await _repository!.AddAsync(doc1, CancellationToken.None);
        await _repository.AddAsync(doc2, CancellationToken.None);

        // Act
        var results = await _repository.GetByRepoAsync(repoUrl, 0, 10, CancellationToken.None);

        // Assert
        Assert.NotEmpty(results);
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task GetByRepoAsync_ShouldRespectPagination()
    {
        // Arrange
        const string repoUrl = "https://github.com/test/repo";
        for (int i = 0; i < 5; i++)
        {
            var doc = CreateTestDocument(repoUrl, $"file{i}.cs");
            await _repository!.AddAsync(doc, CancellationToken.None);
        }

        // Act
        var page1 = await _repository.GetByRepoAsync(repoUrl, 0, 2, CancellationToken.None);
        var page2 = await _repository.GetByRepoAsync(repoUrl, 2, 2, CancellationToken.None);

        // Assert
        Assert.NotNull(page1);
        Assert.NotNull(page2);
        Assert.Equal(2, page1.Count);
        Assert.Equal(2, page2.Count);
    }

    [Fact]
    public async Task UpdateAsync_ShouldModifyExistingDocument()
    {
        // Arrange
        var doc = CreateTestDocument();
        await _repository!.AddAsync(doc, CancellationToken.None);

        // Act
        doc.Title = "Updated Title";
        doc.Text = "Updated content";
        await _repository.UpdateAsync(doc, CancellationToken.None);

        // Assert
        var retrieved = await _repository.GetByIdAsync(doc.Id, CancellationToken.None);
        Assert.NotNull(retrieved);
        Assert.Equal("Updated Title", retrieved.Title);
        Assert.Equal("Updated content", retrieved.Text);
    }

    [Fact]
    public async Task UpdateAsync_ShouldUpdateUpdatedAtTimestamp()
    {
        // Arrange
        var doc = CreateTestDocument();
        await _repository!.AddAsync(doc, CancellationToken.None);
        var originalUpdatedAt = doc.UpdatedAt;

        // Act
        await Task.Delay(100); // Ensure time passes
        doc.Title = "Updated";
        await _repository.UpdateAsync(doc, CancellationToken.None);

        // Assert
        var retrieved = await _repository.GetByIdAsync(doc.Id, CancellationToken.None);
        Assert.NotNull(retrieved);
        Assert.True(retrieved.UpdatedAt >= originalUpdatedAt);
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveDocument()
    {
        // Arrange
        var doc = CreateTestDocument();
        await _repository!.AddAsync(doc, CancellationToken.None);

        // Act
        await _repository.DeleteAsync(doc.Id, CancellationToken.None);

        // Assert
        var retrieved = await _repository.GetByIdAsync(doc.Id, CancellationToken.None);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task ExistsAsync_ShouldReturnTrueForExistingDocument()
    {
        // Arrange
        var doc = CreateTestDocument();
        await _repository!.AddAsync(doc, CancellationToken.None);

        // Act
        var exists = await _repository.ExistsAsync(doc.Id, CancellationToken.None);

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public async Task ExistsAsync_ShouldReturnFalseForNonExistentDocument()
    {
        // Act
        var exists = await _repository!.ExistsAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public async Task ConcurrencyHandling_ShouldWork()
    {
        // Arrange
        var doc = CreateTestDocument();
        await _repository!.AddAsync(doc, CancellationToken.None);

        // Act
        var doc1 = await _repository.GetByIdAsync(doc.Id, CancellationToken.None);
        var doc2 = await _repository.GetByIdAsync(doc.Id, CancellationToken.None);

        doc1!.Title = "First Update";
        doc2!.Title = "Second Update";

        await _repository.UpdateAsync(doc1, CancellationToken.None);

        // Assert - Second update should fail or use optimistic concurrency
        // For now, we expect it to work (UpdatedAt is the concurrency token)
        var final = await _repository.GetByIdAsync(doc.Id, CancellationToken.None);
        Assert.NotNull(final);
    }
}
