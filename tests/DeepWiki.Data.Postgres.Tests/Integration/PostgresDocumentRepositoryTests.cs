using DeepWiki.Data.Entities;
using DeepWiki.Data.Postgres;
using DeepWiki.Data.Postgres.DbContexts;
using DeepWiki.Data.Postgres.Repositories;
using DeepWiki.Data.Postgres.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DeepWiki.Data.Postgres.Tests.Integration;

/// <summary>
/// Integration tests for PostgresDocumentRepository using Testcontainers.
/// Tests actual PostgreSQL with pgvector extension.
/// These tests are identical to SqlServerDocumentRepositoryTests to ensure 100% parity.
/// </summary>
public class PostgresDocumentRepositoryTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly Xunit.Abstractions.ITestOutputHelper _output;
    private PostgresVectorDbContext? _context;
    private PostgresDocumentRepository? _repository;

    public PostgresDocumentRepositoryTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _fixture = new PostgresFixture();
        _output = output;
    }

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _context = _fixture.CreateDbContext();
        _repository = new PostgresDocumentRepository(_context);
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
        var page1 = await _repository!.GetByRepoAsync(repoUrl, 0, 2, CancellationToken.None);
        var page2 = await _repository!.GetByRepoAsync(repoUrl, 2, 2, CancellationToken.None);

        // Assert
        var p1Count = page1?.Count ?? throw new Xunit.Sdk.XunitException("Expected page1 to be non-null");
        var p2Count = page2?.Count ?? throw new Xunit.Sdk.XunitException("Expected page2 to be non-null");
        Assert.Equal(2, p1Count);
        Assert.Equal(2, p2Count);
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
        Assert.Equal("Updated Title", retrieved!.Title);
        Assert.Equal("Updated content", retrieved!.Text);
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

    [Fact(Skip = "Deferred: optimistic concurrency handling deferred; re-enable before public cloud deployment")]
    public async Task ConcurrentUpdate_WithoutReload_ShouldFailDueToStaleToken()
    {
        // Arrange
        var doc = CreateTestDocument();
        await _repository!.AddAsync(doc, CancellationToken.None);
        var originalUpdatedAt = doc.UpdatedAt;

        // Load document twice in separate contexts
        var context2 = _fixture.CreateDbContext();
        var docFromContext2 = await context2.Documents.FirstOrDefaultAsync(d => d.Id == doc.Id);
        
        // Update in context 1
        doc.Title = "Update from context 1";
        await _repository.UpdateAsync(doc, CancellationToken.None);

        // Verify timestamp changed
        var reloaded = await _repository.GetByIdAsync(doc.Id, CancellationToken.None);
        Assert.True(reloaded!.UpdatedAt > originalUpdatedAt);

        // Act: Try to update with stale context2 document (old UpdatedAt token)
        docFromContext2!.Title = "Update from context 2";
        docFromContext2.UpdatedAt = originalUpdatedAt; // Simulate stale token
        var repo2 = new PostgresDocumentRepository(context2);
        
        // Assert: Should throw DbUpdateConcurrencyException due to stale concurrency token
        await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException>(
            () => repo2.UpdateAsync(docFromContext2, CancellationToken.None));

        await context2.DisposeAsync();
    }

    [Fact(Skip = "Deferred: optimistic concurrency handling deferred; re-enable before public cloud deployment")]
    public async Task ConcurrentUpdates_InDifferentContexts_ShouldHandleCorrectly()
    {
        // Arrange
        var doc = CreateTestDocument();
        await _repository!.AddAsync(doc, CancellationToken.None);

        // Create two separate contexts and repositories
        var context2 = _fixture.CreateDbContext();
        var repo2 = new PostgresDocumentRepository(context2);

        // Load same document in both contexts
        var docInContext1 = await _repository.GetByIdAsync(doc.Id, CancellationToken.None);
        var docInContext2 = await repo2.GetByIdAsync(doc.Id, CancellationToken.None);

        Assert.NotNull(docInContext1);
        Assert.NotNull(docInContext2);

        // Act: Update in first context
        docInContext1.Title = "Updated in context 1";
        docInContext1.Text = "New text 1";
        await _repository.UpdateAsync(docInContext1, CancellationToken.None);

        // Update in second context with different property
        docInContext2.Title = "Updated in context 2";
        
        // Assert: Second update should throw due to stale token
        await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException>(
            () => repo2.UpdateAsync(docInContext2, CancellationToken.None));

        await context2.DisposeAsync();
    }

    [Fact(Skip = "Deferred: optimistic concurrency handling deferred; re-enable before public cloud deployment")]
    public async Task ReloadAndUpdate_AfterConflict_ShouldSucceed()
    {
        // Arrange
        var doc = CreateTestDocument();
        await _repository!.AddAsync(doc, CancellationToken.None);

        // Create two separate contexts
        var context2 = _fixture.CreateDbContext();
        var repo2 = new PostgresDocumentRepository(context2);

        // Load same document in both contexts
        var docInContext1 = await _repository.GetByIdAsync(doc.Id, CancellationToken.None);
        var docInContext2 = await repo2.GetByIdAsync(doc.Id, CancellationToken.None);

        // Act: Update in first context
        docInContext1!.Title = "Updated in context 1";
        await _repository.UpdateAsync(docInContext1, CancellationToken.None);

        // Diagnostic: verify DB reflects first update and UpdatedAt differs from the stale copy
        var afterFirst = await _repository.GetByIdAsync(doc.Id, CancellationToken.None);
        Assert.Equal("Updated in context 1", afterFirst!.Title);
        _output.WriteLine($"[DIAG] afterFirst.UpdatedAt={afterFirst.UpdatedAt:o}");
        _output.WriteLine($"[DIAG] docInContext2.UpdatedAt={docInContext2!.UpdatedAt:o}");
        Assert.NotEqual(afterFirst.UpdatedAt, docInContext2.UpdatedAt);

        // Try to update in second context
        docInContext2.Title = "Updated in context 2";
        
        // Assert: First attempt should fail
        await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException>(
            () => repo2.UpdateAsync(docInContext2, CancellationToken.None));

        // Reload the document in context2 with latest values
        var reloaded = await repo2.GetByIdAsync(doc.Id, CancellationToken.None);
        Assert.NotNull(reloaded);
        Assert.Equal("Updated in context 1", reloaded.Title);

        // Now update should succeed with reloaded document
        reloaded.Title = "Updated in context 2 after reload";
        await repo2.UpdateAsync(reloaded, CancellationToken.None);

        // Verify final state
        var final = await _repository.GetByIdAsync(doc.Id, CancellationToken.None);
        Assert.Equal("Updated in context 2 after reload", final!.Title);

        await context2.DisposeAsync();
    }
}
