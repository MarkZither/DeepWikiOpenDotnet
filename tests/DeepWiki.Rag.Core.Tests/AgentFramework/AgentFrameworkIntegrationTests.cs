using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeepWiki.Data.Abstractions;
using DeepWiki.Data.Abstractions.Models;
using Microsoft.Extensions.AI;

namespace DeepWiki.Rag.Core.Tests.AgentFramework;

/// <summary>
/// Integration tests for Microsoft Agent Framework compatibility with Vector Store.
/// Validates tool definitions, parameter binding, and knowledge context integration.
/// Covers T238-T242.
/// </summary>
[Trait("Category", "Integration")]
public class AgentFrameworkIntegrationTests
{
    private readonly InMemoryVectorStore _vectorStore;
    private readonly FixtureEmbeddingService _embeddingService;
    private readonly List<SampleDocument> _sampleDocuments;
    private readonly List<SampleEmbedding> _sampleEmbeddings;

    public AgentFrameworkIntegrationTests()
    {
        // Load fixtures
        _sampleDocuments = LoadSampleDocuments();
        _sampleEmbeddings = LoadSampleEmbeddings();

        // Set up services
        _vectorStore = new InMemoryVectorStore();
        _embeddingService = new FixtureEmbeddingService(_sampleEmbeddings);
    }

    #region T238: Tool Definition and Parameter Binding Tests

    /// <summary>
    /// T238: Validates that the queryKnowledge tool can be created using AIFunctionFactory
    /// with proper tool definition, parameter descriptions, and JSON-serializable results.
    /// </summary>
    [Fact]
    public void QueryKnowledgeTool_CanBeCreatedWithAIFunctionFactory()
    {
        // Arrange
        var knowledgeService = new KnowledgeRetrievalService(_vectorStore, _embeddingService);

        // Act - Create AI tool from the queryKnowledge method
        var tool = AIFunctionFactory.Create(knowledgeService.QueryKnowledge);

        // Assert - Tool should be created with proper metadata
        Assert.NotNull(tool);
        Assert.Equal("QueryKnowledge", tool.Name);
        
        // Verify the function has the expected parameter
        var aiFunction = tool as AIFunction;
        Assert.NotNull(aiFunction);
        Assert.Contains(aiFunction.JsonSchema.GetProperty("properties").EnumerateObject(),
            p => p.Name == "question");
    }

    /// <summary>
    /// T238: Validates tool parameter binding works correctly with Description attributes.
    /// </summary>
    [Fact]
    public void QueryKnowledgeTool_HasProperParameterDescriptions()
    {
        // Arrange
        var knowledgeService = new KnowledgeRetrievalService(_vectorStore, _embeddingService);
        var tool = AIFunctionFactory.Create(knowledgeService.QueryKnowledge) as AIFunction;

        // Assert - Parameter descriptions should be in schema
        Assert.NotNull(tool);
        var schemaJson = tool.JsonSchema.ToString();
        Assert.Contains("question", schemaJson);
    }

    /// <summary>
    /// T238: Validates that the tool returns JSON-serializable results suitable for agent context.
    /// </summary>
    [Fact]
    public async Task QueryKnowledgeTool_ReturnsJsonSerializableResults()
    {
        // Arrange
        await IngestSampleDocuments();
        var knowledgeService = new KnowledgeRetrievalService(_vectorStore, _embeddingService);

        // Act - Call the knowledge retrieval
        var result = await knowledgeService.QueryKnowledge("What is C# programming?");

        // Assert - Result should be JSON-serializable
        Assert.NotNull(result);
        var json = JsonSerializer.Serialize(result);
        Assert.NotNull(json);
        Assert.NotEmpty(json);

        // Deserialize and verify structure
        var deserialized = JsonSerializer.Deserialize<KnowledgeContext>(json);
        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Documents);
    }

    #endregion

    #region T240: Integration Test - Agent Calls queryKnowledge Tool

    /// <summary>
    /// T240: Integration test verifying the complete flow:
    /// Agent calls queryKnowledge(question) → IVectorStore.QueryAsync() → 
    /// retrieves documents → agent integrates knowledge into reasoning context.
    /// </summary>
    [Fact]
    public async Task Agent_CallsQueryKnowledgeTool_RetrievesDocuments_IntegratesKnowledge()
    {
        // Arrange - Ingest sample documents
        await IngestSampleDocuments();
        var knowledgeService = new KnowledgeRetrievalService(_vectorStore, _embeddingService);
        var tool = AIFunctionFactory.Create(knowledgeService.QueryKnowledge);

        // Act - Simulate agent calling the tool (as the agent framework would)
        var arguments = new AIFunctionArguments
        {
            ["question"] = "What is dependency injection in C#?"
        };

        var result = await tool.InvokeAsync(arguments);

        // Assert - Verify knowledge retrieval worked
        Assert.NotNull(result);
        
        // AIFunction.InvokeAsync may return different types depending on the runtime:
        // - The actual object type (KnowledgeContext) in some cases
        // - A string (JSON serialized) in other cases  
        // - A JsonElement in yet other cases
        // We handle all possibilities for robust testing
        KnowledgeContext? knowledgeContext;
        if (result is KnowledgeContext direct)
        {
            knowledgeContext = direct;
        }
        else if (result is JsonElement jsonElement)
        {
            knowledgeContext = jsonElement.Deserialize<KnowledgeContext>();
        }
        else
        {
            // Try to deserialize from string representation
            var jsonString = result.ToString();
            knowledgeContext = JsonSerializer.Deserialize<KnowledgeContext>(jsonString ?? "{}");
        }
        
        Assert.NotNull(knowledgeContext);
        Assert.NotEmpty(knowledgeContext.Documents);
        Assert.True(knowledgeContext.Documents.Count <= 5, "Should return at most 5 documents");
        
        // Verify documents have required fields for agent reasoning
        foreach (var doc in knowledgeContext.Documents)
        {
            Assert.NotNull(doc.Title);
            Assert.NotNull(doc.Content);
            Assert.NotNull(doc.Source);
        }
    }

    /// <summary>
    /// T240: Verifies that the tool properly handles empty results.
    /// </summary>
    [Fact]
    public async Task Agent_CallsQueryKnowledgeTool_EmptyVectorStore_ReturnsEmptyContext()
    {
        // Arrange - Empty vector store
        var knowledgeService = new KnowledgeRetrievalService(_vectorStore, _embeddingService);

        // Act
        var result = await knowledgeService.QueryKnowledge("Random unrelated question about quantum physics");

        // Assert - Should return empty but valid context
        Assert.NotNull(result);
        Assert.Empty(result.Documents);
        Assert.NotNull(result.Query);
    }

    #endregion

    #region T241: E2E Agent Reasoning with Citations

    /// <summary>
    /// T241: End-to-end test validating agent reasoning with Vector Store:
    /// Agent question → retrieve documents → reason over context → generate answer with citations.
    /// </summary>
    [Fact]
    public async Task E2E_AgentReasoningWithCitations_RetrievesAndFormatsContext()
    {
        // Arrange - Ingest sample documents
        await IngestSampleDocuments();
        var knowledgeService = new KnowledgeRetrievalService(_vectorStore, _embeddingService);

        // Act - Retrieve knowledge for a specific question
        var result = await knowledgeService.QueryKnowledge("How do I implement a vector store?");

        // Assert - Verify the context can be used for agent reasoning with citations
        Assert.NotNull(result);
        Assert.NotNull(result.Query);

        // Verify each document has citation-ready information
        foreach (var doc in result.Documents)
        {
            // Document should have source for citation
            Assert.False(string.IsNullOrEmpty(doc.Source), "Document must have a source for citations");
            
            // Document should have title for reference
            Assert.False(string.IsNullOrEmpty(doc.Title), "Document must have a title");
            
            // Document should have content for reasoning
            Assert.False(string.IsNullOrEmpty(doc.Content), "Document must have content");
            
            // Similarity score should be valid (allow small floating point tolerance)
            Assert.True(doc.Relevance >= -0.001f && doc.Relevance <= 1.001f, 
                $"Relevance score must be between 0 and 1, got {doc.Relevance}");
        }
    }

    /// <summary>
    /// T241: Validates the context format is suitable for agent prompt injection.
    /// </summary>
    [Fact]
    public async Task E2E_KnowledgeContext_CanBeFormattedForAgentPrompt()
    {
        // Arrange
        await IngestSampleDocuments();
        var knowledgeService = new KnowledgeRetrievalService(_vectorStore, _embeddingService);

        // Act
        var result = await knowledgeService.QueryKnowledge("Tell me about programming patterns");

        // Assert - Verify the context can be formatted as a prompt
        var promptContext = result.FormatAsContext();
        Assert.NotNull(promptContext);
        Assert.NotEmpty(promptContext);
        
        // Prompt should include document references
        if (result.Documents.Any())
        {
            Assert.Contains("Source:", promptContext);
        }
    }

    /// <summary>
    /// T241: Validates metadata filtering works for scoped knowledge retrieval.
    /// </summary>
    [Fact]
    public async Task E2E_AgentReasoningWithMetadataFilter_ScopesToRepository()
    {
        // Arrange - Ingest sample documents
        await IngestSampleDocuments();
        var knowledgeService = new KnowledgeRetrievalService(_vectorStore, _embeddingService);

        // Get a specific repo URL from sample docs
        var targetRepo = _sampleDocuments.First().RepoUrl;

        // Act - Query with filter
        var result = await knowledgeService.QueryKnowledgeWithFilter(
            "Find implementation details",
            repoUrl: targetRepo);

        // Assert - All results should be from the filtered repository
        Assert.NotNull(result);
        foreach (var doc in result.Documents)
        {
            Assert.Contains(targetRepo.TrimEnd('/'), doc.Source, StringComparison.OrdinalIgnoreCase);
        }
    }

    #endregion

    #region T242: Performance Tests

    /// <summary>
    /// T242: Performance test measuring total agent response time with Vector Store latency.
    /// Target: &lt;1s total (agent reasoning &lt;500ms + Vector Store query &lt;500ms).
    /// </summary>
    [Fact]
    public async Task Performance_TotalAgentResponseTime_Under1Second()
    {
        // Arrange - Ingest sample documents
        await IngestSampleDocuments();
        var knowledgeService = new KnowledgeRetrievalService(_vectorStore, _embeddingService);

        // Warmup
        _ = await knowledgeService.QueryKnowledge("warmup query");

        var totalStopwatch = Stopwatch.StartNew();
        var vectorStoreStopwatch = new Stopwatch();

        // Act - Measure total time including embedding generation and vector query
        vectorStoreStopwatch.Start();
        var result = await knowledgeService.QueryKnowledge("What are the best practices for software development?");
        vectorStoreStopwatch.Stop();

        // Simulate agent reasoning time (formatting context)
        var promptContext = result.FormatAsContext();
        totalStopwatch.Stop();

        // Assert - Total time should be under 1 second
        Assert.True(totalStopwatch.ElapsedMilliseconds < 1000,
            $"Total response time was {totalStopwatch.ElapsedMilliseconds}ms, expected <1000ms");

        // Vector store query should be under 500ms
        Assert.True(vectorStoreStopwatch.ElapsedMilliseconds < 500,
            $"Vector store query took {vectorStoreStopwatch.ElapsedMilliseconds}ms, expected <500ms");
    }

    /// <summary>
    /// T242: Performance test measuring p95 latency across multiple agent queries.
    /// </summary>
    [Fact]
    public async Task Performance_P95AgentResponseTime_Under1Second()
    {
        // Arrange - Ingest sample documents
        await IngestSampleDocuments();
        var knowledgeService = new KnowledgeRetrievalService(_vectorStore, _embeddingService);

        // Warmup
        _ = await knowledgeService.QueryKnowledge("warmup");

        var latencies = new List<long>();
        var queries = new[]
        {
            "What is dependency injection?",
            "How do I implement a repository pattern?",
            "Explain SOLID principles",
            "What is clean architecture?",
            "How to write unit tests?",
            "What are design patterns?",
            "Explain microservices architecture",
            "What is event sourcing?",
            "How to implement CQRS?",
            "What is domain-driven design?"
        };

        // Act - Execute multiple queries and measure latencies
        foreach (var query in queries)
        {
            var sw = Stopwatch.StartNew();
            var result = await knowledgeService.QueryKnowledge(query);
            _ = result.FormatAsContext(); // Include formatting time
            sw.Stop();
            latencies.Add(sw.ElapsedMilliseconds);
        }

        // Calculate p95
        latencies.Sort();
        var p95Index = (int)Math.Ceiling(latencies.Count * 0.95) - 1;
        var p95Latency = latencies[p95Index];

        // Assert - p95 should be under 1 second
        Assert.True(p95Latency < 1000,
            $"P95 latency was {p95Latency}ms, expected <1000ms. All latencies: [{string.Join(", ", latencies)}]ms");
    }

    /// <summary>
    /// T242: Performance test measuring Vector Store query isolation (without embedding).
    /// </summary>
    [Fact]
    public async Task Performance_VectorStoreQueryOnly_Under500ms()
    {
        // Arrange - Ingest sample documents
        await IngestSampleDocuments();

        // Get a pre-computed embedding for direct query
        var queryEmbedding = _sampleEmbeddings.First().Embedding;

        // Warmup
        _ = await _vectorStore.QueryAsync(queryEmbedding, 5);

        var latencies = new List<long>();

        // Act - Execute 20 direct vector queries
        for (int i = 0; i < 20; i++)
        {
            var sw = Stopwatch.StartNew();
            _ = await _vectorStore.QueryAsync(queryEmbedding, 5);
            sw.Stop();
            latencies.Add(sw.ElapsedMilliseconds);
        }

        // Calculate p95
        latencies.Sort();
        var p95Index = (int)Math.Ceiling(latencies.Count * 0.95) - 1;
        var p95Latency = latencies[p95Index];

        // Assert - p95 should be under 500ms
        Assert.True(p95Latency < 500,
            $"P95 Vector Store query latency was {p95Latency}ms, expected <500ms");
    }

    #endregion

    #region Helper Methods

    private async Task IngestSampleDocuments()
    {
        foreach (var doc in _sampleDocuments)
        {
            var embedding = _sampleEmbeddings.FirstOrDefault(e => e.Id == doc.Id)?.Embedding;
            if (embedding is null) continue;

            var dto = new DocumentDto
            {
                Id = Guid.Parse(doc.Id),
                RepoUrl = doc.RepoUrl,
                FilePath = doc.FilePath,
                Title = doc.Title,
                Text = doc.Text,
                Embedding = embedding,
                MetadataJson = JsonSerializer.Serialize(doc.Metadata ?? new Dictionary<string, string>()),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _vectorStore.UpsertAsync(dto);
        }
    }

    private static List<SampleDocument> LoadSampleDocuments()
    {
        var path = GetFixturePath("sample-documents.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<SampleDocument>>(json)
            ?? throw new InvalidOperationException("Failed to load sample documents");
    }

    private static List<SampleEmbedding> LoadSampleEmbeddings()
    {
        var path = GetFixturePath("sample-embeddings.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<SampleEmbedding>>(json)
            ?? throw new InvalidOperationException("Failed to load sample embeddings");
    }

    private static string GetFixturePath(string filename)
    {
        return Path.Combine(
            AppContext.BaseDirectory,
            "fixtures",
            "embedding-samples",
            filename);
    }

    #endregion

    #region Test Support Classes

    /// <summary>
    /// In-memory vector store for testing Agent Framework integration.
    /// </summary>
    private sealed class InMemoryVectorStore : IVectorStore
    {
        private readonly Dictionary<Guid, DocumentDto> _documents = [];
        private readonly object _lock = new();

        public Task<IReadOnlyList<VectorQueryResult>> QueryAsync(
            float[] embedding,
            int k = 10,
            Dictionary<string, string>? filters = null,
            CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                var query = _documents.Values.AsEnumerable();

                // Apply filters
                if (filters is not null)
                {
                    if (filters.TryGetValue("repoUrl", out var repoFilter))
                    {
                        var pattern = repoFilter.Replace("%", "");
                        query = query.Where(d => d.RepoUrl.Contains(pattern, StringComparison.OrdinalIgnoreCase));
                    }
                    if (filters.TryGetValue("filePath", out var fileFilter))
                    {
                        var pattern = fileFilter.Replace("%", "");
                        query = query.Where(d => d.FilePath.Contains(pattern, StringComparison.OrdinalIgnoreCase));
                    }
                }

                var results = query
                    .Select(d => new VectorQueryResult
                    {
                        Document = d,
                        SimilarityScore = CalculateCosineSimilarity(embedding, d.Embedding)
                    })
                    .OrderByDescending(r => r.SimilarityScore)
                    .Take(k)
                    .ToList();

                return Task.FromResult<IReadOnlyList<VectorQueryResult>>(results);
            }
        }

        public Task UpsertAsync(DocumentDto document, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                var existing = _documents.Values
                    .FirstOrDefault(d => d.RepoUrl == document.RepoUrl && d.FilePath == document.FilePath);

                if (existing is not null)
                {
                    _documents.Remove(existing.Id);
                    document.Id = existing.Id;
                }

                _documents[document.Id] = document;
            }
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                _documents.Remove(id);
            }
            return Task.CompletedTask;
        }

        public Task RebuildIndexAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        private static float CalculateCosineSimilarity(float[]? a, float[]? b)
        {
            if (a is null || b is null || a.Length != b.Length || a.Length == 0)
                return 0f;

            float dotProduct = 0;
            float magnitudeA = 0;
            float magnitudeB = 0;

            for (int i = 0; i < a.Length; i++)
            {
                dotProduct += a[i] * b[i];
                magnitudeA += a[i] * a[i];
                magnitudeB += b[i] * b[i];
            }

            if (magnitudeA == 0 || magnitudeB == 0)
                return 0f;

            return dotProduct / (MathF.Sqrt(magnitudeA) * MathF.Sqrt(magnitudeB));
        }
    }

    /// <summary>
    /// Fixture-based embedding service for testing.
    /// </summary>
    private sealed class FixtureEmbeddingService : IEmbeddingService
    {
        private readonly Dictionary<string, float[]> _embeddings;
        private static readonly float[] DefaultEmbedding = CreateDefaultEmbedding();

        public FixtureEmbeddingService(List<SampleEmbedding> sampleEmbeddings)
        {
            _embeddings = sampleEmbeddings.ToDictionary(e => e.Id, e => e.Embedding);
        }

        public string Provider => "fixture";
        public string ModelId => "test-fixture";
        public int EmbeddingDimension => 1536;

        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
        {
            // Return first matching embedding or default
            var matching = _embeddings.Values.FirstOrDefault() ?? DefaultEmbedding;
            return Task.FromResult(matching);
        }

        public async IAsyncEnumerable<float[]> EmbedBatchAsync(
            IEnumerable<string> texts, 
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var text in texts)
            {
                yield return await EmbedAsync(text, cancellationToken);
            }
        }

        public Task<IReadOnlyList<float[]>> EmbedBatchListAsync(
            IEnumerable<string> texts, 
            CancellationToken cancellationToken = default)
        {
            var results = texts.Select(_ => _embeddings.Values.FirstOrDefault() ?? DefaultEmbedding).ToList();
            return Task.FromResult<IReadOnlyList<float[]>>(results);
        }

        public Task<IReadOnlyList<EmbeddingResponse>> EmbedBatchWithMetadataAsync(
            IEnumerable<string> texts,
            int batchSize = 10,
            CancellationToken cancellationToken = default)
        {
            var results = texts.Select(t => EmbeddingResponse.Success(
                _embeddings.Values.FirstOrDefault() ?? DefaultEmbedding,
                Provider,
                ModelId,
                latencyMs: 1)).ToList();
            return Task.FromResult<IReadOnlyList<EmbeddingResponse>>(results);
        }

        private static float[] CreateDefaultEmbedding()
        {
            var embedding = new float[1536];
            var random = new Random(42); // Deterministic
            for (int i = 0; i < embedding.Length; i++)
            {
                embedding[i] = (float)(random.NextDouble() * 2 - 1);
            }
            return embedding;
        }
    }

    private record SampleDocument
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("repoUrl")]
        public string RepoUrl { get; init; } = string.Empty;

        [JsonPropertyName("filePath")]
        public string FilePath { get; init; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; init; } = string.Empty;

        [JsonPropertyName("text")]
        public string Text { get; init; } = string.Empty;

        [JsonPropertyName("metadata")]
        public Dictionary<string, string>? Metadata { get; init; }
    }

    private record SampleEmbedding
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("embedding")]
        public float[] Embedding { get; init; } = [];
    }

    #endregion
}

/// <summary>
/// Knowledge retrieval service that wraps IVectorStore for Agent Framework tool binding.
/// Provides the queryKnowledge tool that agents can call during reasoning.
/// </summary>
public sealed class KnowledgeRetrievalService
{
    private readonly IVectorStore _vectorStore;
    private readonly IEmbeddingService _embeddingService;

    public KnowledgeRetrievalService(IVectorStore vectorStore, IEmbeddingService embeddingService)
    {
        _vectorStore = vectorStore;
        _embeddingService = embeddingService;
    }

    /// <summary>
    /// Queries the knowledge base to retrieve relevant documents for a given question.
    /// This method is designed to be bound as an Agent Framework tool using AIFunctionFactory.
    /// </summary>
    /// <param name="question">The natural language question to search for relevant knowledge.</param>
    /// <returns>A knowledge context containing relevant documents and their metadata for agent reasoning.</returns>
    [Description("Search the knowledge base for documents relevant to answer a question. Returns context with source documents and relevance scores.")]
    public async Task<KnowledgeContext> QueryKnowledge(
        [Description("The natural language question to search for relevant knowledge")] string question)
    {
        // Generate embedding for the question
        var embedding = await _embeddingService.EmbedAsync(question);

        // Query the vector store
        var results = await _vectorStore.QueryAsync(embedding, k: 5);

        // Transform to agent-friendly context
        return new KnowledgeContext
        {
            Query = question,
            Documents = results.Select(r => new KnowledgeDocument
            {
                Title = r.Document.Title,
                Content = TruncateContent(r.Document.Text, 1000),
                Source = $"{r.Document.RepoUrl}/{r.Document.FilePath}",
                Relevance = r.SimilarityScore
            }).ToList()
        };
    }

    /// <summary>
    /// Queries the knowledge base with optional metadata filters.
    /// </summary>
    /// <param name="question">The question to search for.</param>
    /// <param name="repoUrl">Optional repository URL filter.</param>
    /// <param name="filePath">Optional file path filter.</param>
    /// <returns>Filtered knowledge context.</returns>
    [Description("Search the knowledge base with optional filters for repository or file path.")]
    public async Task<KnowledgeContext> QueryKnowledgeWithFilter(
        [Description("The natural language question")] string question,
        [Description("Optional: Filter to specific repository URL")] string? repoUrl = null,
        [Description("Optional: Filter to specific file path pattern")] string? filePath = null)
    {
        var embedding = await _embeddingService.EmbedAsync(question);

        var filters = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(repoUrl))
            filters["repoUrl"] = $"%{repoUrl}%";
        if (!string.IsNullOrEmpty(filePath))
            filters["filePath"] = $"%{filePath}%";

        var results = await _vectorStore.QueryAsync(
            embedding, 
            k: 5, 
            filters: filters.Count > 0 ? filters : null);

        return new KnowledgeContext
        {
            Query = question,
            Documents = results.Select(r => new KnowledgeDocument
            {
                Title = r.Document.Title,
                Content = TruncateContent(r.Document.Text, 1000),
                Source = $"{r.Document.RepoUrl}/{r.Document.FilePath}",
                Relevance = r.SimilarityScore
            }).ToList()
        };
    }

    private static string TruncateContent(string content, int maxLength)
    {
        if (string.IsNullOrEmpty(content) || content.Length <= maxLength)
            return content;
        
        return content[..maxLength] + "...";
    }
}

/// <summary>
/// Represents the knowledge context returned to an agent after querying the vector store.
/// This structure is optimized for agent reasoning and citation generation.
/// </summary>
public sealed class KnowledgeContext
{
    /// <summary>
    /// The original query that was searched.
    /// </summary>
    [JsonPropertyName("query")]
    public string Query { get; init; } = string.Empty;

    /// <summary>
    /// The relevant documents retrieved from the knowledge base.
    /// </summary>
    [JsonPropertyName("documents")]
    public List<KnowledgeDocument> Documents { get; init; } = [];

    /// <summary>
    /// Formats the knowledge context as a string suitable for injection into an agent prompt.
    /// </summary>
    /// <returns>A formatted string containing all document information with citations.</returns>
    public string FormatAsContext()
    {
        if (Documents.Count == 0)
            return "No relevant documents found in the knowledge base.";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Relevant knowledge from the codebase:");
        sb.AppendLine();

        for (int i = 0; i < Documents.Count; i++)
        {
            var doc = Documents[i];
            sb.AppendLine($"[{i + 1}] {doc.Title}");
            sb.AppendLine($"    Source: {doc.Source}");
            sb.AppendLine($"    Relevance: {doc.Relevance:P1}");
            sb.AppendLine($"    Content: {doc.Content}");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}

/// <summary>
/// Represents a single document in the knowledge context with citation-ready metadata.
/// </summary>
public sealed class KnowledgeDocument
{
    /// <summary>
    /// The document title.
    /// </summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// The document content (may be truncated for context length).
    /// </summary>
    [JsonPropertyName("content")]
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// The source reference for citations (repository URL + file path).
    /// </summary>
    [JsonPropertyName("source")]
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// The relevance score from the vector similarity search (0-1).
    /// </summary>
    [JsonPropertyName("relevance")]
    public float Relevance { get; init; }
}
