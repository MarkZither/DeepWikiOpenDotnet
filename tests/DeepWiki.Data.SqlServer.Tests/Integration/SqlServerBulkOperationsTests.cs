using DeepWiki.Data.Entities;
using DeepWiki.Data.SqlServer.DbContexts;
using DeepWiki.Data.SqlServer.Tests.Fixtures;
using Microsoft.Data.SqlTypes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DeepWiki.Data.SqlServer.Tests.Integration;

/// <summary>
/// Integration tests for bulk operations on SQL Server vector store.
/// Tests high-volume document operations and transactional semantics.
/// </summary>
public class SqlServerBulkOperationsTests : IAsyncLifetime
{
    private readonly SqlServerFixture _fixture;

    public SqlServerBulkOperationsTests()
    {
        _fixture = new SqlServerFixture();
    }

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _fixture.DisposeAsync();
    }

    [Fact]
    public async Task BulkUpsert_100Documents_ShouldInsertAllInTransaction()
    {
        // Arrange
        var context = _fixture.CreateDbContext();
        var documents = GenerateTestDocuments(100);

        // Act
        context.Documents.AddRange(documents);
        var result = await context.SaveChangesAsync();

        // Assert
        Assert.Equal(100, result);
        var count = await context.Documents.CountAsync();
        Assert.Equal(100, count);
    }

    [Fact]
    public async Task BulkUpsert_WithDuplicateIds_ShouldFailOnSave()
    {
        // Arrange
        var context = _fixture.CreateDbContext();
        var id = Guid.NewGuid();

        // Create first document
        var doc1 = CreateDocument(id, "repo1", "file1.cs");
        context.Documents.Add(doc1);
        await context.SaveChangesAsync();

        // Create second document with same ID (different context to avoid tracking conflict)
        var context2 = _fixture.CreateDbContext();
        var doc2 = CreateDocument(id, "repo1", "file2.cs");
        context2.Documents.Add(doc2);

        // Act & Assert - duplicate ID should fail on insert
        var exception = await Assert.ThrowsAsync<Exception>(() => context2.SaveChangesAsync());
        Assert.NotNull(exception);
    }

    [Fact]
    public async Task ConcurrentUpdates_ShouldUpdateSuccessfully()
    {
        // Arrange
        var context1 = _fixture.CreateDbContext();
        var doc = CreateDocument(Guid.NewGuid(), "repo", "file.cs");
        context1.Documents.Add(doc);
        await context1.SaveChangesAsync();

        // Load same document in two different contexts
        var loaded1 = await context1.Documents.FindAsync(doc.Id);
        Assert.NotNull(loaded1);

        // Create new context for second read
        var context2 = _fixture.CreateDbContext();
        var loaded2 = await context2.Documents.FindAsync(doc.Id);
        Assert.NotNull(loaded2);

        // Modify and save in first context
        loaded1.Text = "Updated in context 1";
        await context1.SaveChangesAsync();

        // Act: Modify and save in second context
        // Without explicit concurrency checking, this should succeed (last-write-wins)
        loaded2.Text = "Updated in context 2";
        await context2.SaveChangesAsync();

        // Assert: Last write should persist
        var final = await _fixture.CreateDbContext().Documents.FindAsync(doc.Id);
        Assert.NotNull(final);
        Assert.Equal("Updated in context 2", final.Text);
    }

    [Fact]
    public async Task BulkDelete_ByRepository_ShouldRemoveOnlyMatching()
    {
        // Arrange
        var context = _fixture.CreateDbContext();
        var repo1 = "https://github.com/repo1";
        var repo2 = "https://github.com/repo2";

        var docs = new[]
        {
            CreateDocument(Guid.NewGuid(), repo1, "file1.cs"),
            CreateDocument(Guid.NewGuid(), repo1, "file2.cs"),
            CreateDocument(Guid.NewGuid(), repo2, "file3.cs")
        };

        context.Documents.AddRange(docs);
        await context.SaveChangesAsync();

        // Act
        var toDelete = context.Documents.Where(d => d.RepoUrl == repo1);
        context.Documents.RemoveRange(toDelete);
        var deleted = await context.SaveChangesAsync();

        // Assert
        Assert.Equal(2, deleted);
        var remaining = await context.Documents.ToListAsync();
        Assert.Single(remaining);
        Assert.Equal(repo2, remaining[0].RepoUrl);
    }

    [Fact]
    public async Task BulkInsert_VerifyEmbeddingPreserved()
    {
        // Arrange
        var context = _fixture.CreateDbContext();
        var embedding = CreateSampleEmbedding(1536);
        var doc = CreateDocument(Guid.NewGuid(), "repo", "file.cs");
        doc.Embedding = embedding;

        // Act
        context.Documents.Add(doc);
        await context.SaveChangesAsync();

        var retrieved = await context.Documents.FindAsync(doc.Id);

        // Assert
        Assert.NotNull(retrieved);
        Assert.True(retrieved.Embedding.HasValue);
        var retrievedEmbedding = retrieved.Embedding.Value;
        Assert.Equal(1536, retrievedEmbedding.Length);
        
        // Verify embedding content matches
        for (int i = 0; i < 1536; i++)
        {
            Assert.Equal(embedding.Span[i], retrievedEmbedding.Span[i], 5);
        }
    }

    /// <summary>
    /// Generate a batch of test documents with unique IDs and embeddings.
    /// </summary>
    private static List<DocumentEntity> GenerateTestDocuments(int count)
    {
        var documents = new List<DocumentEntity>();

        for (int i = 0; i < count; i++)
        {
            documents.Add(CreateDocument(
                Guid.NewGuid(),
                $"https://github.com/sample/repo-{i % 5}",
                $"src/File{i}.cs"
            ));
        }

        return documents;
    }

    /// <summary>
    /// Create a single test document with realistic data.
    /// </summary>
    private static DocumentEntity CreateDocument(Guid id, string repoUrl, string filePath)
    {
        return new DocumentEntity
        {
            Id = id,
            RepoUrl = repoUrl,
            FilePath = filePath,
            Title = $"Document: {filePath}",
            Text = $"Sample content for {filePath}. This is test data for integration testing.",
            Embedding = CreateSampleEmbedding(1536),
            FileType = Path.GetExtension(filePath)?.TrimStart('.') ?? "txt",
            IsCode = filePath.EndsWith(".cs") || filePath.EndsWith(".py"),
            IsImplementation = true,
            TokenCount = 50,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            MetadataJson = "{\"test\": true}"
        };
    }

    /// <summary>
    /// Create a normalized sample embedding (unit vector for cosine similarity).
    /// </summary>
    private static ReadOnlyMemory<float> CreateSampleEmbedding(int dimensions)
    {
        var embedding = new float[dimensions];
        var random = new Random();

        for (int i = 0; i < dimensions; i++)
        {
            embedding[i] = (float)(random.NextDouble() - 0.5);
        }

        // Normalize to unit vector
        var norm = (float)Math.Sqrt(embedding.Sum(x => x * x));
        if (norm > 0)
        {
            for (int i = 0; i < dimensions; i++)
            {
                embedding[i] /= norm;
            }
        }

        return new ReadOnlyMemory<float>(embedding);
    }
}
