using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeepWiki.Data.Abstractions;
using DeepWiki.Data.Abstractions.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DeepWiki.Examples;

/// <summary>
/// Example demonstrating how to create a Microsoft Agent Framework agent with knowledge retrieval
/// capabilities using the Vector Store Service.
/// 
/// This example shows:
/// 1. Creating a KnowledgeRetrievalService that wraps IVectorStore and IEmbeddingService
/// 2. Defining the queryKnowledge tool with proper descriptions for agent reasoning
/// 3. Registering the tool with an Agent Framework agent
/// 4. Agent invoking the tool during reasoning to retrieve relevant context
/// 
/// Prerequisites:
/// - Configure IVectorStore and IEmbeddingService in DI (see DIRegistrationExample.cs)
/// - Ingest documents using IDocumentIngestionService
/// 
/// Usage:
/// 1. Get services from DI container
/// 2. Create KnowledgeRetrievalService
/// 3. Register queryKnowledge tool with your agent
/// 4. Agent will automatically call the tool when it needs knowledge context
/// </summary>
public static class AgentWithKnowledgeRetrieval
{
    /// <summary>
    /// Creates a sample agent with knowledge retrieval capability.
    /// </summary>
    /// <param name="serviceProvider">The DI service provider with IVectorStore and IEmbeddingService registered.</param>
    /// <returns>An AIFunction that can be used as a tool in the agent framework.</returns>
    public static AIFunction CreateKnowledgeTool(IServiceProvider serviceProvider)
    {
        var vectorStore = serviceProvider.GetRequiredService<IVectorStore>();
        var embeddingService = serviceProvider.GetRequiredService<IEmbeddingService>();
        
        var knowledgeService = new KnowledgeRetrievalService(vectorStore, embeddingService);
        
        // Create the AI tool using AIFunctionFactory
        // The Description attribute on the method will be used for agent reasoning
        return AIFunctionFactory.Create(knowledgeService.QueryKnowledge);
    }

    /// <summary>
    /// Creates all knowledge-related tools for comprehensive agent knowledge access.
    /// </summary>
    /// <param name="serviceProvider">The DI service provider.</param>
    /// <returns>List of AI tools for knowledge retrieval.</returns>
    public static IList<AITool> CreateKnowledgeTools(IServiceProvider serviceProvider)
    {
        var vectorStore = serviceProvider.GetRequiredService<IVectorStore>();
        var embeddingService = serviceProvider.GetRequiredService<IEmbeddingService>();
        
        var knowledgeService = new KnowledgeRetrievalService(vectorStore, embeddingService);
        
        return new List<AITool>
        {
            AIFunctionFactory.Create(knowledgeService.QueryKnowledge),
            AIFunctionFactory.Create(knowledgeService.QueryKnowledgeWithFilter)
        };
    }

    /// <summary>
    /// Example usage demonstrating the agent reasoning flow with knowledge retrieval.
    /// </summary>
    public static async Task ExampleAgentReasoningFlowAsync(IServiceProvider serviceProvider)
    {
        // Create the knowledge tool
        var knowledgeTool = CreateKnowledgeTool(serviceProvider);
        
        Console.WriteLine("=== Agent Framework Knowledge Retrieval Example ===");
        Console.WriteLine($"Tool Name: {knowledgeTool.Name}");
        Console.WriteLine();

        // Simulate agent invoking the tool during reasoning
        // In a real agent, this would be called automatically based on the agent's reasoning
        var arguments = new AIFunctionArguments
        {
            ["question"] = "How do I implement dependency injection in C#?"
        };

        Console.WriteLine($"Agent is retrieving knowledge for: {arguments["question"]}");
        Console.WriteLine();

        var result = await knowledgeTool.InvokeAsync(arguments);
        
        if (result is KnowledgeContext context)
        {
            Console.WriteLine($"Retrieved {context.Documents.Count} relevant documents:");
            Console.WriteLine();
            
            // Format for agent prompt injection
            var formattedContext = context.FormatAsContext();
            Console.WriteLine(formattedContext);
            
            Console.WriteLine("=== Agent can now reason over this context ===");
        }
    }
}

/// <summary>
/// Knowledge retrieval service that wraps IVectorStore for Microsoft Agent Framework tool binding.
/// Provides the queryKnowledge tool that agents can call during reasoning loops.
/// 
/// Design principles:
/// 1. Methods decorated with [Description] for agent reasoning hints
/// 2. Parameters have descriptions for proper argument binding
/// 3. Return types are JSON-serializable for agent context passing
/// 4. Error handling returns empty results rather than throwing (agent-safe)
/// </summary>
public sealed class KnowledgeRetrievalService
{
    private readonly IVectorStore _vectorStore;
    private readonly IEmbeddingService _embeddingService;

    /// <summary>
    /// Initializes a new instance of KnowledgeRetrievalService.
    /// </summary>
    /// <param name="vectorStore">The vector store for semantic search.</param>
    /// <param name="embeddingService">The embedding service for text vectorization.</param>
    public KnowledgeRetrievalService(IVectorStore vectorStore, IEmbeddingService embeddingService)
    {
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
    }

    /// <summary>
    /// Queries the knowledge base to retrieve relevant documents for a given question.
    /// This method is designed to be bound as an Agent Framework tool using AIFunctionFactory.
    /// 
    /// The agent will call this tool when it needs to:
    /// - Answer questions about the codebase
    /// - Find implementation examples
    /// - Retrieve documentation for a specific topic
    /// - Get context for code generation
    /// </summary>
    /// <param name="question">The natural language question to search for relevant knowledge.</param>
    /// <returns>A knowledge context containing relevant documents and their metadata for agent reasoning.</returns>
    [Description("Search the knowledge base for documents relevant to answer a question. Returns context with source documents, relevance scores, and formatted content for reasoning. Use this when you need information about the codebase, implementations, or documentation.")]
    public async Task<KnowledgeContext> QueryKnowledge(
        [Description("The natural language question or topic to search for. Be specific to get better results.")] string question)
    {
        try
        {
            // Generate embedding for the question
            var embedding = await _embeddingService.EmbedAsync(question);

            // Query the vector store for top 5 most relevant documents
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
        catch (Exception ex)
        {
            // Return empty context on error (agent-safe behavior)
            return new KnowledgeContext
            {
                Query = question,
                Documents = [],
                Error = $"Knowledge retrieval failed: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Queries the knowledge base with optional metadata filters for scoped retrieval.
    /// Use this when you want to limit search to specific repositories or file paths.
    /// </summary>
    /// <param name="question">The question to search for.</param>
    /// <param name="repoUrl">Optional repository URL filter to scope the search.</param>
    /// <param name="filePath">Optional file path pattern filter (supports wildcards).</param>
    /// <returns>Filtered knowledge context from the specified scope.</returns>
    [Description("Search the knowledge base with optional filters for repository or file path. Use this to narrow down search to specific projects or file types.")]
    public async Task<KnowledgeContext> QueryKnowledgeWithFilter(
        [Description("The natural language question or topic to search for.")] string question,
        [Description("Optional: Filter to specific repository URL (e.g., 'github.com/org/repo')")] string? repoUrl = null,
        [Description("Optional: Filter to specific file path pattern (e.g., '*.cs' for C# files, 'src/services/*')")] string? filePath = null)
    {
        try
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
        catch (Exception ex)
        {
            return new KnowledgeContext
            {
                Query = question,
                Documents = [],
                Error = $"Filtered knowledge retrieval failed: {ex.Message}"
            };
        }
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
/// 
/// JSON-serializable for agent context passing between tool calls and reasoning steps.
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
    /// Ordered by relevance score (highest first).
    /// </summary>
    [JsonPropertyName("documents")]
    public List<KnowledgeDocument> Documents { get; init; } = [];

    /// <summary>
    /// Error message if the retrieval failed (null on success).
    /// Agents should check this field and handle gracefully.
    /// </summary>
    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }

    /// <summary>
    /// Formats the knowledge context as a string suitable for injection into an agent prompt.
    /// Includes document citations for traceable answers.
    /// </summary>
    /// <returns>A formatted string containing all document information with citations.</returns>
    public string FormatAsContext()
    {
        if (Error is not null)
            return $"Knowledge retrieval error: {Error}";
            
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

        sb.AppendLine("When citing these sources, use the format [1], [2], etc.");
        return sb.ToString();
    }
}

/// <summary>
/// Represents a single document in the knowledge context with citation-ready metadata.
/// All fields are designed to support agent reasoning and answer generation with citations.
/// </summary>
public sealed class KnowledgeDocument
{
    /// <summary>
    /// The document title (usually the file name or heading).
    /// </summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// The document content (may be truncated for context length limits).
    /// Contains the most relevant portion of the document.
    /// </summary>
    [JsonPropertyName("content")]
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// The source reference for citations (repository URL + file path).
    /// Agents should use this when generating citations in their responses.
    /// </summary>
    [JsonPropertyName("source")]
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// The relevance score from the vector similarity search (0-1).
    /// Higher scores indicate better semantic match to the query.
    /// </summary>
    [JsonPropertyName("relevance")]
    public float Relevance { get; init; }
}
