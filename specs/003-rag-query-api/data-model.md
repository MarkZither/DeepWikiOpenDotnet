# Data Model: RAG Query API

**Feature**: 003-rag-query-api  
**Date**: 2026-01-26  
**Status**: Complete

---

## Entities

### Core Entities (Existing)

These entities already exist in `DeepWiki.Data.Abstractions.Models`:

#### DocumentDto

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| Id | Guid | Yes | Unique document identifier |
| RepoUrl | string | Yes | Source repository URL |
| FilePath | string | Yes | File path within repository |
| Title | string | Yes | Document title |
| Text | string | Yes | Full document text content |
| Embedding | float[] | No | Vector embedding (1536 dimensions) |
| MetadataJson | string | No | JSON-encoded metadata |
| CreatedAt | DateTime | Yes | Creation timestamp (UTC) |
| UpdatedAt | DateTime | Yes | Last update timestamp (UTC) |
| TokenCount | int | No | Token count for the document |
| FileType | string | No | File extension/type |
| IsCode | bool | No | Whether document contains code |
| IsImplementation | bool | No | Whether document is implementation (vs interface) |

#### VectorQueryResult

| Field | Type | Description |
|-------|------|-------------|
| Document | DocumentDto | The matched document |
| SimilarityScore | float | Cosine similarity score (0.0 to 1.0) |

---

### API Request/Response Models (New)

These models will be created in `deepwiki-open-dotnet.ApiService/Models/`:

#### QueryRequest

```csharp
/// <summary>
/// Request model for semantic search queries.
/// </summary>
public sealed record QueryRequest
{
    /// <summary>
    /// Natural language search query text.
    /// </summary>
    /// <example>How do I implement authentication in ASP.NET Core?</example>
    [Required]
    [StringLength(10000, MinimumLength = 1)]
    public required string Query { get; init; }

    /// <summary>
    /// Maximum number of results to return (default: 10, max: 100).
    /// </summary>
    [Range(1, 100)]
    public int K { get; init; } = 10;

    /// <summary>
    /// Optional filters to narrow search results.
    /// </summary>
    public QueryFilters? Filters { get; init; }

    /// <summary>
    /// Whether to include full document text in results (default: true).
    /// </summary>
    public bool IncludeFullText { get; init; } = true;
}

/// <summary>
/// Filters for narrowing semantic search results.
/// </summary>
public sealed record QueryFilters
{
    /// <summary>
    /// Filter by repository URL (exact match or SQL LIKE pattern).
    /// </summary>
    public string? RepoUrl { get; init; }

    /// <summary>
    /// Filter by file path (SQL LIKE pattern supported).
    /// </summary>
    public string? FilePath { get; init; }
}
```

#### QueryResponse

```csharp
/// <summary>
/// Response model for semantic search queries.
/// Returns array of results directly (Python API parity).
/// </summary>
public sealed record QueryResultItem
{
    /// <summary>
    /// Unique document identifier.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Source repository URL.
    /// </summary>
    public required string RepoUrl { get; init; }

    /// <summary>
    /// File path within repository.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Document title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Full document text (included when includeFullText=true).
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// Cosine similarity score (0.0 to 1.0, higher is more similar).
    /// </summary>
    public float SimilarityScore { get; init; }

    /// <summary>
    /// Document metadata as JSON object.
    /// </summary>
    public JsonElement? Metadata { get; init; }
}

// Response is: QueryResultItem[] (raw array, not wrapped)
```

#### IngestRequest

```csharp
/// <summary>
/// Request model for batch document ingestion.
/// </summary>
public sealed record IngestRequest
{
    /// <summary>
    /// Documents to ingest into the vector store.
    /// </summary>
    [Required]
    [MinLength(1)]
    [MaxLength(1000)]
    public required IReadOnlyList<IngestDocument> Documents { get; init; }

    /// <summary>
    /// Continue processing if individual documents fail (default: true).
    /// </summary>
    public bool ContinueOnError { get; init; } = true;

    /// <summary>
    /// Batch size for parallel processing (default: 10).
    /// </summary>
    [Range(1, 50)]
    public int BatchSize { get; init; } = 10;
}

/// <summary>
/// Single document for ingestion.
/// </summary>
public sealed record IngestDocument
{
    /// <summary>
    /// Source repository URL.
    /// </summary>
    [Required]
    public required string RepoUrl { get; init; }

    /// <summary>
    /// File path within repository.
    /// </summary>
    [Required]
    public required string FilePath { get; init; }

    /// <summary>
    /// Document title.
    /// </summary>
    [Required]
    public required string Title { get; init; }

    /// <summary>
    /// Full document text content.
    /// </summary>
    [Required]
    [StringLength(5_000_000)] // 5MB text limit per constitution
    public required string Text { get; init; }

    /// <summary>
    /// Optional metadata as JSON object.
    /// </summary>
    public JsonElement? Metadata { get; init; }
}
```

#### IngestResponse

```csharp
/// <summary>
/// Response model for batch document ingestion.
/// </summary>
public sealed record IngestResponse
{
    /// <summary>
    /// Number of documents successfully ingested.
    /// </summary>
    public int SuccessCount { get; init; }

    /// <summary>
    /// Number of documents that failed to ingest.
    /// </summary>
    public int FailureCount { get; init; }

    /// <summary>
    /// Total chunks created from ingested documents.
    /// </summary>
    public int TotalChunks { get; init; }

    /// <summary>
    /// Processing duration in milliseconds.
    /// </summary>
    public long DurationMs { get; init; }

    /// <summary>
    /// IDs of successfully ingested documents.
    /// </summary>
    public IReadOnlyList<Guid> IngestedDocumentIds { get; init; } = [];

    /// <summary>
    /// Details of any ingestion errors.
    /// </summary>
    public IReadOnlyList<IngestError> Errors { get; init; } = [];
}

/// <summary>
/// Error details for a failed document ingestion.
/// </summary>
public sealed record IngestError
{
    /// <summary>
    /// Identifier for the failed document (repoUrl:filePath).
    /// </summary>
    public required string DocumentIdentifier { get; init; }

    /// <summary>
    /// Error message describing the failure.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Processing stage where failure occurred.
    /// </summary>
    public required string Stage { get; init; }
}
```

#### DocumentListResponse (Pagination)

```csharp
/// <summary>
/// Paginated list of documents.
/// </summary>
public sealed record DocumentListResponse
{
    /// <summary>
    /// Documents in current page.
    /// </summary>
    public required IReadOnlyList<DocumentSummary> Items { get; init; }

    /// <summary>
    /// Total number of documents matching filters.
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// Current page number (1-based).
    /// </summary>
    public int Page { get; init; }

    /// <summary>
    /// Items per page.
    /// </summary>
    public int PageSize { get; init; }

    /// <summary>
    /// Total number of pages.
    /// </summary>
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}

/// <summary>
/// Document summary for list views (excludes full text and embedding).
/// </summary>
public sealed record DocumentSummary
{
    public Guid Id { get; init; }
    public required string RepoUrl { get; init; }
    public required string FilePath { get; init; }
    public required string Title { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public int TokenCount { get; init; }
    public string? FileType { get; init; }
    public bool IsCode { get; init; }
}
```

#### Error Response

```csharp
/// <summary>
/// Standard error response (Python API parity).
/// </summary>
public sealed record ErrorResponse
{
    /// <summary>
    /// Error message.
    /// </summary>
    public required string Detail { get; init; }
}
```

---

## Configuration Models

#### VectorStoreOptions

```csharp
/// <summary>
/// Configuration options for vector store provider selection.
/// </summary>
public sealed class VectorStoreOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "VectorStore";

    /// <summary>
    /// Vector store provider: "sqlserver" or "postgres".
    /// </summary>
    public string Provider { get; set; } = "sqlserver";

    /// <summary>
    /// SQL Server-specific configuration.
    /// </summary>
    public SqlServerVectorStoreOptions SqlServer { get; set; } = new();

    /// <summary>
    /// PostgreSQL-specific configuration.
    /// </summary>
    public PostgresVectorStoreOptions Postgres { get; set; } = new();
}

/// <summary>
/// SQL Server vector store configuration.
/// </summary>
public sealed class SqlServerVectorStoreOptions
{
    /// <summary>
    /// Connection string (can also use ConnectionStrings:SqlServer).
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// HNSW index M parameter (default: 16).
    /// </summary>
    public int HnswM { get; set; } = 16;

    /// <summary>
    /// HNSW index ef_construction parameter (default: 200).
    /// </summary>
    public int HnswEfConstruction { get; set; } = 200;
}

/// <summary>
/// PostgreSQL vector store configuration.
/// </summary>
public sealed class PostgresVectorStoreOptions
{
    /// <summary>
    /// Connection string (can also use ConnectionStrings:Postgres).
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// HNSW index M parameter (default: 16).
    /// </summary>
    public int HnswM { get; set; } = 16;

    /// <summary>
    /// HNSW index ef_construction parameter (default: 200).
    /// </summary>
    public int HnswEfConstruction { get; set; } = 200;
}
```

---

## Validation Rules

| Entity | Field | Rule |
|--------|-------|------|
| QueryRequest | Query | Required, 1-10,000 characters |
| QueryRequest | K | Range 1-100, default 10 |
| IngestRequest | Documents | Required, 1-1000 items |
| IngestDocument | Text | Max 5MB (constitution limit) |
| IngestDocument | RepoUrl | Required, valid URL format |
| IngestDocument | FilePath | Required, valid path format |
| Pagination | Page | Min 1, default 1 |
| Pagination | PageSize | Range 1-100, default 20 |

---

## State Transitions

### Document Lifecycle

```
[New] → POST /api/documents/ingest → [Ingested]
[Ingested] → PUT /api/documents/{id} → [Updated]  (future)
[Ingested] → DELETE /api/documents/{id} → [Deleted]
```

### Query Flow

```
QueryRequest → Embedding Service → float[1536] → Vector Store → VectorQueryResult[] → QueryResultItem[]
```

---

## Relationships

```
Repository (1) ──────────── (N) Document
Document (1) ──────────── (1) Embedding
Query (1) ──────────── (N) QueryResultItem
IngestRequest (1) ──────────── (N) IngestDocument
```
