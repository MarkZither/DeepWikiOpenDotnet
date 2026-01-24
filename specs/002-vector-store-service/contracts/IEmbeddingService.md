# IEmbeddingService API Contract

**Purpose**: Microsoft Agent Framework-compatible embedding service for converting text to vector embeddings. Supports OpenAI, Microsoft AI Foundry, and Ollama providers with resilient retry and fallback strategies.

---

## Overview

`IEmbeddingService` converts text into 1536-dimensional vector embeddings suitable for semantic similarity search. Supports multiple providers with automatic retry, exponential backoff, and cache fallback.

**Namespace**: `DeepWiki.Data.Abstractions`

**Implementations**:
- `OpenAIEmbeddingClient` - OpenAI API (direct or Azure OpenAI)
- `FoundryEmbeddingClient` - Microsoft AI Foundry / Foundry Local
- `OllamaEmbeddingClient` - Local Ollama instance

---

## Interface Definition

```csharp
public interface IEmbeddingService
{
    /// <summary>
    /// Gets the name of the current embedding provider.
    /// </summary>
    string Provider { get; }

    /// <summary>
    /// Gets the model ID used for embeddings.
    /// </summary>
    string ModelId { get; }

    /// <summary>
    /// Gets the expected dimensionality of embeddings (typically 1536).
    /// </summary>
    int EmbeddingDimension { get; }

    /// <summary>
    /// Converts text into a vector embedding.
    /// </summary>
    Task<float[]> EmbedAsync(
        string text,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts multiple texts into vector embeddings efficiently.
    /// </summary>
    IAsyncEnumerable<float[]> EmbedBatchAsync(
        IEnumerable<string> texts,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch embedding with full metadata response.
    /// </summary>
    Task<IReadOnlyList<EmbeddingResponse>> EmbedBatchWithMetadataAsync(
        IEnumerable<string> texts,
        int batchSize = 10,
        CancellationToken cancellationToken = default);
}
```

---

## Methods

### EmbedAsync

**Purpose**: Convert a single text string into a 1536-dimensional vector embedding.

**Signature**:
```csharp
Task<float[]> EmbedAsync(
    string text,
    CancellationToken cancellationToken = default)
```

**Parameters**:

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `text` | `string` | Yes | Text to embed (must be ≤8192 tokens) |
| `cancellationToken` | `CancellationToken` | No | Cancellation token |

**Returns**: `float[]` - 1536-dimensional embedding vector

**Example**:
```csharp
var embedding = await embeddingService.EmbedAsync(
    "What are best practices for database indexing?");

Console.WriteLine($"Embedding dimension: {embedding.Length}"); // 1536
Console.WriteLine($"First 5 values: [{string.Join(", ", embedding.Take(5))}]");
```

**Resilience**:
- **Retry**: 3 attempts with exponential backoff (100ms, 200ms, 400ms)
- **Jitter**: ±20% randomization on delays
- **Fallback**: Returns cached embedding if available after all retries fail
- **Timeout**: Fails if total time exceeds 10 seconds

**Errors**:
- `InvalidOperationException`: Provider failed after all retries and no cache available
- `ArgumentException`: Text is empty or exceeds token limit
- `OperationCanceledException`: If cancelled

---

### EmbedBatchAsync

**Purpose**: Convert multiple texts to embeddings efficiently using batching.

**Signature**:
```csharp
IAsyncEnumerable<float[]> EmbedBatchAsync(
    IEnumerable<string> texts,
    CancellationToken cancellationToken = default)
```

**Parameters**:

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `texts` | `IEnumerable<string>` | Yes | Texts to embed |
| `cancellationToken` | `CancellationToken` | No | Cancellation token |

**Returns**: `IAsyncEnumerable<float[]>` - Stream of embeddings in order

**Example**:
```csharp
var documents = new[] { "Doc 1 text...", "Doc 2 text...", "Doc 3 text..." };

await foreach (var embedding in embeddingService.EmbedBatchAsync(documents))
{
    Console.WriteLine($"Got embedding: {embedding.Length} dimensions");
}
```

**Batching Behavior**:
- Default batch size: 10 texts per API call
- Maximum batch size: 100 texts
- Yields embeddings as batches complete
- Order preserved (embedding[i] corresponds to texts[i])

---

### EmbedBatchWithMetadataAsync

**Purpose**: Batch embedding with full response metadata (latency, retry count, etc.).

**Signature**:
```csharp
Task<IReadOnlyList<EmbeddingResponse>> EmbedBatchWithMetadataAsync(
    IEnumerable<string> texts,
    int batchSize = 10,
    CancellationToken cancellationToken = default)
```

**Parameters**:

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `texts` | `IEnumerable<string>` | Yes | - | Texts to embed |
| `batchSize` | `int` | No | 10 | Texts per API batch (1-100) |
| `cancellationToken` | `CancellationToken` | No | - | Cancellation token |

**Returns**: `IReadOnlyList<EmbeddingResponse>` - Embeddings with metadata

**Example**:
```csharp
var texts = documents.Select(d => d.Text);
var results = await embeddingService.EmbedBatchWithMetadataAsync(
    texts,
    batchSize: 20);

foreach (var result in results)
{
    Console.WriteLine($"Provider: {result.Provider}");
    Console.WriteLine($"Latency: {result.LatencyMs}ms");
    Console.WriteLine($"Cached: {result.FromCache}");
    Console.WriteLine($"Retries: {result.RetryAttempts}");
}
```

---

## Models

### EmbeddingResponse

```csharp
public sealed class EmbeddingResponse
{
    /// <summary>
    /// The embedding vector (typically 1536 dimensions).
    /// </summary>
    public required float[] Vector { get; init; }

    /// <summary>
    /// Provider that generated this embedding.
    /// </summary>
    public required string Provider { get; init; }

    /// <summary>
    /// Model ID used for this embedding.
    /// </summary>
    public required string ModelId { get; init; }

    /// <summary>
    /// Latency in milliseconds.
    /// </summary>
    public long LatencyMs { get; init; }

    /// <summary>
    /// Token count of input (if available).
    /// </summary>
    public int? TokenCount { get; init; }

    /// <summary>
    /// Whether served from cache.
    /// </summary>
    public bool FromCache { get; init; }

    /// <summary>
    /// Number of retry attempts before success.
    /// </summary>
    public int RetryAttempts { get; init; }

    /// <summary>
    /// Embedding dimensionality.
    /// </summary>
    public int Dimension => Vector?.Length ?? 0;
}
```

---

## Provider Configuration

### OpenAI

**appsettings.json**:
```json
{
  "Embedding": {
    "Provider": "openai",
    "OpenAI": {
      "ApiKey": "sk-...",
      "ModelId": "text-embedding-3-small",
      "Endpoint": null  // Use default OpenAI endpoint
    },
    "MaxRetries": 3,
    "BaseDelayMs": 100,
    "MaxDelayMs": 10000
  }
}
```

**Environment Variables**:
```bash
EMBEDDING_PROVIDER=openai
OPENAI_API_KEY=sk-...
```

### Microsoft AI Foundry

**appsettings.json**:
```json
{
  "Embedding": {
    "Provider": "foundry",
    "Foundry": {
      "Endpoint": "https://your-foundry.ai.azure.com",
      "ApiKey": "...",  // Or use managed identity
      "DeploymentName": "text-embedding-ada-002",
      "ModelId": "text-embedding-ada-002"
    }
  }
}
```

**Environment Variables**:
```bash
EMBEDDING_PROVIDER=foundry
AZURE_OPENAI_ENDPOINT=https://your-foundry.ai.azure.com
AZURE_OPENAI_API_KEY=...
```

**Foundry Local** (for local development):
```json
{
  "Embedding": {
    "Provider": "foundry",
    "Foundry": {
      "Endpoint": "http://localhost:8000",
      "ApiKey": "local-dev-key"
    }
  }
}
```

### Ollama

**appsettings.json**:
```json
{
  "Embedding": {
    "Provider": "ollama",
    "Ollama": {
      "Endpoint": "http://localhost:11434",
      "ModelId": "nomic-embed-text"
    }
  }
}
```

**Environment Variables**:
```bash
EMBEDDING_PROVIDER=ollama
OLLAMA_ENDPOINT=http://localhost:11434
OLLAMA_MODEL=nomic-embed-text
```

**Starting Ollama locally**:
```bash
# Install Ollama (https://ollama.ai)
ollama serve

# Pull embedding model
ollama pull nomic-embed-text
```

---

## Dependency Injection

### Using EmbeddingServiceFactory

```csharp
// Register factory
services.AddSingleton<EmbeddingServiceFactory>();

// Register IEmbeddingService using factory
services.AddScoped<IEmbeddingService>(sp =>
{
    var factory = sp.GetRequiredService<EmbeddingServiceFactory>();
    return factory.Create();  // Creates based on configuration
});
```

### Direct Registration

```csharp
// OpenAI
services.AddScoped<IEmbeddingService>(sp =>
    new OpenAIEmbeddingClient(
        apiKey: configuration["Embedding:OpenAI:ApiKey"],
        modelId: "text-embedding-3-small",
        retryPolicy: new RetryPolicy()));

// Foundry
services.AddScoped<IEmbeddingService>(sp =>
    new FoundryEmbeddingClient(
        endpoint: configuration["Embedding:Foundry:Endpoint"],
        deploymentName: "text-embedding-ada-002",
        retryPolicy: new RetryPolicy()));

// Ollama
services.AddScoped<IEmbeddingService>(sp =>
    new OllamaEmbeddingClient(
        endpoint: "http://localhost:11434",
        modelId: "nomic-embed-text",
        retryPolicy: new RetryPolicy()));
```

---

## Retry Policy

The `RetryPolicy` handles transient failures:

```csharp
public class RetryPolicy
{
    public int MaxRetries { get; init; } = 3;
    public int BaseDelayMs { get; init; } = 100;
    public int MaxDelayMs { get; init; } = 10_000;
    public bool UseCacheFallback { get; init; } = true;
}
```

**Retry Schedule**:
1. Attempt 1: Immediate
2. Attempt 2: Wait ~100ms (with jitter)
3. Attempt 3: Wait ~200ms (with jitter)
4. Fallback: Check cache for existing embedding

**Jitter**: ±20% randomization prevents thundering herd

---

## Embedding Cache

Optional `IEmbeddingCache` stores embeddings for fallback:

```csharp
public interface IEmbeddingCache
{
    Task<float[]?> GetAsync(string textHash, CancellationToken ct = default);
    Task SetAsync(string textHash, float[] embedding, CancellationToken ct = default);
}
```

**Default Implementation**: `EmbeddingCache` (in-memory with TTL)

```csharp
services.AddSingleton<IEmbeddingCache>(sp =>
    new EmbeddingCache(
        maxEntries: 10_000,
        ttlMinutes: 60));
```

---

## Performance Characteristics

| Operation | Typical Latency | Notes |
|-----------|----------------|-------|
| EmbedAsync (OpenAI) | 50-200ms | Network latency dependent |
| EmbedAsync (Foundry) | 50-200ms | Similar to OpenAI |
| EmbedAsync (Ollama local) | 20-100ms | Faster local execution |
| EmbedBatchAsync (10 texts) | 100-500ms | More efficient than individual calls |
| Throughput target | ≥50 docs/sec | With batching |

---

## Error Handling

```csharp
try
{
    var embedding = await embeddingService.EmbedAsync(text);
}
catch (InvalidOperationException ex)
{
    // Provider failed after all retries, no cache available
    // Message includes provider context: "OpenAI embedding failed after 3 retries: {error}"
    logger.LogError(ex, "Embedding failed for document");
    
    // Options:
    // 1. Skip this document
    // 2. Queue for retry later
    // 3. Return error to agent
}
catch (ArgumentException ex)
{
    // Invalid input (empty text, too many tokens)
    logger.LogWarning("Invalid embedding input: {error}", ex.Message);
}
```

---

## Structured Logging

All providers emit structured logs:

```csharp
// Success
logger.LogInformation(
    "Embedding completed: Provider={Provider}, Model={ModelId}, " +
    "Tokens={TokenCount}, LatencyMs={LatencyMs}, Retries={RetryCount}",
    provider, modelId, tokenCount, latencyMs, retryCount);

// Failure with retry
logger.LogWarning(
    "Embedding failed, retrying ({Attempt}/{MaxRetries}): {Error}",
    attempt, maxRetries, error);

// Final failure
logger.LogError(
    "Embedding failed after {MaxRetries} retries: {Error}",
    maxRetries, error);
```

---

## See Also

- [IVectorStore](./IVectorStore.md) - Store and query embeddings
- [ITokenizationService](./ITokenizationService.md) - Token counting before embedding
- [provider-factory](./provider-factory.md) - Provider selection details
- [Agent Integration](./agent-integration.md) - Using with Microsoft Agent Framework
