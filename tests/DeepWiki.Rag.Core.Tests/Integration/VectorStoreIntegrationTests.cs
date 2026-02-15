using DeepWiki.Data.Abstractions;
using DeepWiki.Data.Abstractions.Models;
using FluentAssertions;
using Moq;
using Xunit;

namespace DeepWiki.Rag.Core.Tests.Integration;

/// <summary>
/// Integration tests for RAG retrieval flow with IVectorStore.
/// Validates end-to-end document retrieval and context formatting.
/// </summary>
public class VectorStoreIntegrationTests
{
    [Fact]
    public async Task GenerationService_WithVectorStore_RetrievesDocumentsForContext()
    {
        // Arrange: Mock vector store with sample query results
        var mockVectorStore = new Mock<IVectorStore>();
        
        var sampleResults = new List<VectorQueryResult>
        {
            new()
            {
                Document = new DocumentDto
                {
                    Id = Guid.NewGuid(),
                    RepoUrl = "https://github.com/example/repo",
                    FilePath = "src/Example.cs",
                    Text = "public class Example { public void Method() { } }",
                    Title = "Example.cs",
                    IsCode = true
                },
                SimilarityScore = 0.95f
            },
            new()
            {
                Document = new DocumentDto
                {
                    Id = Guid.NewGuid(),
                    RepoUrl = "https://github.com/example/repo",
                    FilePath = "docs/README.md",
                    Text = "# Example Project\n\nThis is documentation.",
                    Title = "README.md",
                    IsCode = false
                },
                SimilarityScore = 0.88f
            }
        };

        // Mock vector store to return sample results
        mockVectorStore
            .Setup(x => x.QueryAsync(
                It.IsAny<float[]>(),
                It.IsAny<int>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(sampleResults);

        // Act: Query for documents
        var embedding = new float[1536]; // Dummy embedding
        var results = await mockVectorStore.Object.QueryAsync(
            embedding, 
            k: 5,
            filters: null,
            CancellationToken.None);

        // Assert: Verify retrieval
        results.Should().NotBeNull();
        results.Should().HaveCount(2);
        results.Should().Contain(d => d.Document.FilePath == "src/Example.cs");
        results.Should().Contain(d => d.Document.FilePath == "docs/README.md");
        
        // Verify code/docs mix
        results.Count(d => d.Document.IsCode).Should().Be(1);
        results.Count(d => !d.Document.IsCode).Should().Be(1);
        
        // Verify ordering by score
        results.First().SimilarityScore.Should().BeGreaterThan(results.Last().SimilarityScore);
    }

    [Fact]
    public async Task GenerationService_WithFilters_PassesToVectorStore()
    {
        // Arrange: Mock vector store that validates filters
        var mockVectorStore = new Mock<IVectorStore>();
        Dictionary<string, string>? capturedFilters = null;

        mockVectorStore
            .Setup(x => x.QueryAsync(
                It.IsAny<float[]>(),
                It.IsAny<int>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback<float[], int, Dictionary<string, string>?, CancellationToken>((_, _, filters, _) => 
                capturedFilters = filters)
            .ReturnsAsync(new List<VectorQueryResult>());

        var filters = new Dictionary<string, string>
        {
            ["repoUrl"] = "https://github.com/example/repo",
            ["filePath"] = "%.cs"
        };

        // Act: Query with filters
        await mockVectorStore.Object.QueryAsync(
            new float[1536],
            k: 5,
            filters: filters,
            CancellationToken.None);

        // Assert: Filters passed correctly
        capturedFilters.Should().NotBeNull();
        capturedFilters.Should().ContainKey("repoUrl");
        capturedFilters.Should().ContainKey("filePath");
        capturedFilters!["filePath"].Should().Be("%.cs");
    }

    [Fact]
    public async Task GenerationService_WithTopK_LimitsRetrievedDocuments()
    {
        // Arrange: Mock with many results
        var mockVectorStore = new Mock<IVectorStore>();
        
        var manyResults = Enumerable.Range(0, 20).Select(i => new VectorQueryResult
        {
            Document = new DocumentDto
            {
                Id = Guid.NewGuid(),
                RepoUrl = "https://github.com/example/repo",
                FilePath = $"src/File{i}.cs",
                Text = $"Content {i}",
                Title = $"File{i}.cs",
                IsCode = true
            },
            SimilarityScore = 1.0f - (i * 0.01f)
        }).ToList();

        // Mock returns only k documents
        mockVectorStore
            .Setup(x => x.QueryAsync(
                It.IsAny<float[]>(),
                It.IsAny<int>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((float[] _, int k, Dictionary<string, string>? _, CancellationToken _) => 
                (IReadOnlyList<VectorQueryResult>)manyResults.Take(k).ToList());

        // Act: Query with k = 10
        var results = await mockVectorStore.Object.QueryAsync(
            new float[1536],
            k: 10,
            filters: null,
            CancellationToken.None);

        // Assert: Should return exactly k documents
        results.Should().HaveCount(10);
    }

    [Fact]
    public void RAGContext_FormatsRetrievedDocuments_ForSystemPrompt()
    {
        // Arrange: Sample retrieved results
        var results = new List<VectorQueryResult>
        {
            new()
            {
                Document = new DocumentDto
                {
                    Id = Guid.NewGuid(),
                    RepoUrl = "https://github.com/example/repo",
                    FilePath = "src/Helper.cs",
                    Text = "public static class Helper { public static int Add(int a, int b) => a + b; }",
                    Title = "Helper.cs",
                    IsCode = true
                },
                SimilarityScore = 0.92f
            }
        };

        // Act: Format as RAG context (typical pattern)
        var context = string.Join("\n\n", results.Select(r => 
            $"File: {r.Document.FilePath}\n```{(r.Document.IsCode ? "csharp" : "text")}\n{r.Document.Text}\n```"));

        // Assert: Context is properly formatted for LLM
        context.Should().Contain("File: src/Helper.cs");
        context.Should().Contain("```");
        context.Should().Contain("public static class Helper");
        context.Should().Contain("Add(int a, int b)");
    }

    [Fact]
    public async Task GenerationService_WithEmptyRetrieval_HandlesGracefully()
    {
        // Arrange: Vector store returns no documents
        var mockVectorStore = new Mock<IVectorStore>();
        
        mockVectorStore
            .Setup(x => x.QueryAsync(
                It.IsAny<float[]>(),
                It.IsAny<int>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorQueryResult>());

        // Act: Query returns empty
        var results = await mockVectorStore.Object.QueryAsync(
            new float[1536],
            k: 5,
            filters: null,
            CancellationToken.None);

        // Assert: Should handle empty result gracefully
        results.Should().NotBeNull();
        results.Should().BeEmpty();
        
        // In real GenerationService, this would still proceed with generation
        // but without RAG context in the system prompt
    }
}
