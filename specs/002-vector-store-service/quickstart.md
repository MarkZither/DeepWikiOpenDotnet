# Vector Store Service Layer - Quickstart Guide

**Purpose**: Get the Vector Store Service up and running for RAG document retrieval and Agent Framework knowledge access.

---

## Configuration

### OpenAI Provider

**appsettings.json**:
```json
{
  "Embedding": {
    "Provider": "openai",
    "ApiKey": "sk-...",
    "Model": "text-embedding-3-small",
    "MaxTokens": 8192
  }
}
```

**Environment Variables**:
```bash
EMBEDDING_PROVIDER=openai
EMBEDDING_API_KEY=sk-...
EMBEDDING_MODEL=text-embedding-3-small
```

### Microsoft AI Foundry (Foundry Local)

**appsettings.json**:
```json
{
  "Embedding": {
    "Provider": "foundry",
    "Endpoint": "http://localhost:8000",
    "ApiKey": "foundry-local-key",
    "Model": "text-embedding-ada-002",
    "MaxTokens": 8192
  }
}
```

**Environment Variables**:
```bash
EMBEDDING_PROVIDER=foundry
EMBEDDING_ENDPOINT=http://localhost:8000
EMBEDDING_API_KEY=foundry-local-key
EMBEDDING_MODEL=text-embedding-ada-002
```

### Ollama (Local)

**appsettings.json**:
```json
{
  "Embedding": {
    "Provider": "ollama",
    "Endpoint": "http://localhost:11434",
    "Model": "nomic-embed-text",
    "MaxTokens": 8192
  }
}
```

**Environment Variables**:
```bash
EMBEDDING_PROVIDER=ollama
EMBEDDING_ENDPOINT=http://localhost:11434
EMBEDDING_MODEL=nomic-embed-text
```

**Running Ollama locally**:
```bash
ollama serve
# In another terminal:
ollama pull nomic-embed-text
```

---

## Basic Usage

### 1. Inject Services

```csharp
using DeepWiki.Data.Abstractions;
using DeepWiki.Rag.Core;

public class KnowledgeService
{
    private readonly IVectorStore _vectorStore;
    private readonly ITokenizationService _tokenization;
    private readonly IDocumentIngestionService _ingestion;

    public KnowledgeService(
        IVectorStore vectorStore,
        ITokenizationService tokenization,
        IDocumentIngestionService ingestion)
    {
        _vectorStore = vectorStore;
        _tokenization = tokenization;
        _ingestion = ingestion;
    }
}
```

### 2. Ingest Documents

```csharp
// Ingest a batch of documents
var documents = new List<DocumentEntity>
{
    new()
    {
        RepoUrl = "https://github.com/user/repo",
        FilePath = "docs/architecture.md",
        Title = "Architecture Guide",
        Text = "..."  // Document content
    },
    // ... more documents
};

var result = await _ingestion.IngestAsync(documents, cancellationToken);
Console.WriteLine($"Ingested {result.SuccessCount} documents");
if (result.FailureCount > 0)
{
    Console.WriteLine($"Failed: {result.FailureCount}");
    foreach (var error in result.Errors)
        Console.WriteLine($"  - {error}");
}
```

### 3. Query Similar Documents

```csharp
// Get embedding for query
var queryEmbedding = await _embedding.EmbedAsync("How do I configure clustering?", ct);

// Find similar documents
var results = await _vectorStore.QueryAsync(
    embedding: queryEmbedding,
    k: 5,  // Return top 5 results
    filters: new Dictionary<string, string>
    {
        { "repo_url", "https://github.com/user/repo%" },  // SQL LIKE pattern
        { "file_path", "%.md" }  // Only markdown files
    },
    cancellationToken: ct);

foreach (var result in results)
{
    Console.WriteLine($"Similarity: {result.SimilarityScore:F3}");
    Console.WriteLine($"File: {result.Document.FilePath}");
    Console.WriteLine($"Content: {result.Document.Text.Substring(0, 200)}...\n");
}
```

### 4. Chunk Large Documents

```csharp
// Chunk a large document respecting token limits
var chunks = await _tokenization.ChunkAsync(
    text: largeDocumentContent,
    maxTokens: 8192,  // Embedding model token limit
    cancellationToken: ct);

Console.WriteLine($"Document split into {chunks.Count} chunks");
foreach (var (i, chunk) in chunks.Select((c, i) => (i, c)))
{
    Console.WriteLine($"Chunk {i}: {chunk.Length} characters");
}
```

### 5. Count Tokens

```csharp
// Verify token count before embedding
var tokenCount = await _tokenization.CountTokensAsync(
    text: documentText,
    modelId: "openai:text-embedding-3-small",  // Or "foundry:...", "ollama:..."
    cancellationToken: ct);

Console.WriteLine($"Token count: {tokenCount}");
if (tokenCount > 8192)
    Console.WriteLine("WARNING: Document exceeds embedding token limit!");
```

---

## Using Vector Store with Microsoft Agent Framework

### Overview

The Vector Store Service is designed as a **knowledge retrieval abstraction for Microsoft Agent Framework agents**. Agents can access semantic knowledge from documents during reasoning loops without direct Vector Store knowledge.

### Example: Agent with Knowledge Retrieval Tool

```csharp
using Microsoft.Agent.Framework;
using DeepWiki.Data.Abstractions;

public class KnowledgeRetrievalTool
{
    private readonly IVectorStore _vectorStore;
    private readonly IEmbeddingService _embedding;

    public KnowledgeRetrievalTool(
        IVectorStore vectorStore,
        IEmbeddingService embedding)
    {
        _vectorStore = vectorStore;
        _embedding = embedding;
    }

    // Define tool for Agent Framework
    [Tool(
        name: "queryKnowledge",
        description: "Search the knowledge base for documents relevant to a query")]
    public async Task<List<RetrievalResult>> QueryKnowledgeAsync(
        [Parameter(description: "The search query")]
        string query,
        [Parameter(description: "Maximum number of results", isRequired: false)]
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        // Convert query text to embedding
        var queryEmbedding = await _embedding.EmbedAsync(query, cancellationToken);

        // Search vector store
        var documents = await _vectorStore.QueryAsync(
            embedding: queryEmbedding,
            k: topK,
            filters: null,  // No filters; search entire knowledge base
            cancellationToken: cancellationToken);

        // Return formatted results for agent context
        return documents
            .Select(d => new RetrievalResult
            {
                FileName = d.Document.FilePath,
                Title = d.Document.Title,
                Content = d.Document.Text,
                SimilarityScore = d.SimilarityScore,
                SourceUrl = d.Document.RepoUrl
            })
            .ToList();
    }
}

public class RetrievalResult
{
    public string FileName { get; set; }
    public string Title { get; set; }
    public string Content { get; set; }
    public double SimilarityScore { get; set; }
    public string SourceUrl { get; set; }
}
```

### Register Tool in Agent Framework

```csharp
// In Program.cs or dependency injection setup
services.AddScoped<IPersistenceVectorStore, SqlServerVectorStore>();
services.AddScoped<IEmbeddingService>(sp =>
    EmbeddingServiceFactory.CreateFromConfiguration(config));

// Register the tool
services.AddScoped<KnowledgeRetrievalTool>();

// When creating agent
var agent = new Agent("ResearchAssistant")
    .WithTool<KnowledgeRetrievalTool>(tool => tool.QueryKnowledgeAsync);
```

### Agent Reasoning with Knowledge Retrieval

**Agent Execution Flow**:

```
User Query: "What are the best practices for database indexing?"
    ↓
Agent Reasoning: "I need to find documents about database indexing"
    ↓
Agent calls: tools.queryKnowledge("database indexing best practices")
    ↓
Tool executes: Convert query → Embedding → Vector Store Query → Return documents
    ↓
Returned to Agent: [
    {
        fileName: "docs/performance-tuning.md",
        content: "Indexes can dramatically improve query performance...",
        similarityScore: 0.92
    },
    ...
]
    ↓
Agent integrates into context: "I found these relevant documents: ..."
    ↓
Agent LLM generates answer with citations and retrieved context
    ↓
User receives answer with knowledge-grounded reasoning
```

### Code Example: Agent Using Knowledge

```csharp
public class ResearchAgent
{
    private readonly IAgent _agent;

    public ResearchAgent(IAgent agent)
    {
        _agent = agent;
    }

    public async Task<string> ResearchAsync(
        string question,
        CancellationToken cancellationToken = default)
    {
        // Agent automatically:
        // 1. Receives question
        // 2. Decides to use queryKnowledge tool
        // 3. Calls tool with question
        // 4. Receives documents from vector store
        // 5. Integrates documents into LLM prompt
        // 6. Generates answer with knowledge-grounded reasoning
        
        var response = await _agent.ReasonAsync(
            input: question,
            cancellationToken: cancellationToken);

        return response.GeneratedAnswer;
    }
}

// Usage
var agent = new ResearchAgent(agentFrameworkAgent);
var answer = await agent.ResearchAsync(
    "What are best practices for database indexing?");
Console.WriteLine(answer);
// Output: "Based on the documentation, here are the best practices:
//          1. Create indexes on frequently queried columns...
//          (cited from docs/performance-tuning.md, similarity: 0.92)"
```

---

## Testing

### Unit Tests (Local)

```bash
# Run fast unit tests (exclude integration tests)
# During local development, prefer skipping integration tests:
dotnet test --filter "Category!=Integration"
```

### Integration Tests

```bash
# Requires test SQL Server / PostgreSQL instances (Docker via Testcontainers)
# Set connection string via environment variable if required:
export VECTOR_STORE_TEST_CONNECTION="Server=(local);Database=DeepWikiTest;Trusted_Connection=true;"

# Run integration tests explicitly:
dotnet test --filter "Category=Integration"
```

### Performance Tests

```bash
# Run performance benchmarks (10k document corpus)
# Mark long-running benchmarks with Category=Performance and run separately
dotnet test --filter "Category=Performance" --logger "console;verbosity=detailed"

# Expected output:
# Vector Store Query (10k docs): 245ms
# Embedding throughput: 67 docs/sec
# Token counting parity: 98.5% match to Python tiktoken
```

---

## Deployment

### Docker Compose (Ollama + SQL Server)

```yaml
version: '3.8'
services:
  ollama:
    image: ollama/ollama:latest
    ports:
      - "11434:11434"
    volumes:
      - ollama:/root/.ollama
    environment:
      - OLLAMA_MODELS=/root/.ollama/models
    command: serve

  mssql:
    image: mcr.microsoft.com/mssql/server:2025-latest
    ports:
      - "1433:1433"
    environment:
      - ACCEPT_EULA=Y
      - SA_PASSWORD=YourStrongPassword123!
    volumes:
      - mssql:/var/opt/mssql

  app:
    build: .
    ports:
      - "5000:5000"
    environment:
      - EMBEDDING_PROVIDER=ollama
      - EMBEDDING_ENDPOINT=http://ollama:11434
      - EMBEDDING_MODEL=nomic-embed-text
      - ConnectionStrings__VectorStore=Server=mssql;Database=DeepWiki;User Id=sa;Password=YourStrongPassword123!;TrustServerCertificate=true;
    depends_on:
      - ollama
      - mssql

volumes:
  ollama:
  mssql:
```

### Kubernetes (Microsoft AI Foundry)

```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: vector-store-config
data:
  appsettings.Production.json: |
    {
      "Embedding": {
        "Provider": "foundry",
        "Endpoint": "https://your-foundry.ai.azure.com",
        "Model": "text-embedding-ada-002"
      }
    }

---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: vector-store-api
spec:
  replicas: 3
  selector:
    matchLabels:
      app: vector-store-api
  template:
    metadata:
      labels:
        app: vector-store-api
    spec:
      containers:
      - name: api
        image: deepwiki/vector-store:latest
        ports:
        - containerPort: 5000
        env:
        - name: EMBEDDING_API_KEY
          valueFrom:
            secretKeyRef:
              name: foundry-credentials
              key: api-key
        volumeMounts:
        - name: config
          mountPath: /app/config
      volumes:
      - name: config
        configMap:
          name: vector-store-config
```

---

## Troubleshooting

### "Embedding service unavailable"

**Cause**: Embedding provider (OpenAI, Ollama, Foundry) is not responding

**Solution**:
1. Verify provider is running (ollama serve for local)
2. Check configuration (endpoint URL, API key)
3. Vector Store will automatically retry 3 times with exponential backoff
4. If still failing, check logs for provider error:
   ```csharp
   logger.LogError("Embedding failed after 3 retries: {error}", ex.Message);
   ```

### "Token count mismatch"

**Cause**: Token counting differs from Python tiktoken

**Solution**:
- Verify tokenization model matches: `CountTokensAsync(text, modelId: "openai:...")`
- Known tolerance: ≤2% difference to Python reference
- Run parity tests: `dotnet test ... --filter "Parity"`

### "Vector dimension mismatch"

**Cause**: Embedding model returned wrong dimension (not 1536)

**Solution**:
- Verify model is correct (text-embedding-3-small returns 1536)
- Check provider configuration
- Upsert validation will reject mismatched dimensions with clear error

### "Query returns no results"

**Causes & Solutions**:
1. **No documents ingested yet**: Ingest sample documents first
2. **Filters too restrictive**: Query with `filters: null` to test without filters
3. **Embedding model mismatch**: Verify query embedding uses same model as ingestion
4. **Similarity threshold**: All results are returned regardless of score; sort by SimilarityScore

---

## Performance Tuning

### Vector Store Query Optimization

**Index Strategy**:
- Clustered columnstore index on `Embedding` column
- Non-clustered index on `RepoUrl` for filter performance
- Unique index on (RepoUrl, FilePath) for duplicate prevention

**Query Optimization**:
```csharp
// Good: Filter before similarity search
var results = await _vectorStore.QueryAsync(
    embedding: queryEmbedding,
    k: 10,
    filters: new Dictionary<string, string>
    {
        { "repo_url", "desired-repo%" }  // Narrows search space
    });

// Avoid: Querying all documents then filtering in-memory
var allResults = await _vectorStore.QueryAsync(embedding, k: 1000);
var filtered = allResults.Where(r => r.Document.RepoUrl == "...").Take(10);
```

### Embedding Batch Optimization

```csharp
// Good: Batch 100+ documents together
var results = await _embedding.EmbedBatchAsync(
    documents.Select(d => d.Text),
    cancellationToken: ct);  // Default batch size: 10

// Avoid: Single embeddings in a loop
foreach (var doc in documents)
{
    var embedding = await _embedding.EmbedAsync(doc.Text);  // Slow!
}
```

### Connection Pooling

```csharp
// In Program.cs
services.AddDbContext<VectorDbContext>(options =>
    options.UseSqlServer(
        connectionString,
        sqlOptions =>
        {
            sqlOptions.MaxPoolSize = 20;  // Adjust based on load
            sqlOptions.CommandTimeout = 30;  // seconds
        }));
```

### Token Limits and Chunking

| Model | Max Tokens | Recommended Chunk Size |
|-------|------------|------------------------|
| text-embedding-3-small | 8192 | 6000-7500 |
| text-embedding-ada-002 | 8192 | 6000-7500 |
| nomic-embed-text | 8192 | 6000-7500 |

**Why leave headroom?**: Leave 10-15% below max to account for tokenization variance and metadata.

```csharp
// Configure chunk size in ingestion
var request = new IngestionRequest
{
    Documents = documents,
    MaxTokensPerChunk = 7000,  // Leave headroom
    BatchSize = 20  // Increase for higher throughput
};
```

### Index Maintenance

```sql
-- Monitor index fragmentation
SELECT 
    i.name AS IndexName,
    ips.avg_fragmentation_in_percent AS Fragmentation
FROM sys.dm_db_index_physical_stats(DB_ID(), OBJECT_ID('Documents'), NULL, NULL, 'LIMITED') ips
JOIN sys.indexes i ON ips.object_id = i.object_id AND ips.index_id = i.index_id;

-- Rebuild fragmented indexes (>30% fragmentation)
ALTER INDEX IX_Documents_Embedding ON Documents REBUILD;

-- Reorganize for moderate fragmentation (10-30%)
ALTER INDEX IX_Documents_RepoUrl ON Documents REORGANIZE;
```

---

## Adding a New Embedding Provider

Follow these steps to add support for a new embedding provider:

### Step 1: Create Provider Client

Create a new class inheriting from `BaseEmbeddingClient`:

```csharp
// src/DeepWiki.Rag.Core/Embedding/Providers/MyProviderEmbeddingClient.cs
using DeepWiki.Data.Abstractions;
using Microsoft.Extensions.Logging;

namespace DeepWiki.Rag.Core.Embedding.Providers;

public class MyProviderEmbeddingClient : BaseEmbeddingClient
{
    private readonly HttpClient _httpClient;
    
    public override string Provider => "myprovider";
    public override string ModelId { get; }
    public override int EmbeddingDimension => 1536;

    public MyProviderEmbeddingClient(
        string apiKey,
        string modelId,
        string endpoint,
        RetryPolicy retryPolicy,
        IEmbeddingCache? cache = null,
        ILogger<MyProviderEmbeddingClient>? logger = null)
        : base(retryPolicy, cache, logger)
    {
        ModelId = modelId;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(endpoint)
        };
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    }

    protected override async Task<float[]> EmbedCoreAsync(
        string text,
        CancellationToken cancellationToken)
    {
        var request = new { input = text, model = ModelId };
        var response = await _httpClient.PostAsJsonAsync("/embeddings", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        
        var result = await response.Content.ReadFromJsonAsync<EmbeddingResult>(cancellationToken);
        return result?.Data?.FirstOrDefault()?.Embedding ?? throw new InvalidOperationException("No embedding returned");
    }

    private record EmbeddingResult(EmbeddingData[] Data);
    private record EmbeddingData(float[] Embedding);
}
```

### Step 2: Update EmbeddingServiceFactory

Add the new provider to the factory:

```csharp
// In EmbeddingServiceFactory.CreateForProvider:
public IEmbeddingService CreateForProvider(string provider)
{
    return provider.ToLowerInvariant() switch
    {
        "openai" => CreateOpenAIClient(section, retryPolicy),
        "foundry" => CreateFoundryClient(section, retryPolicy),
        "ollama" => CreateOllamaClient(section, retryPolicy),
        "myprovider" => CreateMyProviderClient(section, retryPolicy),  // Add this
        _ => throw new ArgumentException($"Unknown provider: {provider}")
    };
}

private MyProviderEmbeddingClient CreateMyProviderClient(
    IConfigurationSection section,
    RetryPolicy retryPolicy)
{
    var mySection = section.GetSection("MyProvider");
    return new MyProviderEmbeddingClient(
        apiKey: mySection["ApiKey"] ?? throw new InvalidOperationException("API key required"),
        modelId: mySection["ModelId"] ?? "default-model",
        endpoint: mySection["Endpoint"] ?? "https://api.myprovider.com",
        retryPolicy: retryPolicy,
        cache: _cache,
        logger: _loggerFactory?.CreateLogger<MyProviderEmbeddingClient>());
}
```

### Step 3: Update IsProviderAvailable

```csharp
public bool IsProviderAvailable(string provider)
{
    var section = _configuration.GetSection(ConfigurationSection);
    return provider.ToLowerInvariant() switch
    {
        // ... existing providers ...
        "myprovider" => !string.IsNullOrEmpty(section["MyProvider:ApiKey"]),
        _ => false
    };
}
```

### Step 4: Add Configuration

Add to `appsettings.json`:

```json
{
  "Embedding": {
    "Provider": "myprovider",
    "MyProvider": {
      "ApiKey": "your-api-key",
      "ModelId": "embedding-model-v1",
      "Endpoint": "https://api.myprovider.com"
    }
  }
}
```

### Step 5: Add Unit Tests

```csharp
// tests/DeepWiki.Rag.Core.Tests/Embedding/MyProviderEmbeddingClientTests.cs
public class MyProviderEmbeddingClientTests
{
    [Fact]
    public async Task EmbedAsync_ReturnsCorrectDimension()
    {
        // Arrange with mock HTTP handler
        var client = CreateClientWithMockHandler();
        
        // Act
        var embedding = await client.EmbedAsync("test text");
        
        // Assert
        Assert.Equal(1536, embedding.Length);
    }
    
    [Fact]
    public void Factory_CreatesMyProviderClient_WhenConfigured()
    {
        var config = CreateConfigWithProvider("myprovider");
        var factory = new EmbeddingServiceFactory(config);
        
        var service = factory.Create();
        
        Assert.Equal("myprovider", service.Provider);
    }
}
```

---

## Extended Troubleshooting

### "Connection refused" to Ollama

**Cause**: Ollama server not running or wrong endpoint

**Solutions**:
```bash
# Check if Ollama is running
curl http://localhost:11434/api/tags

# Start Ollama if not running
ollama serve

# If using Docker, ensure port is exposed
docker run -p 11434:11434 ollama/ollama

# If using WSL2, use host.docker.internal
EMBEDDING_ENDPOINT=http://host.docker.internal:11434
```

### "Rate limit exceeded" (OpenAI)

**Cause**: Too many API requests per minute

**Solutions**:
1. Reduce batch size: `BatchSize = 5`
2. Add delay between batches
3. Upgrade OpenAI tier for higher limits
4. Use fallback to Ollama for development

```csharp
// Automatic retry handles rate limits
var request = new IngestionRequest
{
    Documents = documents,
    BatchSize = 5,  // Smaller batches
    MaxRetries = 5  // More retries for rate limits
};
```

### "Out of memory" during batch embedding

**Cause**: Too many documents in memory at once

**Solutions**:
```csharp
// Process in smaller batches
var allDocuments = LoadDocuments();
var batchSize = 100;

for (int i = 0; i < allDocuments.Count; i += batchSize)
{
    var batch = allDocuments.Skip(i).Take(batchSize).ToList();
    var request = IngestionRequest.Create(batch);
    await ingestionService.IngestAsync(request);
    
    // Allow GC between batches
    GC.Collect();
}
```

### "Duplicate key violation" on upsert

**Cause**: Concurrent upserts to same document

**Solution**: This is handled automatically with "first write wins" semantics. If you need custom handling:

```csharp
try
{
    await vectorStore.UpsertAsync(document);
}
catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("duplicate") == true)
{
    // Document was updated by another process
    // Optionally re-read and merge
    var existing = await vectorStore.QueryAsync(...);
}
```

### "SSL/TLS handshake failed" to Foundry

**Cause**: Certificate validation issues

**Solutions**:
```csharp
// For development only - disable certificate validation
var handler = new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) => true
};

// Better: Add certificate to trusted store
// Or use proper certificates in production
```

### Logging for Debugging

Enable detailed logging to diagnose issues:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "DeepWiki.Rag.Core": "Debug",
      "DeepWiki.Rag.Core.Embedding": "Trace"
    }
  }
}
```

```csharp
// In code, structured logging shows all context
logger.LogDebug(
    "Embedding request: Provider={Provider}, Model={Model}, TextLength={Length}",
    provider, modelId, text.Length);
```

---

## Running Tests

### Unit Tests (Fast, No External Dependencies)

```bash
# Run all unit tests (excludes integration tests)
dotnet test --filter "Category!=Integration"

# Run specific test project
dotnet test tests/DeepWiki.Rag.Core.Tests/ --filter "Category!=Integration"

# Run with coverage
dotnet test --collect:"XPlat Code Coverage" --settings coverlet.runsettings

# Run specific test class
dotnet test --filter "FullyQualifiedName~TokenizationServiceTests"
```

### Integration Tests (Requires Database)

```bash
# Using in-memory SQLite (no setup required)
dotnet test --filter "Category=Integration"

# Using SQL Server (set connection string)
export VECTOR_STORE_TEST_CONNECTION="Server=localhost;Database=DeepWikiTest;Trusted_Connection=true;TrustServerCertificate=true;"
dotnet test --filter "Category=Integration"

# Using Testcontainers (automatic Docker provisioning)
dotnet test --filter "Category=Integration" -- RunSettings.Arguments.UseTestcontainers=true
```

### Performance Tests

```bash
# Run performance benchmarks
dotnet test --filter "Category=Performance" --logger "console;verbosity=detailed"

# Run specific benchmark
dotnet test --filter "FullyQualifiedName~PerformanceTests.QueryAsync_10kDocuments"

# With timeout for long-running tests
dotnet test --filter "Category=Performance" -- RunSettings.Arguments.TestTimeout=300000
```

### Parity Tests (Token Counting Validation)

```bash
# Validate token counting against Python tiktoken
dotnet test --filter "FullyQualifiedName~TokenizationParityTests"

# Regenerate Python reference data (requires Python environment)
cd embedding-samples
python generate_tiktoken_samples.py > python-tiktoken-samples.json
```

### Test with Watch Mode

```bash
# Continuous testing during development
dotnet watch test --filter "Category!=Integration" --project tests/DeepWiki.Rag.Core.Tests/
```

### Code Coverage Report

```bash
# Generate coverage and open report
dotnet test --collect:"XPlat Code Coverage" --settings coverlet.runsettings
reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" -targetdir:"TestResults/coverage-report" -reporttypes:Html

# Open report
open TestResults/coverage-report/index.html  # macOS
start TestResults/coverage-report/index.html  # Windows
xdg-open TestResults/coverage-report/index.html  # Linux
```

---

## CI/CD Pipeline

### Overview

The project uses GitHub Actions for continuous integration and deployment. There are two main workflows:

| Workflow | File | Trigger | Purpose |
|----------|------|---------|---------|
| Build & Test | `.github/workflows/build.yml` | Push/PR to main, develop | Unit tests, coverage, benchmarks |
| Integration Tests | `.github/workflows/integration-tests.yml` | Push/PR to main, develop | Database integration tests |

### Build & Test Workflow

The main CI pipeline runs on every push and PR:

1. **Build**: Restores packages and builds all projects in Release mode
2. **Unit Tests**: Runs all tests excluding `Category=Integration`
3. **Code Coverage**: Collects coverage via coverlet, uploads reports
4. **Performance Benchmarks**: Runs on main branch pushes only

```bash
# Reproduce CI locally
dotnet restore deepwiki-open-dotnet.slnx
dotnet build deepwiki-open-dotnet.slnx --configuration Release
dotnet test deepwiki-open-dotnet.slnx --no-build --configuration Release \
  --filter "Category!=Integration" \
  --collect:"XPlat Code Coverage" \
  --settings coverlet.runsettings
```

### Integration Tests Workflow

Runs database integration tests using Testcontainers:

- **SQL Server**: Provisions SQL Server 2025 container via Testcontainers
- **Postgres**: Provisions Postgres with pgvector extension
- **Timeout**: 60 minutes to allow container startup

```bash
# Reproduce integration tests locally (requires Docker)
docker pull mcr.microsoft.com/mssql/server:2025-latest
dotnet test --filter "Category=Integration" --logger "console;verbosity=normal"
```

### Environment Variables for CI

| Variable | Purpose | Required |
|----------|---------|----------|
| `CODECOV_TOKEN` | Upload coverage to Codecov | Optional |
| `VECTOR_STORE_TEST_CONNECTION` | SQL Server connection override | Optional |
| `POSTGRES_TEST_CONNECTION` | Postgres connection override | Optional |

### Running Tests in CI

```yaml
# Example: Manual workflow dispatch
gh workflow run build.yml

# View workflow runs
gh run list --workflow=build.yml

# Download coverage artifacts
gh run download <run-id> --name coverage-reports
```

### Coverage Thresholds

The project targets these coverage thresholds (SC-004):

| Component | Target | Current |
|-----------|--------|---------|
| IVectorStore | ≥90% | 100% |
| ITokenizationService | ≥90% | ~70% |
| IEmbeddingService | ≥90% | ~85% |

Coverage reports are uploaded as artifacts and optionally to Codecov.

### Adding New CI Steps

To add a new CI step:

1. Edit `.github/workflows/build.yml`
2. Add step after build/test:
   ```yaml
   - name: My Custom Step
     run: |
       # Your commands here
   ```
3. For long-running steps, add timeout:
   ```yaml
   - name: Long Running Step
     timeout-minutes: 30
     run: ...
   ```

### Troubleshooting CI

**"Test failed but works locally"**:
- Check for environment-specific paths
- Verify test doesn't depend on external services
- Check for race conditions in parallel tests

**"Coverage upload failed"**:
- Verify `CODECOV_TOKEN` secret is set (optional)
- Check coverage files exist in `./TestResults/`

**"Testcontainers timeout"**:
- Increase `timeout-minutes` in workflow
- Check Docker service is available
- Verify image pull succeeds

---

## API Contracts


For detailed API documentation, see:
- [IVectorStore](./contracts/IVectorStore.md) - Semantic document retrieval
- [ITokenizationService](./contracts/ITokenizationService.md) - Token counting and chunking
- [IEmbeddingService](./contracts/IEmbeddingService.md) - Multiple embedding providers
- [Agent Framework Integration](./contracts/agent-integration.md) - Using Vector Store with agents

---

## Next Steps

1. ✅ **Configure** embedding provider (OpenAI, Ollama, or Foundry)
2. ✅ **Ingest** sample documents
3. ✅ **Query** vector store to verify retrieval
4. ✅ **Test** with Agent Framework tools
5. ✅ **Deploy** to production environment

See [implementation.md](./checklists/implementation.md) for full feature checklist.
