# Implementation Plan: RAG Query API

**Branch**: `003-rag-query-api` | **Date**: 2026-01-26 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `/specs/003-rag-query-api/spec.md`

---

## Summary

Expose REST API endpoints for semantic document search, document CRUD operations, and document ingestion in the DeepWiki .NET application. The API integrates with existing IVectorStore, IEmbeddingService, and IDocumentIngestionService abstractions, supporting both SQL Server 2025 and PostgreSQL with pgvector. Implementation uses ASP.NET Core minimal APIs with factory pattern for provider-agnostic registration.

**Key endpoints:**
- `POST /api/query` - Semantic search (embed query → vector similarity → return top-k results)
- `POST /api/documents/ingest` - Batch document ingestion (chunk → embed → upsert)
- `GET/DELETE /api/documents/{id}` - Document CRUD
- `GET /api/documents` - Paginated document listing

**Python API parity:** Raw JSON results, errors as `{"detail": "..."}`, anonymous access for MVP.

---

## Technical Context

**Language/Version**: C# / .NET 10 (ASP.NET Core minimal APIs)  
**Primary Dependencies**: 
- Microsoft.EntityFrameworkCore 10.x (mandatory per constitution)
- Polly (resilience policies)
- Microsoft.AspNetCore.OpenApi (API documentation)
- Existing: IVectorStore, IEmbeddingService, IDocumentIngestionService abstractions

**Storage**: 
- SQL Server 2025 with native vector type (primary)
- PostgreSQL 17+ with pgvector extension (supported alternative)
- Both use HNSW indexing (m=16, ef_construction=200)

**Testing**: 
- xUnit for unit tests
- WebApplicationFactory for API integration tests
- Testcontainers for database integration tests (per constitution)

**Target Platform**: Linux containers (Aspire service defaults)  
**Project Type**: Web API (adding endpoints to existing ApiService)

**Performance Goals**: 
- Query latency <2s for vector stores with 10,000 documents (SC-002)
- Batch ingestion of 100 documents <60s (SC-003)
- Vector query <500ms @ 10K docs, <2s @ 3M docs (constitution requirement)

**Constraints**: 
- Embedding dimension standardized at 1536
- Rate limiting: 100 requests/minute per IP (existing)
- Memory <500MB for bulk operations (constitution)

**Scale/Scope**: Initial target 10K documents, architecture supports 3M+ documents

---

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Rule | Status | Evidence |
|------|--------|----------|
| **I. Test-First** | ✅ PASS | WebApplicationFactory integration tests + unit tests planned for all endpoints |
| **II. Reproducibility** | ✅ PASS | No LLM parsing logic in API layer; embedding service already has snapshot support |
| **III. Local-First ML** | ✅ PASS | EmbeddingServiceFactory supports Ollama (local-first), OpenAI, Foundry |
| **IV. Observability** | ✅ PASS | Aspire service defaults provide structured logging, metrics, traces |
| **V. Security** | ✅ PASS | Rate limiting in place; anonymous access documented as MVP scope |
| **VI. Simplicity** | ✅ PASS | Minimal APIs, no custom middleware, existing factory patterns reused |
| **VII. EF Core Mandatory** | ✅ PASS | IVectorStore implementations use EF Core; no raw SQL |
| **VIII. Agent Framework Compatibility** | ✅ PASS | Existing services (IVectorStore, IEmbeddingService) already Agent Framework-compatible |

**Pre-Design Gate**: PASSED ✅

**Post-Design Re-check**: (to be validated after Phase 1)
- [ ] Result types remain JSON-serializable
- [ ] Error handling uses structured responses, not exceptions
- [ ] All new services callable from agent tool bindings

---

## Project Structure

### Documentation (this feature)

```text
specs/003-rag-query-api/
├── plan.md              # This file
├── research.md          # Phase 0: Technology research
├── data-model.md        # Phase 1: API request/response models
├── quickstart.md        # Phase 1: Developer setup guide
├── contracts/           # Phase 1: OpenAPI specification
│   └── openapi.yaml
└── tasks.md             # Phase 2: Implementation tasks
```

### Source Code (repository root)

```text
src/
├── deepwiki-open-dotnet.ApiService/
│   ├── Program.cs                      # MODIFY: Add VectorStoreFactory, endpoint registration
│   ├── Controllers/                    # NEW: API controllers directory
│   │   ├── QueryController.cs          # NEW: POST /api/query
│   │   └── DocumentsController.cs      # NEW: CRUD + ingest endpoints
│   ├── Models/                         # NEW: API-specific DTOs
│   │   ├── QueryRequest.cs
│   │   ├── QueryResponse.cs
│   │   ├── IngestRequest.cs
│   │   └── IngestResponse.cs
│   └── Configuration/                  # NEW: Options classes
│       └── VectorStoreOptions.cs
├── DeepWiki.Rag.Core/
│   └── VectorStore/
│       └── VectorStoreFactory.cs       # NEW: Factory for provider selection
├── DeepWiki.Data.Postgres/
│   └── DependencyInjection/
│       └── ServiceCollectionExtensions.cs  # MODIFY: Register IVectorStore adapter
└── DeepWiki.Data.SqlServer/
    └── VectorStore/
        └── SqlServerVectorStoreAdapter.cs  # EXISTS: Already implements IVectorStore

tests/
├── deepwiki-open-dotnet.Tests/
│   └── Api/                            # NEW: API integration tests
│       ├── QueryControllerTests.cs
│       ├── DocumentsControllerTests.cs
│       └── ApiTestFixture.cs           # WebApplicationFactory setup
└── DeepWiki.Rag.Core.Tests/
    └── VectorStore/
        └── VectorStoreFactoryTests.cs  # NEW: Factory unit tests
```

**Structure Decision**: Extending existing Aspire-based solution structure. Controllers use minimal API pattern in dedicated files for maintainability. Options pattern for strongly-typed configuration. Tests colocated in existing test projects.

---

## Complexity Tracking

> **No constitution violations requiring justification.**

| Aspect | Decision | Rationale |
|--------|----------|-----------|
| Controller structure | Separate QueryController + DocumentsController | Single responsibility; query has different concerns than CRUD |
| Factory pattern | VectorStoreFactory (mirrors EmbeddingServiceFactory) | Consistency with existing codebase; provider-agnostic registration |
| Error handling | Polly policies + `{"detail": "..."}` format | Python API parity + resilience for external embedding calls |
| Configuration | VectorStoreOptions with provider-specific sections | Clean separation; each provider can have HNSW-specific settings |

---

## Implementation Phases

### Phase 0: Research (Complete)
- [x] Document existing IVectorStore, IEmbeddingService patterns
- [x] Confirm Python API response format (raw results, `{"detail": "..."}` errors)
- [x] Identify missing abstractions (VectorStoreFactory needed)
- [x] Validate EF Core adapter pattern in SqlServerVectorStoreAdapter

### Phase 1: Design & Contracts (Complete)
- [x] Define API request/response DTOs (data-model.md)
- [x] Create OpenAPI specification (contracts/openapi.yaml)
- [x] Design VectorStoreOptions configuration schema
- [x] Document developer quickstart (quickstart.md)
- [x] Update agent context with new technology decisions

**Post-Design Constitution Re-check**: PASSED ✅
- [x] Result types (QueryResultItem, IngestResponse, etc.) are JSON-serializable records
- [x] Error handling uses ErrorResponse `{"detail": "..."}` format, not exceptions
- [x] Services remain callable via DI; no changes to IVectorStore/IEmbeddingService interfaces

### Phase 2: Implementation Tasks (via /speckit.tasks)
1. **Milestone 1 - VectorStoreFactory DI**: Create factory, register in Program.cs, replace NoOpVectorStore
2. **Milestone 2 - Document CRUD**: GET/DELETE /api/documents/{id}, GET /api/documents with pagination
3. **Milestone 3 - Query endpoint**: POST /api/query with embedding + similarity search
4. **Milestone 4 - Ingestion endpoint**: POST /api/documents/ingest with batch processing

---

## Dependencies & Risks

| Dependency | Status | Mitigation |
|------------|--------|------------|
| IVectorStore implementations | ✅ Ready | SQL Server + PostgreSQL adapters exist |
| IEmbeddingService | ✅ Ready | Factory pattern with Ollama/OpenAI/Foundry |
| IDocumentIngestionService | ✅ Ready | Full pipeline in DocumentIngestionService |
| Polly policies | 🆕 To add | Add resilience package; use existing retry patterns |

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Embedding service latency | Medium | High | Polly circuit breaker + timeout; cache embeddings |
| PostgreSQL IVectorStore adapter missing | Medium | Medium | Create adapter mirroring SQL Server pattern |
| Python API parity gaps | Low | Medium | Document differences in quickstart.md |
