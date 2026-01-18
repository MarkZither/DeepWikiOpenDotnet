using System;
using System.Collections.Generic;
using System.Text.Json;
using DeepWiki.Data.Entities;
using Xunit;

namespace DeepWiki.Data.Tests.Entities;

/// <summary>
/// Unit tests for DocumentEntity validation and serialization.
/// </summary>
public class DocumentEntityTests
{
    // Test data constants
    private const string TestRepoUrl = "https://github.com/test/repo";
    private const string TestFilePath = "src/test.cs";
    private const string TestTitle = "Test Document";
    private const string TestText = "This is test content";

    private DocumentEntity CreateTestEntity(
        string repoUrl = TestRepoUrl,
        string filePath = TestFilePath,
        string? title = TestTitle,
        string text = TestText,
        ReadOnlyMemory<float>? embedding = null)
    {
        return new DocumentEntity
        {
            RepoUrl = repoUrl,
            FilePath = filePath,
            Title = title,
            Text = text,
            Embedding = embedding
        };
    }

    #region Embedding Validation Tests

    [Fact]
    public void ValidateEmbedding_WithValidDimensions_DoesNotThrow()
    {
        // Arrange
        var entity = CreateTestEntity(embedding: new ReadOnlyMemory<float>(new float[1536]));

        // Act & Assert
        entity.ValidateEmbedding(); // Should not throw
    }

    [Fact]
    public void ValidateEmbedding_WithNullEmbedding_DoesNotThrow()
    {
        // Arrange
        var entity = CreateTestEntity(embedding: null);

        // Act & Assert
        entity.ValidateEmbedding(); // Should not throw
    }

    [Fact]
    public void ValidateEmbedding_WithTooFewDimensions_ThrowsArgumentException()
    {
        // Arrange
        var entity = CreateTestEntity(embedding: new float[1535]);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => entity.ValidateEmbedding());
        Assert.Contains("1536", exception.Message);
        Assert.Contains("1535", exception.Message);
    }

    [Fact]
    public void ValidateEmbedding_WithTooManyDimensions_ThrowsArgumentException()
    {
        // Arrange
        var entity = CreateTestEntity(embedding: new float[1537]);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => entity.ValidateEmbedding());
        Assert.Contains("1536", exception.Message);
        Assert.Contains("1537", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(512)]
    [InlineData(2048)]
    [InlineData(4096)]
    public void ValidateEmbedding_WithInvalidDimensions_ThrowsArgumentException(int dimensions)
    {
        // Arrange
        var entity = CreateTestEntity(embedding: new float[dimensions]);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => entity.ValidateEmbedding());
        Assert.Contains("1536", exception.Message);
        Assert.Equal(nameof(DocumentEntity.Embedding), exception.ParamName);
    }

    #endregion

    #region Required Properties Tests

    [Fact]
    public void RequiredProperties_MustBeSetDuringConstruction()
    {
        // Arrange & Act
        var entity = CreateTestEntity();

        // Assert
        Assert.NotNull(entity.RepoUrl);
        Assert.NotNull(entity.FilePath);
        Assert.NotNull(entity.Text);
        Assert.Equal(TestRepoUrl, entity.RepoUrl);
        Assert.Equal(TestFilePath, entity.FilePath);
        Assert.Equal(TestText, entity.Text);
    }

    [Fact]
    public void Id_IsGeneratedWhenNotSpecified()
    {
        // Arrange & Act
        var entity1 = CreateTestEntity();
        var entity2 = CreateTestEntity();

        // Assert
        Assert.NotEqual(Guid.Empty, entity1.Id);
        Assert.NotEqual(Guid.Empty, entity2.Id);
        Assert.NotEqual(entity1.Id, entity2.Id);
    }

    [Fact]
    public void Id_CanBeSetExplicitly()
    {
        // Arrange
        var id = Guid.NewGuid();
        var entity = new DocumentEntity
        {
            Id = id,
            RepoUrl = TestRepoUrl,
            FilePath = TestFilePath,
            Text = TestText
        };

        // Act & Assert
        Assert.Equal(id, entity.Id);
    }

    [Fact]
    public void CreatedAt_IsSetToUtcNow()
    {
        // Arrange
        var beforeCreation = DateTime.UtcNow;

        // Act
        var entity = CreateTestEntity();
        var afterCreation = DateTime.UtcNow;

        // Assert
        Assert.InRange(entity.CreatedAt, beforeCreation.AddMilliseconds(-1), afterCreation.AddMilliseconds(1));
    }

    [Fact]
    public void UpdatedAt_IsSetToUtcNow()
    {
        // Arrange
        var beforeCreation = DateTime.UtcNow;

        // Act
        var entity = CreateTestEntity();
        var afterCreation = DateTime.UtcNow;

        // Assert
        Assert.InRange(entity.UpdatedAt, beforeCreation.AddMilliseconds(-1), afterCreation.AddMilliseconds(1));
    }

    [Fact]
    public void BoolProperties_DefaultToFalse()
    {
        // Arrange & Act
        var entity = CreateTestEntity();

        // Assert
        Assert.False(entity.IsCode);
        Assert.False(entity.IsImplementation);
    }

    [Fact]
    public void TokenCount_DefaultsToZero()
    {
        // Arrange & Act
        var entity = CreateTestEntity();

        // Assert
        Assert.Equal(0, entity.TokenCount);
    }

    #endregion

    #region String Property Constraints Tests

    [Fact]
    public void RepoUrl_CanContainMaxLength()
    {
        // Arrange
        var maxLengthRepoUrl = new string('a', 2000);
        var entity = new DocumentEntity
        {
            RepoUrl = maxLengthRepoUrl,
            FilePath = TestFilePath,
            Text = TestText
        };

        // Act & Assert
        Assert.Equal(maxLengthRepoUrl, entity.RepoUrl);
    }

    [Fact]
    public void FilePath_CanContainMaxLength()
    {
        // Arrange
        var maxLengthFilePath = new string('a', 1000);
        var entity = new DocumentEntity
        {
            RepoUrl = TestRepoUrl,
            FilePath = maxLengthFilePath,
            Text = TestText
        };

        // Act & Assert
        Assert.Equal(maxLengthFilePath, entity.FilePath);
    }

    [Fact]
    public void Title_IsOptional()
    {
        // Arrange & Act
        var entity = CreateTestEntity(title: null);

        // Assert
        Assert.Null(entity.Title);
    }

    [Fact]
    public void Title_CanContainMaxLength()
    {
        // Arrange
        var maxLengthTitle = new string('a', 500);
        var entity = CreateTestEntity(title: maxLengthTitle);

        // Act & Assert
        Assert.Equal(maxLengthTitle, entity.Title);
    }

    [Fact]
    public void FileType_IsOptional()
    {
        // Arrange & Act
        var entity = CreateTestEntity();

        // Assert
        Assert.Null(entity.FileType);
    }

    #endregion

    #region Metadata JSON Serialization Tests

    [Fact]
    public void MetadataJson_CanStoreSimpleObject()
    {
        // Arrange
        var metadata = new { version = "1.0", author = "test" };
        var json = JsonSerializer.Serialize(metadata);
        var entity = CreateTestEntity();
        entity.MetadataJson = json;

        // Act
        var deserialized = JsonSerializer.Deserialize<Dictionary<string, object>>(entity.MetadataJson);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("1.0", deserialized["version"].ToString());
        Assert.Equal("test", deserialized["author"].ToString());
    }

    [Fact]
    public void MetadataJson_CanStoreComplexNestedObject()
    {
        // Arrange
        var metadata = new
        {
            version = "1.0",
            nested = new { level1 = new { level2 = "deep value" } }
        };
        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var entity = CreateTestEntity();
        entity.MetadataJson = json;

        // Act
        var deserialized = JsonSerializer.Deserialize<JsonElement>(entity.MetadataJson);

        // Assert
        Assert.True(deserialized.TryGetProperty("nested", out var nested));
        Assert.True(nested.TryGetProperty("level1", out var level1));
        Assert.True(level1.TryGetProperty("level2", out var level2));
        Assert.Equal("deep value", level2.GetString());
    }

    [Fact]
    public void MetadataJson_RoundTripPreservesData()
    {
        // Arrange
        var originalData = new Dictionary<string, string> { ["key1"] = "value1", ["key2"] = "value2" };
        var json = JsonSerializer.Serialize(originalData);
        var entity = CreateTestEntity();
        entity.MetadataJson = json;

        // Act
        var deserializedJson = entity.MetadataJson;
        var resultData = JsonSerializer.Deserialize<Dictionary<string, string>>(deserializedJson);

        // Assert
        Assert.NotNull(resultData);
        Assert.Equal(originalData.Count, resultData.Count);
        Assert.Equal(originalData["key1"], resultData["key1"]);
        Assert.Equal(originalData["key2"], resultData["key2"]);
    }

    [Fact]
    public void MetadataJson_IsOptional()
    {
        // Arrange & Act
        var entity = CreateTestEntity();

        // Assert
        Assert.Null(entity.MetadataJson);
    }

    [Fact]
    public void MetadataJson_CanContainEmptyObject()
    {
        // Arrange
        var json = "{}";
        var entity = CreateTestEntity();
        entity.MetadataJson = json;

        // Act
        var deserialized = JsonSerializer.Deserialize<Dictionary<string, object>>(entity.MetadataJson);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Empty(deserialized);
    }

    #endregion

    #region Entity Complete Test

    [Fact]
    public void CreateValidDocument_WithAllProperties_Succeeds()
    {
        // Arrange
        var embedding = new float[1536];
        for (int i = 0; i < 1536; i++)
        {
            embedding[i] = 0.5f;
        }

        var metadata = new { version = "1.0", customField = 42 };
        var metadataJson = JsonSerializer.Serialize(metadata);

        // Act
        var entity = new DocumentEntity
        {
            Id = Guid.NewGuid(),
            RepoUrl = "https://github.com/org/repo",
            FilePath = "src/Program.cs",
            Title = "Program",
            Text = "using System; class Program { }",
            FileType = "cs",
            IsCode = true,
            IsImplementation = true,
            TokenCount = 42,
            MetadataJson = metadataJson,
            Embedding = new ReadOnlyMemory<float>(embedding)
        };

        // Assert
        Assert.NotNull(entity);
        Assert.NotEqual(Guid.Empty, entity.Id);
        Assert.Equal("https://github.com/org/repo", entity.RepoUrl);
        Assert.Equal("src/Program.cs", entity.FilePath);
        Assert.Equal("Program", entity.Title);
        Assert.Equal("using System; class Program { }", entity.Text);
        Assert.Equal("cs", entity.FileType);
        Assert.True(entity.IsCode);
        Assert.True(entity.IsImplementation);
        Assert.Equal(42, entity.TokenCount);
        Assert.Equal(metadataJson, entity.MetadataJson);
        Assert.NotNull(entity.Embedding);
        Assert.Equal(1536, entity.Embedding.Value.Length);
        entity.ValidateEmbedding(); // Should not throw
    }

    [Fact]
    public void CreateMinimalValidDocument_WithOnlyRequiredProperties_Succeeds()
    {
        // Arrange & Act
        var entity = new DocumentEntity
        {
            RepoUrl = "https://github.com/org/repo",
            FilePath = "src/Program.cs",
            Text = "using System; class Program { }"
        };

        // Assert
        Assert.NotNull(entity);
        Assert.NotEqual(Guid.Empty, entity.Id);
        Assert.Equal("https://github.com/org/repo", entity.RepoUrl);
        Assert.Equal("src/Program.cs", entity.FilePath);
        Assert.Equal("using System; class Program { }", entity.Text);
        Assert.Null(entity.Title);
        Assert.Null(entity.FileType);
        Assert.Null(entity.Embedding);
        Assert.Null(entity.MetadataJson);
        Assert.False(entity.IsCode);
        Assert.False(entity.IsImplementation);
        Assert.Equal(0, entity.TokenCount);
        Assert.NotEqual(default(DateTime), entity.CreatedAt);
        Assert.NotEqual(default(DateTime), entity.UpdatedAt);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void NegativeTokenCount_CanBeSet()
    {
        // Arrange & Act
        var entity = CreateTestEntity();
        entity.TokenCount = -1;

        // Assert
        Assert.Equal(-1, entity.TokenCount);
    }

    [Fact]
    public void LargeTokenCount_CanBeSet()
    {
        // Arrange & Act
        var entity = CreateTestEntity();
        entity.TokenCount = int.MaxValue;

        // Assert
        Assert.Equal(int.MaxValue, entity.TokenCount);
    }

    [Fact]
    public void UpdatedAt_CanBeModifiedAfterCreation()
    {
        // Arrange
        var entity = CreateTestEntity();
        var originalUpdatedAt = entity.UpdatedAt;
        var newUpdatedAt = DateTime.UtcNow.AddMinutes(1);

        // Act
        entity.UpdatedAt = newUpdatedAt;

        // Assert
        Assert.NotEqual(originalUpdatedAt, entity.UpdatedAt);
        Assert.Equal(newUpdatedAt, entity.UpdatedAt);
    }

    [Fact]
    public void EachNewEntity_HasUniqueId()
    {
        // Arrange & Act
        var entities = new DocumentEntity[10];
        for (int i = 0; i < 10; i++)
        {
            entities[i] = CreateTestEntity();
        }

        // Assert - All IDs should be unique
        var uniqueIds = new HashSet<Guid>();
        foreach (var entity in entities)
        {
            Assert.True(uniqueIds.Add(entity.Id), $"Duplicate ID found: {entity.Id}");
        }
    }

    #endregion
}
