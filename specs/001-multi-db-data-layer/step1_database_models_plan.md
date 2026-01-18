# DeepWiki .NET Migration — Step 1: Database & Models Foundation

## Executive Summary
Build the foundational data layer with EF Core that compiles, has test coverage, and supports multiple vector databases (SQL Server 2025 vector type + Postgres pgvector). This establishes a solid, testable base before tackling agent orchestration and API endpoints.

---

## Python Data Model Analysis

From the Python codebase, the core data model is:

**Document entity** (adalflow `Document` type):
- `text`: string (document content)
- `meta_data`: dict with:
  - `file_path`: string (relative path in repo)
  - `type`: string (file extension without dot)
  - `is_code`: bool
  - `is_implementation`: bool
  - `title`: string
  - `token_count`: int
- `id`: string (auto-generated UUID)
- `embedding`: float[] (1536-dim vector from embedder)
- `repo_url`: string (added during ingestion)

**Storage**: Python uses `adalflow.core.db.LocalDB` (pickle-based FAISS index) for vector search and retrieval.

**Migration target**: Replace LocalDB with EF Core + SQL Server/Postgres vector columns for production-grade persistence and querying.

---

## Step 1 Architecture: Multi-Database EF Core Design

### Project structure

```
src/
  DeepWiki.Data/                      # Base abstractions & shared models
    Entities/
      DocumentEntity.cs               # Shared entity (no DB-specific annotations)
    Interfaces/
      IVectorStore.cs                 # Vector store abstraction
      IDocumentRepository.cs          # CRUD operations
    DeepWiki.Data.csproj
  
  DeepWiki.Data.SqlServer/            # SQL Server 2025 implementation
    Configuration/
      DocumentEntityConfiguration.cs  # EF configuration with vector(1536)
    DbContexts/
      SqlServerVectorDbContext.cs     # DbContext for SQL Server
    Repositories/
      SqlServerVectorStore.cs         # IVectorStore implementation
    DeepWiki.Data.SqlServer.csproj
  
  DeepWiki.Data.Postgres/             # Postgres pgvector implementation
    Configuration/
      DocumentEntityConfiguration.cs  # EF configuration with vector type
    DbContexts/
      PostgresVectorDbContext.cs      # DbContext for Postgres
    Repositories/
      PostgresVectorStore.cs          # IVectorStore implementation
    DeepWiki.Data.Postgres.csproj

tests/
  DeepWiki.Data.Tests/                # Shared tests (use in-memory or mocks)
  DeepWiki.Data.SqlServer.Tests/      # SQL Server-specific integration tests
  DeepWiki.Data.Postgres.Tests/       # Postgres-specific integration tests
```

### Rationale
- **Base project** (`DeepWiki.Data`): DB-agnostic entity models, interfaces, shared logic.
- **DB-specific projects**: Handle vector column type mappings, ANN query syntax, and provider-specific optimizations.
- **Testability**: Shared unit tests + provider-specific integration tests.

---

## Detailed Design

### 1. `DeepWiki.Data` — Shared Abstractions

#### `Entities/DocumentEntity.cs`
```csharp
namespace DeepWiki.Data.Entities;

public class DocumentEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string RepoUrl { get; set; }
    public required string FilePath { get; set; }
    public required string Title { get; set; }
    public required string Text { get; set; }
    public string FileType { get; set; } = string.Empty; // e.g., "py", "md"
    public bool IsCode { get; set; }
    public bool IsImplementation { get; set; }
    public int TokenCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Metadata stored as JSON for extensibility
    public string? MetadataJson { get; set; }

    // Embedding: stored as byte[] or float[] depending on DB provider
    // Base entity uses float[] for in-memory representation
    public float[]? Embedding { get; set; }
}
```

#### `Interfaces/IVectorStore.cs`
```csharp
namespace DeepWiki.Data.Interfaces;

public interface IVectorStore
{
    Task UpsertAsync(DocumentEntity document, CancellationToken cancellationToken = default);
    Task<IEnumerable<DocumentEntity>> QueryNearestAsync(
        float[] queryEmbedding,
        int k = 10,
        string? repoUrlFilter = null,
        CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task DeleteByRepoAsync(string repoUrl, CancellationToken cancellationToken = default);
    Task<int> CountAsync(string? repoUrlFilter = null, CancellationToken cancellationToken = default);
}
```

#### `Interfaces/IDocumentRepository.cs`
```csharp
namespace DeepWiki.Data.Interfaces;

public interface IDocumentRepository
{
    Task<DocumentEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<DocumentEntity>> GetByRepoAsync(string repoUrl, CancellationToken cancellationToken = default);
    Task AddAsync(DocumentEntity document, CancellationToken cancellationToken = default);
    Task UpdateAsync(DocumentEntity document, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
```

---

### 2. `DeepWiki.Data.SqlServer` — SQL Server 2025 Vector Support

#### `Configuration/DocumentEntityConfiguration.cs`
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DeepWiki.Data.Entities;

namespace DeepWiki.Data.SqlServer.Configuration;

public class DocumentEntityConfiguration : IEntityTypeConfiguration<DocumentEntity>
{
    public void Configure(EntityTypeBuilder<DocumentEntity> builder)
    {
        builder.ToTable("Documents");
        builder.HasKey(d => d.Id);

        // Vector column: SQL Server 2025 syntax
        builder.Property(d => d.Embedding)
            .HasColumnType("vector(1536)")
            .HasColumnName("Embedding");

        // Indexes
        builder.HasIndex(d => d.RepoUrl);
        builder.HasIndex(d => d.CreatedAt);

        // TODO: Add vector index creation via migration SQL or external script
        // Example: CREATE INDEX idx_embedding ON Documents(Embedding) WITH (METHOD = 'HNSW');
    }
}
```

#### `DbContexts/SqlServerVectorDbContext.cs`
```csharp
using Microsoft.EntityFrameworkCore;
using DeepWiki.Data.Entities;
using DeepWiki.Data.SqlServer.Configuration;

namespace DeepWiki.Data.SqlServer.DbContexts;

public class SqlServerVectorDbContext : DbContext
{
    public SqlServerVectorDbContext(DbContextOptions<SqlServerVectorDbContext> options)
        : base(options) { }

    public DbSet<DocumentEntity> Documents => Set<DocumentEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new DocumentEntityConfiguration());
    }
}
```

#### `Repositories/SqlServerVectorStore.cs`
```csharp
using Microsoft.EntityFrameworkCore;
using DeepWiki.Data.Entities;
using DeepWiki.Data.Interfaces;
using DeepWiki.Data.SqlServer.DbContexts;

namespace DeepWiki.Data.SqlServer.Repositories;

public class SqlServerVectorStore : IVectorStore
{
    private readonly SqlServerVectorDbContext _context;

    public SqlServerVectorStore(SqlServerVectorDbContext context)
    {
        _context = context;
    }

    public async Task UpsertAsync(DocumentEntity document, CancellationToken cancellationToken = default)
    {
        var existing = await _context.Documents.FindAsync(new object[] { document.Id }, cancellationToken);
        if (existing != null)
        {
            _context.Entry(existing).CurrentValues.SetValues(document);
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            await _context.Documents.AddAsync(document, cancellationToken);
        }
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IEnumerable<DocumentEntity>> QueryNearestAsync(
        float[] queryEmbedding,
        int k = 10,
        string? repoUrlFilter = null,
        CancellationToken cancellationToken = default)
    {
        // SQL Server 2025 vector distance function (pseudo-SQL; adjust to actual syntax)
        // Example: ORDER BY VECTOR_DISTANCE(Embedding, @queryVector) ASC
        var sql = @"
            SELECT TOP(@k) * 
            FROM Documents 
            WHERE (@repoUrl IS NULL OR RepoUrl = @repoUrl)
            ORDER BY VECTOR_DISTANCE(Embedding, @queryVector) ASC";

        // TODO: Replace with actual SQL Server 2025 vector distance syntax
        // and parameterize @queryVector as byte[] or appropriate type

        // Placeholder: use EF Core FromSqlInterpolated with proper vector serialization
        throw new NotImplementedException("SQL Server vector query syntax pending finalization");
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var doc = await _context.Documents.FindAsync(new object[] { id }, cancellationToken);
        if (doc != null)
        {
            _context.Documents.Remove(doc);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task DeleteByRepoAsync(string repoUrl, CancellationToken cancellationToken = default)
    {
        await _context.Documents.Where(d => d.RepoUrl == repoUrl)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<int> CountAsync(string? repoUrlFilter = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Documents.AsQueryable();
        if (!string.IsNullOrEmpty(repoUrlFilter))
            query = query.Where(d => d.RepoUrl == repoUrlFilter);
        return await query.CountAsync(cancellationToken);
    }
}
```

---

### 3. `DeepWiki.Data.Postgres` — Postgres pgvector Support

Similar structure to SQL Server, but with pgvector-specific configuration:

#### `Configuration/DocumentEntityConfiguration.cs`
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DeepWiki.Data.Entities;

namespace DeepWiki.Data.Postgres.Configuration;

public class DocumentEntityConfiguration : IEntityTypeConfiguration<DocumentEntity>
{
    public void Configure(EntityTypeBuilder<DocumentEntity> builder)
    {
        builder.ToTable("documents");
        builder.HasKey(d => d.Id);

        // pgvector: use vector(1536) column type
        builder.Property(d => d.Embedding)
            .HasColumnType("vector(1536)")
            .HasColumnName("embedding");

        builder.HasIndex(d => d.RepoUrl);
        builder.HasIndex(d => d.CreatedAt);

        // TODO: Add IVFFlat or HNSW index via migration
        // Example: CREATE INDEX ON documents USING ivfflat (embedding vector_cosine_ops);
    }
}
```

#### Postgres `QueryNearestAsync` example:
```csharp
public async Task<IEnumerable<DocumentEntity>> QueryNearestAsync(...)
{
    // pgvector uses <-> for L2 distance, <#> for inner product, <=> for cosine
    var sql = @"
        SELECT * FROM documents 
        WHERE (@repoUrl IS NULL OR repo_url = @repoUrl)
        ORDER BY embedding <=> @queryVector 
        LIMIT @k";
    
    // Use Npgsql.EntityFrameworkCore.PostgreSQL vector extension
    // and parameterize @queryVector appropriately
    throw new NotImplementedException("Postgres pgvector query pending implementation");
}
```

---

## Testing Strategy

### Unit tests (`DeepWiki.Data.Tests`)
- Test entity validation and metadata serialization
- Mock `IVectorStore` and `IDocumentRepository` for service layer tests

### Integration tests (`DeepWiki.Data.SqlServer.Tests` & `.Postgres.Tests`)
- Use Testcontainers or Docker Compose to spin up real SQL Server 2025 / Postgres instances
- Test:
  - EF migrations apply successfully
  - CRUD operations
  - Vector upsert and retrieval
  - Nearest-neighbor query accuracy (use known embeddings and verify top-k results)
  - Performance benchmarks (index creation, query latency)

---

## Implementation Checklist

### Phase 1.1: Base Project Setup (2–3 days)
- [ ] Create `DeepWiki.Data` class library (.NET 10)
- [ ] Define `DocumentEntity` with all properties
- [ ] Define `IVectorStore` and `IDocumentRepository` interfaces
- [ ] Add `System.Text.Json` for metadata serialization
- [ ] Write unit tests for entity model and metadata JSON round-trip

### Phase 1.2: SQL Server Implementation (3–5 days)
- [ ] Create `DeepWiki.Data.SqlServer` project
- [ ] Install EF Core SQL Server provider
- [ ] Implement `DocumentEntityConfiguration` with `vector(1536)` column type
- [ ] Implement `SqlServerVectorDbContext`
- [ ] Implement `SqlServerVectorStore` (CRUD operations first, vector query as TODO)
- [ ] Create initial EF migration
- [ ] Add Docker Compose file for SQL Server 2025 (or use Testcontainers)
- [ ] Write integration tests: migration, CRUD, index creation
- [ ] Research/implement SQL Server 2025 vector distance function syntax
- [ ] Test nearest-neighbor queries with sample embeddings

### Phase 1.3: Postgres Implementation (3–5 days)
- [ ] Create `DeepWiki.Data.Postgres` project
- [ ] Install Npgsql.EntityFrameworkCore.PostgreSQL + pgvector extension
- [ ] Implement `DocumentEntityConfiguration` with pgvector column type
- [ ] Implement `PostgresVectorDbContext`
- [ ] Implement `PostgresVectorStore` with pgvector distance operators
- [ ] Create initial EF migration for Postgres
- [ ] Add Docker Compose service for Postgres + pgvector
- [ ] Write integration tests: migration, CRUD, IVFFlat/HNSW index creation
- [ ] Test nearest-neighbor queries and compare accuracy with SQL Server

### Phase 1.4: Shared Repository Implementation (2–3 days)
- [ ] Implement `DocumentRepository` (non-vector CRUD) in both providers
- [ ] Add bulk insert/update methods for ingestion pipeline
- [ ] Add transaction support for batch operations
- [ ] Write tests for concurrent upserts and repo-level deletions

### Phase 1.5: Documentation & Handoff (1–2 days)
- [ ] Document EF migration commands and Docker setup
- [ ] Create sample seed data script with embeddings
- [ ] Write performance benchmark report (insert/query latency, index build time)
- [ ] Create README with usage examples and test instructions
- [ ] Tag release and update migration plan with lessons learned

---

## Dependencies & Prerequisites

- .NET 10 SDK
- EF Core 10.x
- SQL Server 2025 preview (or Testcontainers image)
- Postgres 17+ with pgvector extension
- Docker & Docker Compose (for local testing)
- xUnit + FluentAssertions (for tests)
- Testcontainers.SqlServer / Testcontainers.PostgreSQL (optional, for CI)

---

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| SQL Server 2025 vector syntax unavailable or changes | Abstract vector query behind `IVectorStore`; implement fallback using CLR functions or external service |
| Embedding dimension mismatch during ingestion | Add schema validation; enforce dimension check in upsert logic |
| Performance issues with large corpora | Tune index parameters (HNSW M/efConstruction for SQL Server, IVFFlat lists for Postgres); add performance tests early |
| EF Core vector type mapping issues | Use `HasConversion` or raw SQL queries as fallback; document workarounds |

---

## Success Criteria

- ✅ Solution compiles without errors
- ✅ All unit tests pass
- ✅ SQL Server integration tests pass (migration + CRUD + vector query)
- ✅ Postgres integration tests pass (migration + CRUD + vector query)
- ✅ Nearest-neighbor query returns correct top-k results for sample data
- ✅ Performance benchmarks documented (insert 10k docs, query 100 vectors)
- ✅ README and migration guide updated

---

## Next Steps (Step 2 preview)

After Step 1 completes:
- **Step 2**: Implement embedding service & tokenization (port Python `count_tokens`, integrate OpenAI/Ollama/Google embedders)
- **Step 3**: Implement data ingestion pipeline (repo cloning, document reading, chunking, embedding, batch upsert)
- **Step 4**: Implement RAG service & agent orchestration with Microsoft Agent Framework
- **Step 5**: Build ASP.NET Core API + streaming endpoints

---

**Estimated Duration**: 10–16 days (2–3 weeks)  
**Team Size**: 1–2 developers  
**Output**: Fully functional, tested, multi-DB EF Core data layer ready for ingestion and RAG service integration

---

*This plan is ready for use with speckit to create tasks, track progress, and maintain focus on the database/models foundation.*
