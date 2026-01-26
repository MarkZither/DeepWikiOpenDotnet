# Research: RAG Query API

**Feature**: 003-rag-query-api  
**Date**: 2026-01-26  
**Status**: Complete

---

## Research Tasks Resolved

### 1. Existing Service Patterns

**Question**: How does the existing EmbeddingServiceFactory pattern work?

**Findings**:
- Factory reads `Embedding:Provider` configuration to select provider (openai, foundry, ollama)
- `IsProviderAvailable()` checks for required configuration per provider
- `Create()` returns configured `IEmbeddingService` implementation
- Polly retry policies built into factory (exponential backoff)
- NoOpEmbeddingService fallback when no provider configured

**Decision**: Mirror this pattern for VectorStoreFactory with `VectorStore:Provider` configuration.

---

### 2. IVectorStore Implementations

**Question**: What implementations exist and what's their registration pattern?

**Findings**:
- `DeepWiki.Data.SqlServer.VectorStore.SqlServerVectorStoreAdapter` - Full implementation wrapping `IPersistenceVectorStore`
- `DeepWiki.Data.Postgres.Repositories.PostgresVectorStore` implements `IPersistenceVectorStore` but NO `IVectorStore` adapter exists
- `DeepWiki.Rag.Core.VectorStore.NoOpVectorStore` - Placeholder currently registered in Program.cs
- SQL Server registration via `AddSqlServerDataLayer()` registers the adapter

**Decision**: 
1. Create `PostgresVectorStoreAdapter` mirroring SQL Server pattern
2. Create `VectorStoreFactory` to select between providers
3. Replace NoOpVectorStore registration with factory-based registration

**Rationale**: Consistent adapter pattern enables provider-agnostic API layer.

---

### 3. Python API Response Format

**Question**: What response format does the Python API use?

**Findings**:
- Python API returns raw JSON data (no envelope wrapper)
- Errors returned as `{"detail": "error message"}`
- No authentication required (anonymous access)
- Python uses FastAPI's automatic JSON serialization

**Decision**: Match Python format for frontend compatibility:
- Success: Return raw JSON array/object
- Error: Return `{"detail": "..."}` with appropriate HTTP status

**Alternatives Rejected**:
- RFC 7807 Problem Details: More structured but breaks Python parity
- Custom envelope: Adds complexity, not needed for MVP

---

### 4. Polly Resilience Patterns

**Question**: What resilience patterns should be applied?

**Findings**:
- Embedding service calls are external (network latency, failures possible)
- EmbeddingServiceFactory already uses retry policy with exponential backoff
- Vector store operations are database-bound (EF Core handles retries)

**Decision**: Apply Polly policies at API level for embedding service calls:
- Retry: 3 attempts with exponential backoff (1s, 2s, 4s)
- Circuit breaker: Break after 5 failures, half-open after 30s
- Timeout: 30s per operation

**Rationale**: Prevents cascade failures when embedding service is degraded.

---

### 5. Configuration Schema

**Question**: How should provider selection be configured?

**Findings**:
- Existing `Embedding:Provider` pattern works well
- Options pattern with strongly-typed classes is .NET best practice
- Environment variable override needed for deployment flexibility

**Decision**: Use `VectorStore:Provider` with provider-specific subsections:
```json
{
  "VectorStore": {
    "Provider": "sqlserver",
    "SqlServer": {
      "ConnectionString": "...",
      "HnswM": 16,
      "HnswEfConstruction": 200
    },
    "Postgres": {
      "ConnectionString": "...",
      "HnswM": 16,
      "HnswEfConstruction": 200
    }
  }
}
```

**Rationale**: Matches existing configuration patterns; provider-specific settings isolated.

---

### 6. API Endpoint Design

**Question**: POST vs GET for semantic search?

**Findings**:
- GET with query parameters limited by URL length (browser/proxy limits)
- POST with JSON body supports long queries, complex filters
- Python API uses POST for similar endpoints
- REST purists prefer GET for read operations, but pragmatism wins

**Decision**: Use `POST /api/query` with JSON body:
```json
{
  "query": "How do I implement authentication?",
  "k": 10,
  "filters": {
    "repoUrl": "https://github.com/example/repo"
  },
  "includeFullText": true
}
```

**Rationale**: Supports long queries, complex filters, matches Python patterns.

---

### 7. PostgreSQL IVectorStore Adapter Gap

**Question**: Does PostgreSQL need an IVectorStore adapter?

**Findings**:
- `PostgresVectorStore` implements `IPersistenceVectorStore` (persistence layer)
- No adapter implementing `DeepWiki.Data.Abstractions.IVectorStore` exists
- SQL Server has `SqlServerVectorStoreAdapter` bridging this gap
- Factory pattern requires both providers to implement same interface

**Decision**: Create `PostgresVectorStoreAdapter` in `DeepWiki.Data.Postgres`:
- Mirror SQL Server adapter structure
- Delegate to `IPersistenceVectorStore` 
- Map between `DocumentDto` and `DocumentEntity`

**Priority**: Must be done in Milestone 1 before VectorStoreFactory can work.

---

### 8. WebApplicationFactory Testing

**Question**: How should API integration tests be structured?

**Findings**:
- WebApplicationFactory provides in-memory test server
- Can override DI registrations for test doubles
- Testcontainers can provide real database for E2E
- Existing test projects follow this pattern

**Decision**: Two-tier testing approach:
1. **Unit tests**: Mock IVectorStore, IEmbeddingService - test controller logic
2. **Integration tests**: WebApplicationFactory with Testcontainers - test full pipeline

**Test Coverage Targets**:
- All API endpoints tested
- Error scenarios (404, 400, 503) covered
- Pagination logic validated

---

## Summary of Decisions

| Topic | Decision | Rationale |
|-------|----------|-----------|
| Factory pattern | VectorStoreFactory mirroring EmbeddingServiceFactory | Consistency |
| Postgres adapter | Create PostgresVectorStoreAdapter | Enable provider-agnostic API |
| Response format | Raw JSON, errors as `{"detail": "..."}` | Python parity |
| Resilience | Polly retry + circuit breaker for embedding calls | Prevent cascades |
| Configuration | Options pattern with VectorStoreOptions | .NET best practice |
| Query endpoint | POST /api/query with JSON body | Supports long queries |
| Testing | WebApplicationFactory + Testcontainers | Full pipeline coverage |

---

## Open Items

None. All NEEDS CLARIFICATION items resolved.
