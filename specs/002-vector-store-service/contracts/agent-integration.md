# Vector Store Integration with Microsoft Agent Framework

**Purpose**: Document how Microsoft Agent Framework agents use the Vector Store Service for knowledge-grounded reasoning.

---

## Overview

The Vector Store Service Layer is designed as a **knowledge retrieval abstraction for Microsoft Agent Framework**. Instead of agents reasoning over fixed context, they can dynamically retrieve relevant documents during their reasoning process using the `IVectorStore` interface.

**Agent Reasoning Loop with Knowledge Retrieval**:

```
Agent Initialization
    ↓
User provides task/question
    ↓
Agent decides it needs knowledge
    ↓
Agent calls: tools.queryKnowledge(query)
    ↓
Tool binding calls: IVectorStore.QueryAsync(embedding, k, filters)
    ↓
Vector Store returns: top k documents with similarity scores
    ↓
Agent integrates documents into reasoning context
    ↓
Agent LLM generates response using retrieved knowledge
    ↓
Agent optionally cites sources from retrieved documents
    ↓
Response returned to user with knowledge-grounded answer
```

---

## IVectorStore Interface for Agents

### Method: QueryAsync

**Signature**:
```csharp
Task<IEnumerable<VectorQueryResult>> QueryAsync(
    float[] embedding,
    int k,
    Dictionary<string, string> filters,
    CancellationToken cancellationToken = default)
```

**Parameters**:
- **embedding** (`float[]`): 1536-dimensional vector representing the query meaning (obtained via `IEmbeddingService.EmbedAsync(queryText)`)
- **k** (`int`): Maximum number of results to return (typically 5-10 for agent context)
- **filters** (`Dictionary<string, string>`): Optional metadata filters using SQL LIKE patterns
  - `"repo_url"`: Filter by repository (e.g., `"https://github.com/user/repo%"`)
  - `"file_path"`: Filter by file type (e.g., `"%.md"` for markdown files)
  - Can pass `null` to search entire knowledge base
- **cancellationToken**: For canceling long-running queries

**Returns**: `IEnumerable<VectorQueryResult>`
- **VectorQueryResult**: Contains
  - `DocumentEntity Document` - The retrieved document
  - `double SimilarityScore` - Cosine similarity (0.0 to 1.0; higher = more relevant)

**Error Handling**:
- **ArgumentException**: If embedding dimension != 1536
- **InvalidOperationException**: If vector store not initialized
- **TimeoutException**: If query exceeds timeout (default 30 seconds)

**Agent Integration Pattern**:
```csharp
// Tool definition for agent
public async Task<List<RetrievalResult>> QueryKnowledgeAsync(
    string query,
    int topK = 5,
    CancellationToken ct = default)
{
    // Step 1: Convert query text to embedding
    var embedding = await _embeddingService.EmbedAsync(query, ct);
    
    // Step 2: Query vector store
    var results = await _vectorStore.QueryAsync(
        embedding: embedding,
        k: topK,
        filters: null,  // Search entire knowledge base
        cancellationToken: ct);
    
    // Step 3: Format for agent context (JSON-serializable)
    return results
        .Select(r => new RetrievalResult
        {
            Document = r.Document.Text,
            Source = r.Document.FilePath,
            Similarity = r.SimilarityScore
        })
        .ToList();
}

// Agent uses results:
// Agent Prompt: "I found these relevant documents:
//                Document 1 (similarity: 0.95): [content]
//                Document 2 (similarity: 0.87): [content]
//                ...based on this knowledge..."
```

### Method: UpsertAsync

**Signature**:
```csharp
Task UpsertAsync(
    DocumentEntity doc,
    CancellationToken cancellationToken = default)
```

**Purpose**: Persist a document with embedding for later retrieval. Used by background knowledge ingestion, not by agents directly.

**Supported by Agents?**: No - agents don't ingest documents. Ingestion is handled by separate `IDocumentIngestionService`.

### Method: DeleteAsync

**Signature**:
```csharp
Task DeleteAsync(
    Guid id,
    CancellationToken cancellationToken = default)
```

**Purpose**: Remove a document from the knowledge base.

**Agent Use Case**: Agents might trigger document removal during reasoning (e.g., "This information is outdated, remove it").

---

## IEmbeddingService for Agents

### Method: EmbedAsync

**Signature**:
```csharp
Task<float[]> EmbedAsync(
    string text,
    CancellationToken cancellationToken = default)
```

**Purpose**: Convert query text to embedding vector for similarity search.

**Agent Integration**:
```csharp
// Agent needs to embed its query before retrieving documents
var queryText = "What are best practices for database indexing?";
var embedding = await _embeddingService.EmbedAsync(queryText, ct);
// Returns: float[1536] vector
```

**Reliability**:
- **Automatic Retry**: 3 attempts with exponential backoff if provider unavailable
- **Fallback**: Falls back to cached embedding if available
- **Timeout**: Fails with clear error after max retry time

**Error Handling**:
```csharp
try
{
    var embedding = await _embeddingService.EmbedAsync(query, ct);
}
catch (InvalidOperationException ex)
{
    // All embedding providers failed, no cached embedding available
    logger.LogError("Could not embed query: {error}", ex.Message);
    // Agent should handle gracefully: respond without knowledge retrieval
}
```

### Method: EmbedBatchAsync

**Signature**:
```csharp
IAsyncEnumerable<float[]> EmbedBatchAsync(
    IEnumerable<string> texts,
    CancellationToken cancellationToken = default)
```

**Purpose**: Efficiently embed multiple texts.

**Agent Use Case**: Agents might embed multiple candidate queries to find best knowledge match:
```csharp
var queries = new[]
{
    "database indexing",
    "SQL optimization",
    "performance tuning"
};
var embeddings = await _embeddingService.EmbedBatchAsync(queries, ct)
    .ToListAsync(ct);

// Query each embedding to find which retrieves best results
foreach (var embedding in embeddings)
{
    var results = await _vectorStore.QueryAsync(embedding, k: 3);
    // Agent evaluates which query retrieved most relevant documents
}
```

---

## ITokenizationService for Agents

### Method: CountTokensAsync

**Signature**:
```csharp
Task<int> CountTokensAsync(
    string text,
    string modelId,
    CancellationToken cancellationToken = default)
```

**Purpose**: Count tokens to ensure text fits in embedding model limits.

**Agent Use Case**: Verify document fits in embedding token limit before processing:
```csharp
var documentText = await GetDocumentAsync(docId);
var tokenCount = await _tokenization.CountTokensAsync(
    documentText,
    modelId: "openai:text-embedding-3-small",
    ct);

if (tokenCount > 8192)
{
    // Document too large for direct embedding
    // Use chunking instead
    var chunks = await _tokenization.ChunkAsync(documentText, 8192, ct);
    // Process chunks individually
}
```

**Supported Models**:
- `"openai:text-embedding-3-small"` (dimensions: 1536)
- `"openai:text-embedding-3-large"` (dimensions: 3072)
- `"foundry:text-embedding-ada-002"` (dimensions: 1536)
- `"ollama:nomic-embed-text"` (dimensions: 768)

### Method: ChunkAsync

**Signature**:
```csharp
Task<IEnumerable<string>> ChunkAsync(
    string text,
    int maxTokens,
    CancellationToken cancellationToken = default)
```

**Purpose**: Split large documents into chunks respecting token limits.

**Agent Use Case**: Prepare large documents before ingestion:
```csharp
var largeDoc = await GetDocumentAsync(docId);
var chunks = await _tokenization.ChunkAsync(largeDoc, maxTokens: 8192, ct);

Console.WriteLine($"Document split into {chunks.Count()} chunks");
// Each chunk is guaranteed < 8192 tokens
// Chunks respect word boundaries (no mid-word splits)
```

---

## Tool Binding for Agent Framework

### Define Tool

```csharp
using Microsoft.Agent.Framework;

public class KnowledgeRetrievalTool
{
    private readonly IVectorStore _vectorStore;
    private readonly IEmbeddingService _embedding;
    private readonly ITokenizationService _tokenization;

    public KnowledgeRetrievalTool(
        IVectorStore vectorStore,
        IEmbeddingService embedding,
        ITokenizationService tokenization)
    {
        _vectorStore = vectorStore;
        _embedding = embedding;
        _tokenization = tokenization;
    }

    [Tool(
        name: "queryKnowledge",
        description: "Search the knowledge base for documents relevant to a query. Returns top k documents with similarity scores.")]
    public async Task<QueryKnowledgeResult> QueryKnowledgeAsync(
        [Parameter(
            description: "The search query to find relevant knowledge",
            isRequired: true)]
        string query,
        [Parameter(
            description: "Maximum number of documents to return (1-20)",
            isRequired: false)]
        int topK = 5,
        [Parameter(
            description: "Filter by repository (SQL LIKE pattern, e.g., 'https://github.com/company/%')",
            isRequired: false)]
        string repoFilter = null,
        [Parameter(
            description: "Filter by file type (SQL LIKE pattern, e.g., '%.md' for markdown)",
            isRequired: false)]
        string fileTypeFilter = null,
        CancellationToken cancellationToken = default)
    {
        // Validate parameters
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query cannot be empty");
        
        if (topK < 1 || topK > 20)
            throw new ArgumentException("topK must be between 1 and 20");

        // Embed the query
        float[] embedding;
        try
        {
            embedding = await _embedding.EmbedAsync(query, cancellationToken);
        }
        catch (Exception ex)
        {
            // Return error result that agent can interpret
            return new QueryKnowledgeResult
            {
                Success = false,
                Error = $"Failed to embed query: {ex.Message}",
                Documents = new List<DocumentSummary>()
            };
        }

        // Build filters
        var filters = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(repoFilter))
            filters["repo_url"] = repoFilter;
        if (!string.IsNullOrEmpty(fileTypeFilter))
            filters["file_path"] = fileTypeFilter;

        // Query vector store
        try
        {
            var results = await _vectorStore.QueryAsync(
                embedding: embedding,
                k: topK,
                filters: filters.Any() ? filters : null,
                cancellationToken: cancellationToken);

            // Format results for agent
            return new QueryKnowledgeResult
            {
                Success = true,
                Documents = results
                    .Select(r => new DocumentSummary
                    {
                        Title = r.Document.Title ?? Path.GetFileName(r.Document.FilePath),
                        FilePath = r.Document.FilePath,
                        Repository = r.Document.RepoUrl,
                        Content = r.Document.Text.Length > 500
                            ? r.Document.Text.Substring(0, 500) + "..."
                            : r.Document.Text,
                        SimilarityScore = r.SimilarityScore,
                        Metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(
                            r.Document.MetadataJson ?? "{}")
                    })
                    .ToList()
            };
        }
        catch (Exception ex)
        {
            return new QueryKnowledgeResult
            {
                Success = false,
                Error = $"Vector store query failed: {ex.Message}",
                Documents = new List<DocumentSummary>()
            };
        }
    }

    [Tool(
        name: "validateDocumentTokens",
        description: "Check if a document fits within embedding model token limits before processing")]
    public async Task<TokenValidationResult> ValidateDocumentTokensAsync(
        [Parameter(description: "Document content to validate")]
        string documentText,
        [Parameter(description: "Embedding model to validate against")]
        string modelId = "openai:text-embedding-3-small",
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tokenCount = await _tokenization.CountTokensAsync(
                documentText,
                modelId,
                cancellationToken);

            const int MaxTokens = 8192;
            
            return new TokenValidationResult
            {
                TokenCount = tokenCount,
                MaxTokens = MaxTokens,
                Fits = tokenCount <= MaxTokens,
                Message = tokenCount <= MaxTokens
                    ? $"Document fits in {tokenCount}/{MaxTokens} tokens"
                    : $"Document too large: {tokenCount} tokens (max {MaxTokens}). " +
                      $"Consider chunking with ChunkDocument tool."
            };
        }
        catch (Exception ex)
        {
            return new TokenValidationResult
            {
                Error = $"Token validation failed: {ex.Message}"
            };
        }
    }
}

// Result types (must be JSON-serializable for agent context)
public class QueryKnowledgeResult
{
    public bool Success { get; set; }
    public string Error { get; set; }
    public List<DocumentSummary> Documents { get; set; }
}

public class DocumentSummary
{
    public string Title { get; set; }
    public string FilePath { get; set; }
    public string Repository { get; set; }
    public string Content { get; set; }
    public double SimilarityScore { get; set; }
    public Dictionary<string, string> Metadata { get; set; }
}

public class TokenValidationResult
{
    public int TokenCount { get; set; }
    public int MaxTokens { get; set; }
    public bool Fits { get; set; }
    public string Message { get; set; }
    public string Error { get; set; }
}
```

### Register Tool with Agent

```csharp
// In Program.cs or service configuration
var services = new ServiceCollection();

// Register Vector Store services
services.AddScoped<IPersistenceVectorStore, SqlServerVectorStore>();
services.AddScoped<ITokenizationService>(sp =>
    new TokenizationService(/* dependencies */));
services.AddScoped<IEmbeddingService>(sp =>
    EmbeddingServiceFactory.CreateFromConfiguration(configuration));

// Register the knowledge tool
services.AddScoped<KnowledgeRetrievalTool>();

// Create agent with tool
var agentBuilder = new AgentBuilder("ResearchAssistant")
    .WithSystemPrompt("""
        You are a helpful research assistant. When users ask questions, 
        use the queryKnowledge tool to find relevant documents from the 
        knowledge base. Cite your sources and provide evidence-based answers.
        """)
    .WithTool<KnowledgeRetrievalTool>(t => t.QueryKnowledgeAsync)
    .WithTool<KnowledgeRetrievalTool>(t => t.ValidateDocumentTokensAsync);

var agent = agentBuilder.Build();
```

---

## Agent Reasoning with Knowledge Retrieval

### Example: Research Agent

```csharp
public class ResearchAgent
{
    private readonly IAgent _agent;
    private readonly ILogger<ResearchAgent> _logger;

    public ResearchAgent(IAgent agent, ILogger<ResearchAgent> logger)
    {
        _agent = agent;
        _logger = logger;
    }

    public async Task<ResearchResult> ResearchAsync(
        string question,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting research for: {question}", question);

        try
        {
            // Agent reasons about the question and decides to use tools
            var agentResponse = await _agent.ReasonAsync(
                input: question,
                cancellationToken: cancellationToken);

            // Extract retrieved documents from agent context
            var documents = agentResponse.ToolCalls
                .Where(tc => tc.ToolName == "queryKnowledge")
                .SelectMany(tc => ExtractDocumentsFromResult(tc.Result))
                .ToList();

            return new ResearchResult
            {
                Answer = agentResponse.GeneratedAnswer,
                Sources = documents,
                RetrievalCount = documents.Count,
                ExecutionTime = agentResponse.ExecutionTime
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Research failed for: {question}", question);
            throw;
        }
    }

    private List<DocumentSource> ExtractDocumentsFromResult(object result)
    {
        if (result is QueryKnowledgeResult qkr && qkr.Success)
        {
            return qkr.Documents
                .Select(d => new DocumentSource
                {
                    Title = d.Title,
                    Path = d.FilePath,
                    Repository = d.Repository,
                    RelevanceScore = d.SimilarityScore
                })
                .ToList();
        }
        return new List<DocumentSource>();
    }
}

public class ResearchResult
{
    public string Answer { get; set; }
    public List<DocumentSource> Sources { get; set; }
    public int RetrievalCount { get; set; }
    public TimeSpan ExecutionTime { get; set; }
}

public class DocumentSource
{
    public string Title { get; set; }
    public string Path { get; set; }
    public string Repository { get; set; }
    public double RelevanceScore { get; set; }
}
```

### Usage

```csharp
// Create and use agent
var agentFactory = serviceProvider.GetRequiredService<IAgentFactory>();
var agent = agentFactory.CreateAgent("ResearchAssistant");

var researcher = new ResearchAgent(agent, logger);

var result = await researcher.ResearchAsync(
    "What are the best practices for database indexing?");

Console.WriteLine($"Answer: {result.Answer}");
Console.WriteLine($"Sources ({result.RetrievalCount}):");
foreach (var source in result.Sources)
{
    Console.WriteLine($"  - {source.Title} (relevance: {source.RelevanceScore:P0})");
    Console.WriteLine($"    from: {source.Path}");
}
Console.WriteLine($"Execution time: {result.ExecutionTime.TotalSeconds}s");
```

---

## Latency Considerations

### Agent Response Time Breakdown

**Agent response time = Agent reasoning + Tool call latency**

Expected breakdown for a typical query:

```
User Query: "What are best practices for database indexing?"
    ↓
[100ms] Agent receives and processes query
    ↓
[50ms]  Agent decides to use queryKnowledge tool
    ↓
[50ms]  Agent calls EmbeddingService.EmbedAsync(query)
    ↓
[300ms] Vector store QueryAsync (key-NN search in 10k docs)
    ↓
[100ms] Format results for agent context
    ↓
[200ms] Agent integrates knowledge into LLM prompt
    ↓
[1500ms] LLM generates response (varies by model)
    ↓
Total: ~2.3 seconds (agent reasoning: 0.8s + Vector Store: 0.3s + LLM: 1.2s)
```

### Optimization Tips

**For faster agent response**:
1. **Limit k**: Return fewer documents (e.g., k=3 instead of k=10)
2. **Use filters**: Narrow search space (e.g., filter by repository)
3. **Increase timeout**: If queries occasionally timeout, increase limit
4. **Tune batch sizes**: Larger batches in EmbedBatchAsync improve throughput
5. **Monitor query performance**: Enable SQL Query Store on Vector Store database

---

## Error Handling in Agent Context

### Agent Graceful Degradation

```csharp
// Tool handles failures gracefully without breaking agent
[Tool(name: "queryKnowledge")]
public async Task<QueryKnowledgeResult> QueryKnowledgeAsync(string query, ...)
{
    try
    {
        var embedding = await _embedding.EmbedAsync(query, ct);
        var results = await _vectorStore.QueryAsync(embedding, k, filters, ct);
        return new QueryKnowledgeResult { Success = true, Documents = results };
    }
    catch (InvalidOperationException ex)
    {
        // Embedding service failed - return error result
        return new QueryKnowledgeResult
        {
            Success = false,
            Error = $"Could not retrieve knowledge: {ex.Message}",
            Documents = new List<DocumentSummary>()
        };
    }
    
    // Agent receives error result and can:
    // - Inform user that knowledge base is unavailable
    // - Fall back to general knowledge in LLM
    // - Retry the request
}
```

---

## See Also

- [IVectorStore.md](./IVectorStore.md) - Detailed interface documentation
- [IEmbeddingService.md](./IEmbeddingService.md) - Embedding provider APIs
- [ITokenizationService.md](./ITokenizationService.md) - Token counting and chunking
- [quickstart.md](../quickstart.md) - Configuration and usage examples
