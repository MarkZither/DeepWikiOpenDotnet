# IVectorStore API Contract

**Purpose**: Abstraction for vector store operations used by Microsoft Agent Framework agents for knowledge retrieval.

---

## Overview

`IVectorStore` provides semantic document retrieval capabilities using k-NN (k-Nearest Neighbors) queries over embedding vectors stored in SQL Server 2025. Methods return JSON-serializable results suitable for Agent Framework tool bindings.

**Namespace**: `DeepWiki.Data.Abstractions`

**Implementations**:
- `SqlServerVectorStore` - SQL Server 2025 with `vector(1536)` column type
- `SqlServerVectorStoreAdapter` - Adapter for legacy EF Core contexts
- Future: PostgreSQL with pgvector

---

## Interface Definition

```csharp
public interface IVectorStore
{
    /// <summary>
    /// Queries the vector store for the k most similar documents to the given embedding.
    /// </summary>
    Task<IReadOnlyList<VectorQueryResult>> QueryAsync(
        float[] embedding,
        int k,
        Dictionary<string, string>? filters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts a document to the vector store. Updates if RepoUrl+FilePath exists.
    /// </summary>
    Task UpsertAsync(
        DocumentDto document,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a document from the vector store by its unique identifier.
    /// </summary>
    Task DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rebuilds the vector store index for optimal query performance.
    /// </summary>
    Task RebuildIndexAsync(CancellationToken cancellationToken = default);
}
```

---

## Methods

### QueryAsync

**Purpose**: Find the k most similar documents to a query embedding using cosine similarity.

**Signature**:
```csharp
Task<IReadOnlyList<VectorQueryResult>> QueryAsync(
    float[] embedding,
    int k,
    Dictionary<string, string>? filters = null,
    CancellationToken cancellationToken = default)
```

**Parameters**:

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `embedding` | `float[]` | Yes | Query embedding vector (must be 1536 dimensions) |
| `k` | `int` | Yes | Maximum number of results to return (1-100) |
| `filters` | `Dictionary<string, string>?` | No | Metadata filters using SQL LIKE patterns |
| `cancellationToken` | `CancellationToken` | No | Cancellation token |

**Returns**: `IReadOnlyList<VectorQueryResult>` - Documents ordered by similarity (highest first)

**Filters**:
- `"repo_url"`: Filter by repository URL (e.g., `"https://github.com/user/%"`)
- `"file_path"`: Filter by file path pattern (e.g., `"%.md"` for markdown)
- Multiple filters are combined with AND logic

**Example**:
```csharp
var embedding = await embeddingService.EmbedAsync("database indexing best practices");
var results = await vectorStore.QueryAsync(
    embedding: embedding,
    k: 5,
    filters: new Dictionary<string, string>
    {
        { "repo_url", "https://github.com/myorg/%" },
        { "file_path", "docs/%.md" }
    });

foreach (var result in results)
{
    Console.WriteLine($"{result.SimilarityScore:F3}: {result.Document.Title}");
}
```

**SQL Execution** (internal):
```sql
SELECT TOP(@k)
    d.Id, d.RepoUrl, d.FilePath, d.Title, d.Text, d.MetadataJson,
    VECTOR_DISTANCE('cosine', d.Embedding, @queryEmbedding) as Distance
FROM Documents d
WHERE d.RepoUrl LIKE @repoFilter
  AND d.FilePath LIKE @pathFilter
ORDER BY Distance ASC;
```

**Errors**:
- `ArgumentException`: If embedding dimension ≠ 1536
- `InvalidOperationException`: If vector store not initialized
- `OperationCanceledException`: If cancelled

---

### UpsertAsync

**Purpose**: Insert or update a document with its embedding. Updates if a document with the same `RepoUrl` and `FilePath` exists.

**Signature**:
```csharp
Task UpsertAsync(
    DocumentDto document,
    CancellationToken cancellationToken = default)
```

**Parameters**:

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `document` | `DocumentDto` | Yes | Document to upsert with embedding |
| `cancellationToken` | `CancellationToken` | No | Cancellation token |

**Document Fields**:
- `Id` (Guid): Auto-generated if empty
- `RepoUrl` (string): Repository URL (required, uniqueness key part 1)
- `FilePath` (string): File path within repo (required, uniqueness key part 2)
- `Title` (string): Document title
- `Text` (string): Full document content
- `Embedding` (float[]): 1536-dim embedding vector
- `MetadataJson` (string): JSON metadata (language, file_type, etc.)

**Example**:
```csharp
var doc = new DocumentDto
{
    RepoUrl = "https://github.com/user/repo",
    FilePath = "docs/architecture.md",
    Title = "Architecture Guide",
    Text = "...",
    Embedding = await embeddingService.EmbedAsync("..."),
    MetadataJson = JsonSerializer.Serialize(new { language = "en", file_type = "markdown" })
};

await vectorStore.UpsertAsync(doc);
```

**Behavior**:
- If document with same `(RepoUrl, FilePath)` exists: **UPDATE** (atomic)
- If document does not exist: **INSERT**
- `UpdatedAt` timestamp is always refreshed
- `CreatedAt` is set only on insert

**Errors**:
- `ArgumentException`: If embedding dimension ≠ 1536 or required fields missing
- `DbUpdateException`: If database constraint violated

---

### DeleteAsync

**Purpose**: Remove a document from the vector store by its unique identifier.

**Signature**:
```csharp
Task DeleteAsync(
    Guid id,
    CancellationToken cancellationToken = default)
```

**Parameters**:

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | `Guid` | Yes | Document ID to delete |
| `cancellationToken` | `CancellationToken` | No | Cancellation token |

**Example**:
```csharp
await vectorStore.DeleteAsync(documentId);
```

**Behavior**:
- No-op if document doesn't exist (no error thrown)
- Delete is permanent (no soft-delete in MVP)

---

### RebuildIndexAsync

**Purpose**: Rebuild vector index for optimal query performance. May be no-op if indexing is automatic.

**Signature**:
```csharp
Task RebuildIndexAsync(CancellationToken cancellationToken = default)
```

**Example**:
```csharp
// After bulk ingestion, rebuild index
await vectorStore.RebuildIndexAsync();
```

**SQL Execution** (internal):
```sql
ALTER INDEX IX_Documents_Embedding ON Documents REBUILD;
```

---

## Models

### DocumentDto

```csharp
public class DocumentDto
{
    public Guid Id { get; set; }
    public string RepoUrl { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public float[] Embedding { get; set; } = Array.Empty<float>();
    public string MetadataJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int TokenCount { get; set; }
    public string FileType { get; set; } = string.Empty;
    public bool IsCode { get; set; }
    public bool IsImplementation { get; set; }
}
```

### VectorQueryResult

```csharp
public class VectorQueryResult
{
    public DocumentDto Document { get; set; } = new();
    public float SimilarityScore { get; set; }  // 0.0 to 1.0, higher = more similar
}
```

---

## Dependency Injection

### Register in Program.cs

```csharp
// SQL Server implementation
services.AddScoped<IVectorStore>(sp =>
{
    var context = sp.GetRequiredService<VectorDbContext>();
    var logger = sp.GetRequiredService<ILogger<SqlServerVectorStore>>();
    return new SqlServerVectorStore(context, logger);
});
```

### Configuration

**appsettings.json**:
```json
{
  "ConnectionStrings": {
    "VectorStore": "Server=localhost;Database=DeepWiki;Trusted_Connection=true;TrustServerCertificate=true;"
  },
  "VectorStore": {
    "EmbeddingDimension": 1536,
    "DefaultK": 10,
    "QueryTimeoutSeconds": 30
  }
}
```

---

## Performance Characteristics

| Operation | Typical Latency | 10k Documents |
|-----------|----------------|---------------|
| QueryAsync (k=10) | 150-300ms | <500ms p95 |
| UpsertAsync | 50-100ms | N/A |
| DeleteAsync | 20-50ms | N/A |
| RebuildIndexAsync | 1-5 seconds | 10-30 seconds |

**Recommendations**:
- Use filters to narrow search space before similarity
- Batch upserts through `IDocumentIngestionService` for efficiency
- Rebuild index after large batch ingestions

---

## See Also

- [IEmbeddingService](./IEmbeddingService.md) - Generate embeddings for queries
- [ITokenizationService](./ITokenizationService.md) - Token counting before embedding
- [IDocumentIngestionService](./IDocumentIngestionService.md) - Batch document ingestion
- [Agent Integration](./agent-integration.md) - Using with Microsoft Agent Framework
