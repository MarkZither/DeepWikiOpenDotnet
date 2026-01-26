# IPersistenceVectorStore Interface Specification

**Version**: 1.0.0  
**Date**: 2026-01-16  
**Location**: `src/DeepWiki.Data/Interfaces/IPersistenceVectorStore.cs`

---

## Purpose

Abstracts vector similarity search operations for document embeddings, enabling semantic search across knowledge base documents. Supports multiple database providers (SQL Server, PostgreSQL) with provider-specific ANN index optimizations while maintaining a consistent API.

---

## Interface Definition

```csharp
namespace DeepWiki.Data.Interfaces;

/// <summary>
/// Provides vector similarity search operations for document embeddings.
/// </summary>
public interface IPersistenceVectorStore
{
    /// <summary>
    /// Inserts a new document or updates an existing document based on Id.
    /// </summary>
    /// <param name="document">Document entity with embedding vector.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task representing the async operation.</returns>
    /// <exception cref="ArgumentNullException">If document is null.</exception>
    /// <exception cref="ArgumentException">If embedding dimensions invalid (not 1536 when non-null).</exception>
    /// <exception cref="DbUpdateException">If database operation fails.</exception>
    Task UpsertAsync(
        DocumentEntity document, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries for the k nearest documents to the query embedding using cosine similarity.
    /// </summary>
    /// <param name="queryEmbedding">Query vector (must be 1536 dimensions).</param>
    /// <param name="k">Number of nearest neighbors to return (default 10).</param>
    /// <param name="repoUrlFilter">Optional filter to only search within specific repository.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of k nearest documents ordered by similarity (closest first).</returns>
    /// <exception cref="ArgumentNullException">If queryEmbedding is null.</exception>
    /// <exception cref="ArgumentException">If queryEmbedding is not 1536 dimensions or k < 1.</exception>
    /// <exception cref="InvalidOperationException">If no documents with embeddings exist.</exception>
    Task<List<DocumentEntity>> QueryNearestAsync(
        float[] queryEmbedding,
        int k = 10,
        string? repoUrlFilter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a document by its unique identifier.
    /// </summary>
    /// <param name="id">Document unique identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task representing the async operation.</returns>
    /// <exception cref="InvalidOperationException">If document with id does not exist.</exception>
    Task DeleteAsync(
        Guid id, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all documents belonging to a specific repository.
    /// </summary>
    /// <param name="repoUrl">Repository URL to filter deletions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task representing the async operation.</returns>
    /// <exception cref="ArgumentException">If repoUrl is null or empty.</exception>
    Task DeleteByRepoAsync(
        string repoUrl, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts total documents, optionally filtered by repository.
    /// </summary>
    /// <param name="repoUrlFilter">Optional repository filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Count of documents matching filter.</returns>
    Task<int> CountAsync(
        string? repoUrlFilter = null, 
        CancellationToken cancellationToken = default);
}
```

---

## Method Specifications

### UpsertAsync

**Behavior**:
- If document with `Id` exists: Update all properties including embedding
- If document does not exist: Insert new document
- Automatically sets `UpdatedAt` to current UTC time
- Validates embedding dimensions (exactly 1536 floats when non-null)

**Performance Requirements**:
- Single document upsert: <100ms for local database
- Bulk upsert (1000 docs): <10 seconds with batching

**Concurrency**:
- Uses optimistic concurrency via `UpdatedAt` timestamp
- Throws `DbUpdateConcurrencyException` if document modified between read and write

**Example Usage**:
```csharp
var document = new DocumentEntity
{
    Id = Guid.NewGuid(),
    RepoUrl = "https://github.com/org/repo",
    FilePath = "src/Program.cs",
    Title = "Program.cs",
    Text = "using System; ...",
    Embedding = new float[1536], // Pre-computed embedding
    IsCode = true,
    TokenCount = 150
};

await vectorStore.UpsertAsync(document);
```

---

### QueryNearestAsync

**Behavior**:
- Returns k documents with smallest cosine distance to query embedding
- Results ordered by distance ascending (most similar first)
- Documents without embeddings automatically excluded
- If fewer than k documents exist (or match filter), returns all available
- Empty list returned if no documents with embeddings (not exception)

**Performance Requirements**:
- 10K documents: <500ms
- 3M documents: <2 seconds (with HNSW indexing)

**Distance Metric**:
- Cosine distance: `distance = 1 - cosine_similarity`
- Range [0, 2] where 0 = identical, 1 = orthogonal, 2 = opposite

**Example Usage**:
```csharp
float[] queryEmbedding = await embeddingService.GenerateAsync("How to authenticate users?");

var results = await vectorStore.QueryNearestAsync(
    queryEmbedding: queryEmbedding,
    k: 10,
    repoUrlFilter: "https://github.com/org/auth-service");

foreach (var doc in results)
{
    Console.WriteLine($"{doc.Title}: {doc.Text.Substring(0, 200)}...");
}
```

**Provider-Specific Implementation Notes**:
- **SQL Server**: Uses `VECTOR_DISTANCE('cosine', Embedding, @query)` function
- **PostgreSQL**: Uses `Embedding <=> @query` operator (cosine distance)

---

### DeleteAsync

**Behavior**:
- Permanently deletes document (no soft delete)
- Throws `InvalidOperationException` if document does not exist
- Cascade deletes not applicable (no related entities in v1)

**Example Usage**:
```csharp
await vectorStore.DeleteAsync(documentId);
```

---

### DeleteByRepoAsync

**Behavior**:
- Deletes all documents where `RepoUrl` matches exactly
- Case-sensitive match
- No-op if no documents match (succeeds without error)
- Batch deletion for performance (all deleted in single transaction)

**Performance Requirements**:
- 1000 documents: <5 seconds

**Example Usage**:
```csharp
await vectorStore.DeleteByRepoAsync("https://github.com/old-org/deprecated-repo");
```

---

### CountAsync

**Behavior**:
- Counts all documents if `repoUrlFilter` is null
- Counts documents in specific repository if filter provided
- Includes documents with and without embeddings

**Example Usage**:
```csharp
int totalDocs = await vectorStore.CountAsync();
int repoDocs = await vectorStore.CountAsync("https://github.com/org/repo");
```

---

## Error Handling

| Exception | Thrown When | Recovery Strategy |
|-----------|-------------|-------------------|
| `ArgumentNullException` | Null document or queryEmbedding | Fix caller code |
| `ArgumentException` | Invalid embedding dimensions or k < 1 | Validate inputs before call |
| `InvalidOperationException` | Delete non-existent document | Check existence first or ignore |
| `DbUpdateException` | Database constraints violated | Log error, retry transaction |
| `DbUpdateConcurrencyException` | Concurrent update conflict | Reload entity and retry |
| `TimeoutException` | Database operation timeout | Retry with exponential backoff |

---

## Testing Requirements

**Unit Tests** (with mocked DbContext):
- ✅ UpsertAsync creates new document
- ✅ UpsertAsync updates existing document
- ✅ UpsertAsync throws ArgumentException for invalid embedding dimensions
- ✅ QueryNearestAsync returns correct number of results
- ✅ QueryNearestAsync filters by repository correctly
- ✅ QueryNearestAsync returns empty list when no embeddings exist
- ✅ DeleteAsync removes document
- ✅ DeleteByRepoAsync removes all repo documents
- ✅ CountAsync returns correct counts with and without filters

**Integration Tests** (with Testcontainers):
- ✅ UpsertAsync persists document with embedding to database
- ✅ QueryNearestAsync orders results by cosine similarity
- ✅ QueryNearestAsync with identical embedding returns distance ≈ 0
- ✅ Vector index improves query performance (benchmark test)
- ✅ Concurrent upserts handle optimistic concurrency correctly
- ✅ DeleteByRepoAsync deletes 1000+ documents performantly
- ✅ Same test suite passes for both SQL Server and PostgreSQL (100% parity)

---

## Implementation Checklist

- [ ] Create `IVectorStore.cs` in `DeepWiki.Data/Interfaces/`
- [ ] Implement `SqlServerVectorStore` with `VECTOR_DISTANCE()` queries
- [ ] Implement `PostgresVectorStore` with `<=>` operator queries
- [ ] Add XML documentation comments to all public methods
- [ ] Write unit tests for argument validation
- [ ] Write integration tests with Testcontainers
- [ ] Document DI registration patterns in quickstart.md
- [ ] Add performance benchmarks for 10K and 3M document scales

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0.0 | 2026-01-16 | Initial specification |
