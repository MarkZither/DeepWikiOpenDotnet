# .NET Migration Plan — Full Migration to ASP.NET Core + Microsoft Agent Framework

**Target**: Full C#/.NET application replacing current Python API. No remaining Python API endpoints; rely on EF Core + SQL Server 2025 (vector type) for vector storage. Use Microsoft Agent Framework for agent orchestration.

---

## Summary

This document outlines a full migration plan to port the `api/` Python package into a single, self-contained .NET 10 application using:

- ASP.NET Core for the HTTP/WebSocket server and streaming endpoints
- Microsoft Agent Framework (MAF) for agent orchestration and prompt management
- EF Core for persistence, including SQL Server 2025's native vector type (`vector(1536)`) via `SqlVector<float>`
- Provider adapters for model providers (Azure OpenAI, OpenAI, Bedrock, OpenRouter, Ollama, Dashscope, etc.)

Goals:
- Feature parity with existing Python API (endpoints, streaming behavior, RAG flows)
- No Python microservices required — embedding, ingestion, indexing, retrieval all handled in .NET
- Pluggable vector-store interface so you can target Postgres (pgvector) later if desired

---

## Key assumptions

- SQL Server 2025 supports a vector column type and EF Core mapping: e.g.

```csharp
[Column(TypeName = "vector(1536)")]
public SqlVector<float> Embedding { get; set; }
```

- Embedding dimensionality is fixed (1536 in this example) and consistent across providers.
- Streaming behavior (text/event-stream & WebSocket) will be implemented using ASP.NET Core with efficient backpressure handling.

---

## High-level architecture

1. ASP.NET Core Web API + SignalR for WebSocket endpoints
2. Microsoft Agent Framework for building agent flows and handling memory, chains, and tools
3. EF Core persistence with DocumentEntity + `SqlServerVectorStore` implementation
4. Provider adapters: `IMLClient` implementations for each model provider
5. Embedding and Tokenization services implemented in .NET (or via a vendor SDK)


### Component responsibilities
- Controllers: translate HTTP requests to domain-level commands and return SSE / streaming responses
- RAG Service: builds prompts, uses `IVectorStore` to retrieve nearest documents, composes the system prompt, calls model adapters
- Vector Store: Upsert/query/delete documents with embeddings and metadata
- Embedding service: calls model provider to create embeddings
- Agent Layer: orchestrates generator flows, enforces output format rules, and handles memory

---

## EF Core data model

```csharp
public class DocumentEntity
{
    public Guid Id { get; set; }
    public string RepoUrl { get; set; }
    public string FilePath { get; set; }
    public string Title { get; set; }
    public string Text { get; set; }

    [Column(TypeName = "vector(1536)")]
    public SqlVector<float> Embedding { get; set; }

    public string MetadataJson { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

DbContext snippet:

```csharp
public class RAGDbContext : DbContext
{
    public DbSet<DocumentEntity> Documents { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DocumentEntity>().HasKey(d => d.Id);
        modelBuilder.Entity<DocumentEntity>().Property(d => d.Embedding).HasColumnType("vector(1536)");
        modelBuilder.Entity<DocumentEntity>().HasIndex(d => d.RepoUrl);
        // Add any provider-specific indexes (ANN index creation managed via migration or script)
    }
}
```


---

## IVectorStore interface (abstraction)

```csharp
public interface IVectorStore
{
    Task UpsertAsync(DocumentEntity doc);
    Task<IEnumerable<DocumentEntity>> QueryAsync(float[] embedding, int k = 10, IDictionary<string,string> filters = null);
    Task DeleteAsync(Guid id);
    Task RebuildIndexAsync();
}
```

Implementation: `SqlServerVectorStore` will use EF Core for inserts and `FromSqlRaw` for nearest-neighbor queries (provider-specific ANN operator or function). Keep implementation testable and pluggable.

Note: exact SQL for nearest neighbor depends on SQL Server 2025 API—use parameterized raw SQL with a placeholder for ANN distance or use a CLR-provided function if required.

Example pseudocode (query):

```sql
-- PSEUDO-SQL: replace ANN_FUNC and syntax with SQL Server 2025 specifics
SELECT TOP (@k) *
FROM Documents
WHERE RepoUrl = @repo -- optional filter
ORDER BY ANN_DISTANCE(Embedding, @query_vector) ASC;
```

Use EF Core `FromSqlInterpolated` to run this safely and map results to `DocumentEntity`.

---

## Embedding & tokenization

- Implement an `IEmbeddingService` that calls chosen provider(s). Wrap providers behind adapters so you can swap providers.
- Implement a tokenizer utility to approximate `tiktoken` for chunking and token counting. For exact parity, consider a thin compatibility service or port of the tokenizer. Add unit tests for chunking parity.
- For batch embedding upserts, implement batching and retry/backoff strategies.

---

## Agent orchestration

- Recreate the `Generator` behavior from `adalflow` using Microsoft Agent Framework by:
  - Implementing MAF tools or skills for the model adapters and the vector store
  - Translating `DataClassParser` formatting checks to an output validation component in .NET
  - Implementing a `RAGAgent` or `RAGSkill` that accepts a user query, retrieves contexts, and calls model adapters with templated prompt
- Implement memory using EF (conversations table) or in-memory caches with persistence.

---

## Endpoints & streaming

- Recreate all FastAPI endpoints as ASP.NET Core controllers. Keep the same route contracts where feasible for frontend parity.
- Streaming SSE: implement via PushStream or IAsyncEnumerable-based Response streaming.
- WebSocket: implement with SignalR or built-in WebSocket middleware; match expected message format.

---

## Testing and validation

- Unit tests for:
  - EF mappings and `SqlVector<float>` round-trip serialization
  - `IVectorStore` queries (using in-memory provider or test SQL Server instance)
  - Embedding and tokenization parity (compare to representative outputs)
  - Agent output validation (ensures answer schema and formatting)
- Integration tests for:
  - End-to-end RAG query → embedding → retrieval → generation
  - Streamed responses and WebSocket flows
- Perf tests for retrieval latency and scaling under realistic corpora

---

## Migration checklist and milestones

1. Draft plan & repo skeleton (this file) — **1–2 days**
2. EF Core model + migrations, DB setup scripts (SQL for vector index) — **2–4 days**
3. Implement `IVectorStore` (SQL Server) + unit tests — **4–7 days**
4. Embedding service & tokenizer + end-to-end ingestion pipeline — **3–5 days**
5. Agent orchestration mapping & basic flow with a single provider — **4–7 days**
6. Streaming endpoints + SignalR WebSocket implementation — **2–4 days**
7. Integration tests + performance tuning — **3–7 days**
8. Documentation, deployment manifests, and cut-over plan — **2–3 days**

Total rough estimate: 4–8 weeks depending on team size and parallelization.

---

## Risks & mitigations

- **Embedding dimension mismatch**: enforce schema check during ingestion; maintain a migration plan if embeddings change.
- **Nearest-neighbor SQL syntax changes**: abstract query logic behind `IVectorStore` and make SQL-specific code isolated and tested.
- **Tokenization mismatch**: produce unit tests comparing tokenization and chunking behaviors; consider porting tokenizer for exact parity.
- **Performance**: tune index parameters and batch sizes; plan capacity testing early.

---

## Next steps

- Implement prototype `DocumentEntity`, `RAGDbContext`, and a minimal `SqlServerVectorStore` with sample query and migration.
- Build integration tests verifying retrieval accuracy on a small sample dataset.
- Map `adalflow` flows into MAF components with a simple agent proof-of-concept.


---

*File generated by GitHub Copilot on request.*
