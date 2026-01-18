# Phase 1: Data Model — Multi-Database Data Access Layer

**Date**: 2026-01-16  
**Phase**: Design & Contracts  
**Purpose**: Define entity model, relationships, validation rules, and state transitions

---

## Entity Model

### DocumentEntity

**Purpose**: Represents a document in the knowledge base with its text content, vector embedding, and metadata for semantic search and retrieval.

**Location**: `src/DeepWiki.Data/Entities/DocumentEntity.cs`

**Properties**:

| Property | Type | Constraints | Description |
|----------|------|-------------|-------------|
| `Id` | `Guid` | Required, Primary Key | Unique identifier (auto-generated) |
| `RepoUrl` | `string` | Required, Max 2000 chars, Indexed | Source repository URL (e.g., `https://github.com/org/repo`) |
| `FilePath` | `string` | Required, Max 1000 chars | Relative file path within repository (e.g., `src/main.cs`) |
| `Title` | `string?` | Optional, Max 500 chars | Document title or filename without extension |
| `Text` | `string` | Required, Max 50MB | Full document text content |
| `Embedding` | `float[]?` | Optional, Exactly 1536 dims when non-null | Vector embedding representing semantic meaning |
| `FileType` | `string?` | Optional, Max 10 chars | File extension without dot (e.g., `cs`, `md`, `py`) |
| `IsCode` | `bool` | Required, Default false | True if document is source code |
| `IsImplementation` | `bool` | Required, Default false | True if document contains implementation code (vs comments/docs) |
| `TokenCount` | `int` | Required, Min 0 | Number of tokens in text (for chunking and cost estimation) |
| `CreatedAt` | `DateTime` | Required, UTC | Timestamp when document was first stored |
| `UpdatedAt` | `DateTime` | Required, UTC | Timestamp of last update (for optimistic concurrency) |
| `MetadataJson` | `string?` | Optional, Max 1MB | Additional metadata as JSON (extensible key-value pairs) |

**Validation Rules**:

```csharp
public class DocumentEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    [Required]
    [MaxLength(2000)]
    public required string RepoUrl { get; set; }
    
    [Required]
    [MaxLength(1000)]
    public required string FilePath { get; set; }
    
    [MaxLength(500)]
    public string? Title { get; set; }
    
    [Required]
    public required string Text { get; set; } // Note: 50MB limit enforced at DB level
    
    public float[]? Embedding { get; set; } // Validated: 1536 dims when non-null
    
    [MaxLength(10)]
    public string? FileType { get; set; }
    
    public bool IsCode { get; set; }
    
    public bool IsImplementation { get; set; }
    
    [Range(0, int.MaxValue)]
    public int TokenCount { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    [MaxLength(1048576)] // 1MB
    public string? MetadataJson { get; set; }
    
    // Validation method
    public void ValidateEmbedding()
    {
        if (Embedding != null && Embedding.Length != 1536)
        {
            throw new ArgumentException(
                $"Embedding must be exactly 1536 dimensions (got {Embedding.Length})",
                nameof(Embedding));
        }
    }
}
```

**Indexes**:
- Primary: `Id` (clustered index)
- Non-clustered: `RepoUrl` (for filtering queries)
- Non-clustered: `CreatedAt` (for time-based queries)
- Vector: `Embedding` (HNSW/IVFFlat for ANN search)

**Relationships**:
- None in v1 (standalone entity)
- Future: May relate to `RepositoryEntity` (one-to-many) for repository metadata

---

## Database-Specific Mappings

### SQL Server Configuration

**Location**: `src/DeepWiki.Data.SqlServer/Configuration/DocumentEntityConfiguration.cs`

```csharp
public class DocumentEntityConfiguration : IEntityTypeConfiguration<DocumentEntity>
{
    public void Configure(EntityTypeBuilder<DocumentEntity> builder)
    {
        builder.ToTable("Documents");
        
        // Primary key
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever(); // Client-generated Guid
        
        // String properties with SQL Server-specific constraints
        builder.Property(d => d.RepoUrl)
            .IsRequired()
            .HasMaxLength(2000)
            .HasColumnType("nvarchar(2000)");
        
        builder.Property(d => d.FilePath)
            .IsRequired()
            .HasMaxLength(1000)
            .HasColumnType("nvarchar(1000)");
        
        builder.Property(d => d.Title)
            .HasMaxLength(500)
            .HasColumnType("nvarchar(500)");
        
        builder.Property(d => d.Text)
            .IsRequired()
            .HasColumnType("nvarchar(max)"); // Up to 2GB (50MB limit enforced at app level)
        
        // Vector column: SQL Server 2025 native vector type
        builder.Property(d => d.Embedding)
            .HasColumnType("vector(1536)")
            .HasColumnName("Embedding");
        
        builder.Property(d => d.FileType)
            .HasMaxLength(10)
            .HasColumnType("nvarchar(10)");
        
        builder.Property(d => d.MetadataJson)
            .HasColumnType("nvarchar(max)");
        
        // Timestamps with UTC
        builder.Property(d => d.CreatedAt)
            .IsRequired()
            .HasColumnType("datetime2")
            .HasDefaultValueSql("SYSUTCDATETIME()");
        
        builder.Property(d => d.UpdatedAt)
            .IsRequired()
            .HasColumnType("datetime2")
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .IsConcurrencyToken(); // Optimistic concurrency
        
        // Indexes
        builder.HasIndex(d => d.RepoUrl).HasDatabaseName("IX_Documents_RepoUrl");
        builder.HasIndex(d => d.CreatedAt).HasDatabaseName("IX_Documents_CreatedAt");
        
        // Vector index created via raw SQL in migration (see migration notes)
    }
}
```

**Migration SQL (add to migration Up() method)**:
```sql
CREATE INDEX IX_Documents_Embedding
ON Documents(Embedding)
USING VECTOR
WITH (METHOD = 'HNSW', METRIC = 'COSINE');
```

---

### PostgreSQL Configuration

**Location**: `src/DeepWiki.Data.Postgres/Configuration/DocumentEntityConfiguration.cs`

```csharp
public class DocumentEntityConfiguration : IEntityTypeConfiguration<DocumentEntity>
{
    public void Configure(EntityTypeBuilder<DocumentEntity> builder)
    {
        builder.ToTable("documents"); // Lowercase per Postgres convention
        
        // Primary key
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        
        // String properties with Postgres-specific constraints
        builder.Property(d => d.RepoUrl)
            .IsRequired()
            .HasMaxLength(2000)
            .HasColumnName("repo_url")
            .HasColumnType("varchar(2000)");
        
        builder.Property(d => d.FilePath)
            .IsRequired()
            .HasMaxLength(1000)
            .HasColumnName("file_path")
            .HasColumnType("varchar(1000)");
        
        builder.Property(d => d.Title)
            .HasMaxLength(500)
            .HasColumnName("title")
            .HasColumnType("varchar(500)");
        
        builder.Property(d => d.Text)
            .IsRequired()
            .HasColumnName("text")
            .HasColumnType("text");
        
        // Vector column: pgvector extension type
        builder.Property(d => d.Embedding)
            .HasColumnType("vector(1536)")
            .HasColumnName("embedding");
        
        builder.Property(d => d.FileType)
            .HasMaxLength(10)
            .HasColumnName("file_type")
            .HasColumnType("varchar(10)");
        
        builder.Property(d => d.IsCode)
            .HasColumnName("is_code");
        
        builder.Property(d => d.IsImplementation)
            .HasColumnName("is_implementation");
        
        builder.Property(d => d.TokenCount)
            .HasColumnName("token_count");
        
        builder.Property(d => d.MetadataJson)
            .HasColumnName("metadata_json")
            .HasColumnType("jsonb"); // Native JSON type for Postgres
        
        // Timestamps with UTC
        builder.Property(d => d.CreatedAt)
            .IsRequired()
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");
        
        builder.Property(d => d.UpdatedAt)
            .IsRequired()
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsConcurrencyToken();
        
        // Indexes
        builder.HasIndex(d => d.RepoUrl)
            .HasDatabaseName("ix_documents_repo_url");
        builder.HasIndex(d => d.CreatedAt)
            .HasDatabaseName("ix_documents_created_at");
    }
}
```

**Migration SQL (add to migration Up() method)**:
```sql
-- Enable pgvector extension
CREATE EXTENSION IF NOT EXISTS vector;

-- Create vector index (HNSW preferred, IVFFlat fallback)
CREATE INDEX ix_documents_embedding ON documents
USING hnsw (embedding vector_cosine_ops);

-- Alternative: IVFFlat (for pgvector < 0.5.0)
-- CREATE INDEX ix_documents_embedding ON documents
-- USING ivfflat (embedding vector_cosine_ops)
-- WITH (lists = 100);
```

---

## State Transitions

### Document Lifecycle

```
┌─────────────┐
│  Non-existent│
└──────┬──────┘
       │ AddAsync()
       ▼
┌─────────────┐
│   Created   │ (Embedding may be null)
│  (Stored)   │
└──────┬──────┘
       │ UpdateAsync() [with Embedding]
       ▼
┌─────────────┐
│  Embedded   │ (Ready for vector search)
│  (Indexed)  │
└──────┬──────┘
       │ UpdateAsync() [text changes]
       ▼
┌─────────────┐
│   Updated   │ (Embedding stale, needs re-embedding)
│ (Re-index)  │
└──────┬──────┘
       │ DeleteAsync()
       ▼
┌─────────────┐
│   Deleted   │ (Permanent removal, no soft delete)
└─────────────┘
```

**State Invariants**:
- `CreatedAt` never changes after initial creation
- `UpdatedAt` updated on every modification
- `Embedding` null allowed but excludes document from vector queries
- Optimistic concurrency: Updates check `UpdatedAt` to detect conflicts

**Concurrency Handling**:
```csharp
// Update operation
public async Task UpdateAsync(DocumentEntity document, CancellationToken cancellationToken)
{
    document.UpdatedAt = DateTime.UtcNow;
    
    try
    {
        await _context.SaveChangesAsync(cancellationToken);
    }
    catch (DbUpdateConcurrencyException)
    {
        // Another process modified the document; reload and retry or fail
        throw new InvalidOperationException(
            $"Document {document.Id} was modified by another process");
    }
}
```

---

## Metadata JSON Schema (Example)

While `MetadataJson` is extensible, here's a recommended schema for common use cases:

```json
{
  "language": "csharp",
  "linesOfCode": 245,
  "lastCommitHash": "abc123def456",
  "lastCommitAuthor": "developer@example.com",
  "lastCommitDate": "2026-01-15T10:30:00Z",
  "tags": ["api", "controller", "authentication"],
  "customFields": {
    "complexity": "high",
    "reviewStatus": "approved"
  }
}
```

**Serialization**:
```csharp
using System.Text.Json;

// Serialize
document.MetadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = false
});

// Deserialize
var metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(
    document.MetadataJson,
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
```

---

## Validation Summary

**Pre-Save Validation** (in repository methods):
1. `ValidateEmbedding()` - Check 1536 dimensions
2. Check required properties (RepoUrl, FilePath, Text)
3. Validate string length constraints
4. Validate TokenCount >= 0

**Database Constraints**:
1. Primary key uniqueness
2. Not null constraints on required fields
3. String length limits (nvarchar/varchar max lengths)
4. Check constraint on TokenCount >= 0
5. Optimistic concurrency on UpdatedAt

**Error Messages**:
- `ArgumentException`: "Embedding must be exactly 1536 dimensions (got {actual})"
- `DbUpdateException`: "Failed to save document: {inner exception details}"
- `DbUpdateConcurrencyException`: "Document was modified by another process"
- `InvalidOperationException`: "Cannot query vectors for documents without embeddings"

---

## Next Steps

1. Implement `DocumentEntity` in `DeepWiki.Data` project
2. Implement provider-specific configurations
3. Create initial EF migrations for both databases
4. Write unit tests for entity validation
5. Write integration tests for database round-trip with Testcontainers

See `contracts/` directory for interface specifications and `quickstart.md` for setup instructions.
