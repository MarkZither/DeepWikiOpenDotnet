# IDocumentRepository Interface Specification

**Version**: 1.0.0  
**Date**: 2026-01-16  
**Location**: `src/DeepWiki.Data/Interfaces/IDocumentRepository.cs`

---

## Purpose

Provides CRUD (Create, Read, Update, Delete) operations for document entities without vector-specific operations. Complements `IVectorStore` by handling standard database operations like retrieval by ID, listing documents by repository, and basic existence checks.

---

## Interface Definition

```csharp
namespace DeepWiki.Data.Interfaces;

/// <summary>
/// Provides CRUD operations for document entities.
/// </summary>
public interface IDocumentRepository
{
    /// <summary>
    /// Retrieves a document by its unique identifier.
    /// </summary>
    /// <param name="id">Document unique identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Document entity or null if not found.</returns>
    Task<DocumentEntity?> GetByIdAsync(
        Guid id, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all documents belonging to a repository with pagination.
    /// </summary>
    /// <param name="repoUrl">Repository URL filter.</param>
    /// <param name="skip">Number of documents to skip (for pagination).</param>
    /// <param name="take">Number of documents to take (max 1000).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of documents ordered by CreatedAt descending.</returns>
    /// <exception cref="ArgumentException">If repoUrl null/empty or take > 1000.</exception>
    Task<List<DocumentEntity>> GetByRepoAsync(
        string repoUrl,
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new document to the database.
    /// </summary>
    /// <param name="document">Document entity to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task representing the async operation.</returns>
    /// <exception cref="ArgumentNullException">If document is null.</exception>
    /// <exception cref="DbUpdateException">If document with same Id already exists.</exception>
    Task AddAsync(
        DocumentEntity document, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing document in the database.
    /// </summary>
    /// <param name="document">Document entity with updated properties.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task representing the async operation.</returns>
    /// <exception cref="ArgumentNullException">If document is null.</exception>
    /// <exception cref="InvalidOperationException">If document does not exist.</exception>
    /// <exception cref="DbUpdateConcurrencyException">If document modified concurrently.</exception>
    Task UpdateAsync(
        DocumentEntity document, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a document from the database.
    /// </summary>
    /// <param name="id">Document unique identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task representing the async operation.</returns>
    /// <exception cref="InvalidOperationException">If document does not exist.</exception>
    Task DeleteAsync(
        Guid id, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a document exists by its unique identifier.
    /// </summary>
    /// <param name="id">Document unique identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if document exists, false otherwise.</returns>
    Task<bool> ExistsAsync(
        Guid id, 
        CancellationToken cancellationToken = default);
}
```

---

## Method Specifications

### GetByIdAsync

**Behavior**:
- Returns document entity if found
- Returns null if document does not exist (not exception)
- Includes all properties (embedding, metadata, etc.)

**Performance Requirements**:
- <100ms for local database (primary key lookup)

**Example Usage**:
```csharp
var document = await repository.GetByIdAsync(documentId);
if (document != null)
{
    Console.WriteLine($"Found: {document.Title} ({document.FilePath})");
}
else
{
    Console.WriteLine("Document not found");
}
```

---

### GetByRepoAsync

**Behavior**:
- Returns documents ordered by `CreatedAt` descending (newest first)
- Supports pagination via `skip` and `take` parameters
- Maximum `take` value: 1000 (prevents excessive memory usage)
- Returns empty list if no documents match repository URL

**Performance Requirements**:
- <500ms for 10K document repository (with index on RepoUrl)

**Example Usage**:
```csharp
// Get first 100 documents
var page1 = await repository.GetByRepoAsync(
    "https://github.com/org/repo",
    skip: 0,
    take: 100);

// Get next 100 documents
var page2 = await repository.GetByRepoAsync(
    "https://github.com/org/repo",
    skip: 100,
    take: 100);

foreach (var doc in page1)
{
    Console.WriteLine($"{doc.CreatedAt}: {doc.FilePath}");
}
```

**Pagination Best Practices**:
- Use consistent `take` size for predictable memory usage
- Track total count separately via `IVectorStore.CountAsync()` if needed
- Consider cursor-based pagination for very large repositories (future enhancement)

---

### AddAsync

**Behavior**:
- Inserts new document with client-generated `Id` (Guid)
- Sets `CreatedAt` to current UTC time if not provided
- Sets `UpdatedAt` to current UTC time
- Throws `DbUpdateException` if document with same `Id` already exists

**Validation**:
- Validates embedding dimensions (1536 when non-null) before save
- Validates required properties (RepoUrl, FilePath, Text)

**Example Usage**:
```csharp
var document = new DocumentEntity
{
    Id = Guid.NewGuid(),
    RepoUrl = "https://github.com/org/repo",
    FilePath = "docs/README.md",
    Title = "README",
    Text = "# Project Documentation\n\n...",
    IsCode = false,
    TokenCount = 250
};

await repository.AddAsync(document);
```

---

### UpdateAsync

**Behavior**:
- Updates all properties of existing document
- Automatically updates `UpdatedAt` to current UTC time
- Uses optimistic concurrency via `UpdatedAt` timestamp
- Throws `InvalidOperationException` if document does not exist
- Throws `DbUpdateConcurrencyException` if concurrent modification detected

**Concurrency Handling**:
```csharp
// Load current state
var document = await repository.GetByIdAsync(documentId);
if (document == null) return;

// Modify properties
document.Text = updatedText;
document.Embedding = newEmbedding;

try
{
    await repository.UpdateAsync(document);
}
catch (DbUpdateConcurrencyException)
{
    // Reload and retry or notify user of conflict
    Console.WriteLine("Document was modified by another process");
}
```

---

### DeleteAsync

**Behavior**:
- Permanently deletes document (no soft delete)
- Throws `InvalidOperationException` if document does not exist
- Cascades to related entities (none in v1)

**Performance Requirements**:
- <100ms for single document deletion

**Example Usage**:
```csharp
if (await repository.ExistsAsync(documentId))
{
    await repository.DeleteAsync(documentId);
    Console.WriteLine("Document deleted");
}
```

---

### ExistsAsync

**Behavior**:
- Returns true if document with given `Id` exists
- Returns false otherwise
- Lightweight query (no document data loaded)

**Performance Requirements**:
- <50ms for local database (primary key lookup with EXISTS)

**Example Usage**:
```csharp
if (await repository.ExistsAsync(documentId))
{
    // Proceed with update
}
else
{
    // Create new document
}
```

---

## Relationship with IVectorStore

**Separation of Concerns**:
- `IDocumentRepository`: Standard CRUD operations
- `IVectorStore`: Vector-specific operations (similarity search, upsert with embedding validation)

**When to Use Each**:

| Operation | Interface | Reason |
|-----------|-----------|--------|
| Get document by ID | `IDocumentRepository` | No vector operations needed |
| List repository documents | `IDocumentRepository` | Standard pagination query |
| Add document without embedding | `IDocumentRepository` | Embedding not required |
| Add/update document with embedding | `IVectorStore` | Ensures embedding validation and index updates |
| Semantic search | `IVectorStore` | Requires vector similarity operations |
| Delete by ID | Either | Same behavior in both |

**Example Workflow** (document ingestion):
```csharp
// Phase 1: Create document without embedding
var document = new DocumentEntity { /* properties */ };
await repository.AddAsync(document);

// Phase 2: Generate embedding asynchronously
float[] embedding = await embeddingService.GenerateAsync(document.Text);
document.Embedding = embedding;

// Phase 3: Update with embedding (use IVectorStore for validation)
await vectorStore.UpsertAsync(document);
```

---

## Error Handling

| Exception | Thrown When | Recovery Strategy |
|-----------|-------------|-------------------|
| `ArgumentNullException` | Null document parameter | Fix caller code |
| `ArgumentException` | Invalid pagination (take > 1000) or empty repoUrl | Validate inputs |
| `InvalidOperationException` | Update/delete non-existent document | Check ExistsAsync first |
| `DbUpdateException` | Unique constraint violation or FK issues | Log and retry with new ID |
| `DbUpdateConcurrencyException` | Concurrent modification | Reload and retry |

---

## Testing Requirements

**Unit Tests**:
- ✅ GetByIdAsync returns document when exists
- ✅ GetByIdAsync returns null when not found
- ✅ GetByRepoAsync returns documents ordered by CreatedAt desc
- ✅ GetByRepoAsync respects skip/take pagination
- ✅ GetByRepoAsync throws ArgumentException when take > 1000
- ✅ AddAsync inserts new document
- ✅ AddAsync throws DbUpdateException for duplicate ID
- ✅ UpdateAsync modifies existing document
- ✅ UpdateAsync throws InvalidOperationException for non-existent document
- ✅ UpdateAsync throws DbUpdateConcurrencyException on concurrent modification
- ✅ DeleteAsync removes document
- ✅ ExistsAsync returns true/false correctly

**Integration Tests** (with Testcontainers):
- ✅ AddAsync persists document and can be retrieved via GetByIdAsync
- ✅ UpdateAsync changes are persisted
- ✅ GetByRepoAsync pagination works correctly across multiple pages
- ✅ DeleteAsync removes document from database
- ✅ Optimistic concurrency detects concurrent updates
- ✅ Repository index improves GetByRepoAsync performance

---

## Implementation Checklist

- [ ] Create `IDocumentRepository.cs` in `DeepWiki.Data/Interfaces/`
- [ ] Implement `SqlServerDocumentRepository` using EF Core
- [ ] Implement `PostgresDocumentRepository` using EF Core
- [ ] Add XML documentation comments to all public methods
- [ ] Write unit tests for all methods
- [ ] Write integration tests with Testcontainers
- [ ] Verify 100% test parity between SQL Server and PostgreSQL implementations
- [ ] Document usage patterns in quickstart.md

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0.0 | 2026-01-16 | Initial specification |
