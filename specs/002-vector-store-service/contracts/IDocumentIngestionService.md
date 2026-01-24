# IDocumentIngestionService API Contract

**Purpose**: Microsoft Agent Framework-compatible document ingestion service for orchestrating document chunking, embedding, and vector store upsert operations.

---

## Overview

`IDocumentIngestionService` provides batch document ingestion with automatic chunking, embedding generation, and vector store persistence. Supports duplicate detection, concurrent write handling, and resilient error recovery.

**Namespace**: `DeepWiki.Data.Abstractions`

**Implementations**:
- `DocumentIngestionService` - Main implementation orchestrating chunk → embed → upsert

---

## Interface Definition

```csharp
public interface IDocumentIngestionService
{
    /// <summary>
    /// Ingests a batch of documents by chunking, embedding, and upserting.
    /// </summary>
    Task<IngestionResult> IngestAsync(
        IngestionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts a single document with atomic transaction semantics.
    /// </summary>
    Task<DocumentDto> UpsertAsync(
        DocumentDto document,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Chunks text and generates embeddings for each chunk.
    /// </summary>
    Task<IReadOnlyList<ChunkEmbeddingResult>> ChunkAndEmbedAsync(
        string text,
        int maxTokensPerChunk = 8192,
        Guid? parentDocumentId = null,
        CancellationToken cancellationToken = default);
}
```

---

## Methods

### IngestAsync

**Purpose**: Ingest a batch of documents with full pipeline: validate → chunk → embed → upsert. Continues processing on individual failures for batch resilience.

**Signature**:
```csharp
Task<IngestionResult> IngestAsync(
    IngestionRequest request,
    CancellationToken cancellationToken = default)
```

**Parameters**:

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `request` | `IngestionRequest` | Yes | Batch ingestion configuration |
| `cancellationToken` | `CancellationToken` | No | Cancellation token |

**Returns**: `IngestionResult` - Success/failure counts, errors, and ingested document IDs

**Example**:
```csharp
var request = IngestionRequest.Create(new[]
{
    new IngestionDocument
    {
        RepoUrl = "https://github.com/user/repo",
        FilePath = "docs/architecture.md",
        Text = "# Architecture\n\nThis document describes..."
    },
    new IngestionDocument
    {
        RepoUrl = "https://github.com/user/repo",
        FilePath = "docs/getting-started.md",
        Text = "# Getting Started\n\nFollow these steps..."
    }
});

var result = await ingestionService.IngestAsync(request);

Console.WriteLine($"Ingested: {result.SuccessCount}");
Console.WriteLine($"Failed: {result.FailureCount}");
Console.WriteLine($"Duration: {result.DurationMs}ms");

foreach (var error in result.Errors)
{
    Console.WriteLine($"Error in {error.FilePath}: {error.Message}");
}
```

**Ingestion Pipeline**:
1. **Validate**: Check required fields (RepoUrl, FilePath, Text)
2. **Token Count**: Validate text size, chunk if needed
3. **Chunk**: Split large documents respecting token limits (8192 default)
4. **Embed**: Generate embeddings via `IEmbeddingService.EmbedBatchAsync`
5. **Enrich**: Add metadata (language, file_type, chunk_index)
6. **Upsert**: Persist to vector store via `IVectorStore.UpsertAsync`

**Duplicate Handling**:
- Documents with same `(RepoUrl, FilePath)` are **updated**, not duplicated
- Update refreshes: Text, Embedding, MetadataJson, UpdatedAt
- CreatedAt preserved from original insert

**Error Handling**:
- If `ContinueOnError = true` (default): logs error, continues with next document
- If `ContinueOnError = false`: stops on first error
- All errors collected in `IngestionResult.Errors`

---

### UpsertAsync

**Purpose**: Upsert a single document with atomic transaction semantics.

**Signature**:
```csharp
Task<DocumentDto> UpsertAsync(
    DocumentDto document,
    CancellationToken cancellationToken = default)
```

**Parameters**:

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `document` | `DocumentDto` | Yes | Document to upsert (with embedding) |
| `cancellationToken` | `CancellationToken` | No | Cancellation token |

**Returns**: `DocumentDto` - The upserted document with updated IDs and timestamps

**Example**:
```csharp
var embedding = await embeddingService.EmbedAsync(documentText);

var doc = new DocumentDto
{
    RepoUrl = "https://github.com/user/repo",
    FilePath = "docs/new-doc.md",
    Title = "New Document",
    Text = documentText,
    Embedding = embedding,
    MetadataJson = JsonSerializer.Serialize(new { language = "en" })
};

var result = await ingestionService.UpsertAsync(doc);
Console.WriteLine($"Document ID: {result.Id}");
Console.WriteLine($"Updated at: {result.UpdatedAt}");
```

**Atomicity**:
- Uses database transaction for insert/update
- Rolls back on any error
- Concurrent writes to same `(RepoUrl, FilePath)` use "first write wins" with atomic update

---

### ChunkAndEmbedAsync

**Purpose**: Chunk text and generate embeddings for each chunk. Useful for preprocessing before manual upsert.

**Signature**:
```csharp
Task<IReadOnlyList<ChunkEmbeddingResult>> ChunkAndEmbedAsync(
    string text,
    int maxTokensPerChunk = 8192,
    Guid? parentDocumentId = null,
    CancellationToken cancellationToken = default)
```

**Parameters**:

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `text` | `string` | Yes | - | Text to chunk and embed |
| `maxTokensPerChunk` | `int` | No | 8192 | Maximum tokens per chunk |
| `parentDocumentId` | `Guid?` | No | null | Parent document ID for metadata |
| `cancellationToken` | `CancellationToken` | No | - | Cancellation token |

**Returns**: `IReadOnlyList<ChunkEmbeddingResult>` - Chunks with embeddings and metadata

**Example**:
```csharp
var largeDocument = File.ReadAllText("docs/large-manual.md");
var parentId = Guid.NewGuid();

var chunks = await ingestionService.ChunkAndEmbedAsync(
    largeDocument,
    maxTokensPerChunk: 8192,
    parentDocumentId: parentId);

Console.WriteLine($"Created {chunks.Count} chunks");

foreach (var chunk in chunks)
{
    Console.WriteLine($"Chunk {chunk.ChunkIndex}: {chunk.TokenCount} tokens, {chunk.EmbeddingLatencyMs}ms");
    
    // Manually upsert each chunk
    var doc = new DocumentDto
    {
        RepoUrl = "https://example.com",
        FilePath = $"docs/large-manual.md#chunk-{chunk.ChunkIndex}",
        Text = chunk.Text,
        Embedding = chunk.Embedding,
        MetadataJson = JsonSerializer.Serialize(new
        {
            chunk_index = chunk.ChunkIndex,
            parent_id = parentId,
            language = chunk.Language
        })
    };
    await vectorStore.UpsertAsync(doc);
}
```

---

## Models

### IngestionRequest

```csharp
public sealed class IngestionRequest
{
    /// <summary>
    /// Documents to ingest.
    /// </summary>
    public IReadOnlyList<IngestionDocument> Documents { get; init; } = [];
    
    /// <summary>
    /// Batch size for embedding operations (default: 10, max: 100).
    /// </summary>
    public int BatchSize { get; init; } = 10;
    
    /// <summary>
    /// Maximum retry count for failed operations.
    /// </summary>
    public int MaxRetries { get; init; } = 3;
    
    /// <summary>
    /// Default metadata to apply to all documents.
    /// </summary>
    public Dictionary<string, string>? MetadataDefaults { get; init; }
    
    /// <summary>
    /// Continue processing if individual document fails.
    /// </summary>
    public bool ContinueOnError { get; init; } = true;
    
    /// <summary>
    /// Maximum tokens per chunk (default: 8192).
    /// </summary>
    public int MaxTokensPerChunk { get; init; } = 8192;
    
    /// <summary>
    /// Skip embedding (for testing only).
    /// </summary>
    public bool SkipEmbedding { get; init; }
}
```

### IngestionDocument

```csharp
public sealed class IngestionDocument
{
    /// <summary>
    /// Repository URL.
    /// </summary>
    public string RepoUrl { get; init; } = string.Empty;
    
    /// <summary>
    /// File path within repository.
    /// </summary>
    public string FilePath { get; init; } = string.Empty;
    
    /// <summary>
    /// Document text content.
    /// </summary>
    public string Text { get; init; } = string.Empty;
    
    /// <summary>
    /// Document title (optional, defaults to filename).
    /// </summary>
    public string? Title { get; init; }
    
    /// <summary>
    /// Additional metadata.
    /// </summary>
    public Dictionary<string, string>? Metadata { get; init; }
}
```

### IngestionResult

```csharp
public sealed class IngestionResult
{
    /// <summary>
    /// Number of successfully ingested documents.
    /// </summary>
    public int SuccessCount { get; init; }
    
    /// <summary>
    /// Number of failed ingestions.
    /// </summary>
    public int FailureCount { get; init; }
    
    /// <summary>
    /// Total chunks created across all documents.
    /// </summary>
    public int TotalChunks { get; init; }
    
    /// <summary>
    /// Errors encountered during ingestion.
    /// </summary>
    public IReadOnlyList<IngestionError> Errors { get; init; } = [];
    
    /// <summary>
    /// Total duration in milliseconds.
    /// </summary>
    public long DurationMs { get; init; }
    
    /// <summary>
    /// IDs of successfully ingested documents.
    /// </summary>
    public IReadOnlyList<Guid> IngestedDocumentIds { get; init; } = [];
    
    /// <summary>
    /// Whether all documents succeeded.
    /// </summary>
    public bool IsFullySuccessful => FailureCount == 0 && SuccessCount > 0;
}
```

### IngestionError

```csharp
public sealed class IngestionError
{
    /// <summary>
    /// Repository URL of failed document.
    /// </summary>
    public string RepoUrl { get; init; } = string.Empty;
    
    /// <summary>
    /// File path of failed document.
    /// </summary>
    public string FilePath { get; init; } = string.Empty;
    
    /// <summary>
    /// Error message.
    /// </summary>
    public string Message { get; init; } = string.Empty;
    
    /// <summary>
    /// Error category (Validation, Embedding, Storage).
    /// </summary>
    public string Category { get; init; } = "Unknown";
}
```

### ChunkEmbeddingResult

```csharp
public sealed class ChunkEmbeddingResult
{
    /// <summary>
    /// Chunk text content.
    /// </summary>
    public string Text { get; init; } = string.Empty;
    
    /// <summary>
    /// Embedding vector (1536 dimensions).
    /// </summary>
    public float[] Embedding { get; init; } = Array.Empty<float>();
    
    /// <summary>
    /// Zero-based chunk index.
    /// </summary>
    public int ChunkIndex { get; init; }
    
    /// <summary>
    /// Parent document ID.
    /// </summary>
    public Guid? ParentDocumentId { get; init; }
    
    /// <summary>
    /// Token count in chunk.
    /// </summary>
    public int TokenCount { get; init; }
    
    /// <summary>
    /// Detected language.
    /// </summary>
    public string Language { get; init; } = "en";
    
    /// <summary>
    /// Embedding latency in milliseconds.
    /// </summary>
    public long EmbeddingLatencyMs { get; init; }
}
```

---

## Dependency Injection

### Register in Program.cs

```csharp
// Register dependencies
services.AddScoped<IVectorStore, SqlServerVectorStore>();
services.AddScoped<IEmbeddingService>(sp => factory.Create());
services.AddScoped<ITokenizationService, TokenizationService>();

// Register ingestion service
services.AddScoped<IDocumentIngestionService, DocumentIngestionService>();
```

### Configuration

```json
{
  "Ingestion": {
    "DefaultBatchSize": 10,
    "MaxTokensPerChunk": 8192,
    "MaxRetries": 3,
    "ContinueOnError": true
  }
}
```

---

## Metadata Enrichment

The ingestion service automatically enriches documents with metadata:

```csharp
// Auto-detected metadata
{
    "language": "en",           // Detected from content
    "file_type": "markdown",    // From file extension
    "chunk_index": 0,           // If chunked
    "chunk_count": 3,           // Total chunks
    "parent_id": "...",         // If chunked
    "token_count": 1234,        // Tokens in chunk
    "ingested_at": "2026-01-18T12:00:00Z"
}
```

---

## Performance Characteristics

| Operation | Typical Latency | Notes |
|-----------|----------------|-------|
| IngestAsync (10 docs) | 2-5 seconds | Includes embedding |
| IngestAsync (100 docs) | 10-30 seconds | Batched embedding |
| UpsertAsync | 100-500ms | Single document |
| ChunkAndEmbedAsync | 500ms-2s | Depends on text size |

**Throughput Target**: ≥50 documents/second (with batching)

---

## Error Handling

```csharp
var result = await ingestionService.IngestAsync(request);

if (!result.IsFullySuccessful)
{
    foreach (var error in result.Errors)
    {
        switch (error.Category)
        {
            case "Validation":
                // Invalid input (missing fields, too large)
                logger.LogWarning("Validation error: {file} - {msg}",
                    error.FilePath, error.Message);
                break;
                
            case "Embedding":
                // Embedding service failed
                logger.LogError("Embedding failed: {file} - {msg}",
                    error.FilePath, error.Message);
                // May retry later
                break;
                
            case "Storage":
                // Database error
                logger.LogError("Storage error: {file} - {msg}",
                    error.FilePath, error.Message);
                break;
        }
    }
}
```

---

## See Also

- [IVectorStore](./IVectorStore.md) - Vector store operations
- [IEmbeddingService](./IEmbeddingService.md) - Embedding generation
- [ITokenizationService](./ITokenizationService.md) - Token counting and chunking
- [data-model](../data-model.md) - Document entity schema
- [Agent Integration](./agent-integration.md) - Using with Microsoft Agent Framework
