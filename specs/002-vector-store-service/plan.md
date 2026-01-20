# Implementation Plan: Vector Store Service Layer for RAG Document Retrieval

**Branch**: `002-vector-store-service` | **Date**: 2026-01-18 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/002-vector-store-service/spec.md`

## Summary

Implement a **Microsoft Agent Framework-compatible knowledge retrieval abstraction** for semantic document access in SQL Server 2025 using k-NN queries. This Vector Store Service Layer enables Agent Framework agents to retrieve knowledge context during reasoning loops. Support 3 embedding providers (OpenAI API compatibility, Microsoft AI Foundry with Foundry Local emphasis, Ollama) with resilient retry/fallback strategy. Create two new class libraries (`DeepWiki.Data.Abstractions` for shared interfaces, `DeepWiki.Rag.Core` for implementations) providing `IVectorStore`, `ITokenizationService`, and `IEmbeddingService` as Agent Framework-compatible abstractions. Implement SQL LIKE metadata filtering and atomic document upserts. Prioritize Foundry Local and Ollama for production; OpenAI as baseline compatibility. MVP scope: 5 independently testable slices (vector store, tokenization, embedding factory, ingestion, integration tests + docs) plus Agent Framework integration examples.

## Technical Context

**Language/Version**: C# / .NET 10 with EF Core 9.0+  
**Primary Dependencies**: Entity Framework Core, SQL Server, OpenAI SDK, Azure.AI.OpenAI, OllamaSharp (or equivalent Ollama client)  
**Storage**: SQL Server 2025 (primary with `vector(1536)` type); Postgres (pgvector) as pluggable alternative  
**Testing**: xUnit (unit), bUnit (components), TestServer (integration), in-memory SQLite for unit tests, test SQL Server instance for integration  
**Target Platform**: ASP.NET Core web service (.NET 10)  
**Project Type**: Multi-project (2 new class libraries + updates to ApiService)  
**Performance Goals**: <500ms retrieval p95 for 10k document corpus; ≥50 docs/sec embedding throughput; token counting within 2% of Python tiktoken  
**Constraints**: Embedding providers called synchronously; 1536-dimensional vectors fixed; document corpus fits in standard SQL Server storage  
**Scale/Scope**: Support 3 embedding providers (OpenAI, Foundry, Ollama); support document corpus up to 10k+ documents; metadata filtering via SQL LIKE  

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

**Test-First Compliance**: ✅ PASS
- Feature includes 5 user stories with acceptance scenarios; each independently testable (US1-US5 are MVP slices).
- Specification includes 10 measurable success criteria with thresholds (SC-001 to SC-010).
- Plan commits to ≥90% unit test coverage (SC-004), integration tests (SC-005), and tokenization parity tests (SC-002).
- Snapshot testing for LLM embeddings deferred to Agent Orchestration feature (out of scope).

**Reproducibility & Determinism**: ✅ PASS  
- Embeddings are deterministic given same input text and provider model version.
- Token counting must match Python tiktoken (SC-002, ≤2% tolerance).
- Document retrieval ranked by cosine similarity (reproducible).
- Edge case: embedding service retry/fallback is deterministic (max 3 retries with exponential backoff).
- No streaming LLM calls in this feature (streaming endpoints in future feature).

**Local-First ML**: ✅ PASS  
- Ollama is co-equal provider priority with Foundry Local; documentation emphasizes both.
- OpenAI API compatibility baseline for cloud users.
- Provider factory pattern allows swapping without code changes (FR-007).
- Test suite uses in-memory SQLite + mock embeddings (no live provider calls in CI).

**Observability & Cost Visibility**: ⚠️ DEFER  
- Embedding service MUST emit structured logs for provider calls, failures, retries (FR-013).
- Cost tracking (token counts, API spend) deferred to Observability/Monitoring feature.
- LLM snapshots deferred to Agent Orchestration feature (not in MVP scope).

**Security & Privacy**: ✅ PASS  
- Document content stored in DB; no secrets in repos.
- Provider API keys retrieved from secure config (ApiService handles auth).
- Embeddings are numeric vectors (no PII leakage).
- Metadata filtering uses SQL parameterization (no SQL injection).

**Simplicity & Incremental Design**: ✅ PASS  
- Feature breaks into 5 independently deliverable slices.
- Interfaces (IVectorStore, ITokenizationService, IEmbeddingService) are simple and stable.
- Factory pattern for provider selection avoids breaking changes.
- Out-of-scope items deferred (async queues, soft deletes, agent orchestration, streaming).

**Agent Framework Compatibility**: ✅ PASS
- IVectorStore is designed as Agent Framework-compatible abstraction
- VectorQueryResult and related types are JSON-serializable for agent context passing
- Error handling returns clear, agent-recoverable results (not exceptions breaking agent loops)
- Tool binding example provided (queryKnowledge tool using IVectorStore.QueryAsync)
- Agent integration examples in quickstart.md and contracts/agent-integration.md
- E2E agent reasoning tests with knowledge retrieval included in tasks (T238-T242)

**GATE VERDICT**: ✅ **APPROVED** — All core principles satisfied including Agent Framework alignment. Observability/cost tracking deferred to future feature.

## Project Structure

### Documentation (this feature)

```text
specs/002-vector-store-service/
├── spec.md              # Feature specification (clarifications included)
├── plan.md              # This file (implementation plan)
├── research.md          # Phase 0 output: research on SQL Server vector ops, EF Core mapping
├── data-model.md        # Phase 1 output: DocumentEntity, metadata schema
├── contracts/           # Phase 1 output: API contracts (IVectorStore, ITokenizationService)
│   ├── IVectorStore.md
│   ├── ITokenizationService.md
│   ├── IEmbeddingService.md
│   └── provider-factory.md
├── quickstart.md        # Phase 1 output: getting started guide (OpenAI, Foundry, Ollama)
└── checklists/
    ├── requirements.md  # Specification quality checklist
    └── implementation.md # Implementation checklist (TODO)
```

### Source Code (repository root)

```text
src/
├── DeepWiki.Data.Abstractions/          # NEW: Shared interfaces
│   ├── IVectorStore.cs
│   ├── ITokenizationService.cs
│   ├── IEmbeddingService.cs
│   ├── Models/
│   │   ├── DocumentEntity.cs
│   │   ├── VectorQueryResult.cs
│   │   └── EmbeddingRequest.cs
│   └── DeepWiki.Data.Abstractions.csproj
│
├── DeepWiki.Rag.Core/                   # NEW: RAG implementations
│   ├── VectorStore/
│   │   └── SqlServerVectorStore.cs      # k-NN queries via FromSqlInterpolated
│   ├── Tokenization/
│   │   ├── ITokenizationService.cs      # Implementation of interface from Abstractions
│   │   ├── TokenizerFactory.cs          # Provider-specific token encoding
│   │   └── Chunker.cs                   # Text splitting with token limits
│   ├── Embedding/
│   │   ├── EmbeddingServiceFactory.cs   # Provider selection
│   │   ├── Providers/
│   │   │   ├── OpenAIEmbeddingClient.cs      # OpenAI SDK wrapper
│   │   │   ├── FoundryEmbeddingClient.cs     # Azure.AI.OpenAI wrapper
│   │   │   └── OllamaEmbeddingClient.cs      # Ollama client wrapper
│   │   └── RetryPolicy.cs                # Exponential backoff + fallback
│   ├── Ingestion/
│   │   └── DocumentIngestionService.cs  # Upsert, chunking orchestration
│   └── DeepWiki.Rag.Core.csproj
│
└── deepwiki-open-dotnet.ApiService/     # UPDATED: Add RAG service injection
    ├── Program.cs                       # Register IVectorStore, ITokenizationService
    └── Controllers/                     # Future: Add RAG query endpoint

tests/
├── DeepWiki.Data.Abstractions.Tests/    # Tests for Abstractions
│   ├── IVectorStoreTests.cs
│   └── Models/
│
├── DeepWiki.Rag.Core.Tests/             # Tests for Core implementations
│   ├── VectorStore/
│   │   ├── SqlServerVectorStoreTests.cs
│   │   └── VectorStoreIntegrationTests.cs
│   ├── Tokenization/
│   │   ├── TokenizerFactoryTests.cs
│   │   ├── ChunkerTests.cs
│   │   └── TokenizationParityTests.cs   # Compare to Python tiktoken
│   ├── Embedding/
│   │   ├── EmbeddingServiceFactoryTests.cs
│   │   ├── OpenAIEmbeddingClientTests.cs
│   │   ├── FoundryEmbeddingClientTests.cs
│   │   ├── OllamaEmbeddingClientTests.cs
│   │   └── RetryPolicyTests.cs          # Backoff + fallback scenarios
│   └── Ingestion/
│       └── DocumentIngestionServiceTests.cs

./
└── embedding-samples/               # Test data: documents, embeddings, token counts
    ├── python-tiktoken-samples.json # Reference token counts from Python
    ├── sample-documents.json        # Test corpus for retrieval
    └── similarity-ground-truth.json # Expected k-NN results for validation
```

**Structure Decision**: Multi-project architecture with shared abstractions layer enables clean separation of concerns:
- **DeepWiki.Data.Abstractions** keeps interfaces decoupled from implementation (enables future Postgres backend).
- **DeepWiki.Rag.Core** contains all RAG logic (vector store, tokenization, embedding providers, ingestion).
- **ApiService** references both libraries for dependency injection and exposes RAG endpoints.
- Tests colocated with projects for easy maintenance and clear coverage.
- Fixtures directory for test data (documents, token samples, ground-truth results).

---

## Phase 0: Research & Clarifications

**Status**: ✅ COMPLETE (from Clarification phase)

**Topics Resolved**:
1. ✅ Embedding service failure strategy: Retry + fallback caching (FR-014)
2. ✅ Document deduplication: Store separately by repo+path (FR-009)
3. ✅ MVP provider scope: OpenAI API, Foundry, Ollama (FR-005, FR-007)
4. ✅ Metadata filtering: SQL LIKE pattern matching (FR-004)

**Remaining Clarifications** (if any): None at gate — all critical decisions made.

**Assumptions Validated**:
- SQL Server 2025 supports `vector(1536)` column type ✓
- EF Core 9.0+ available in .NET 10 ✓
- Ollama and Foundry APIs publicly documented ✓

---

## Phase 1: Design & Contracts

### 1a. Data Model (`data-model.md` to generate)

**Key Entities**:
- **DocumentEntity**: ID, RepoUrl, FilePath, Title, Text, Embedding (vector), MetadataJson, CreatedAt, UpdatedAt
- **Document**: Domain model (ID, content, metadata, embedding)
- **VectorQueryResult**: Document + similarity score
- **EmbeddingRequest**: Text, metadata, provider hint
- **TokenizationConfig**: Model, max_tokens, language

**Database Schema**:
```sql
CREATE TABLE Documents (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    RepoUrl NVARCHAR(500) NOT NULL,
    FilePath NVARCHAR(MAX) NOT NULL,
    Title NVARCHAR(500),
    Text NVARCHAR(MAX) NOT NULL,
    Embedding vector(1536) NOT NULL,
    MetadataJson NVARCHAR(MAX),  -- JSON: {language, file_type, chunk_index, parent_doc_id}
    CreatedAt DATETIME DEFAULT GETUTCDATE(),
    UpdatedAt DATETIME DEFAULT GETUTCDATE()
);

CREATE UNIQUE INDEX IX_Document_Repo_Path ON Documents (RepoUrl, FilePath);
CREATE INDEX IX_Document_RepoUrl ON Documents (RepoUrl);
CREATE CLUSTERED COLUMNSTORE INDEX IX_Document_Embedding ON Documents (Embedding);
```

**EF Core Mapping**:
```csharp
modelBuilder.Entity<DocumentEntity>()
    .Property(d => d.Embedding)
    .HasColumnType("vector(1536)");
```

### 1b. API Contracts (`contracts/` directory to generate)

**IVectorStore Interface**:
- `Task<IEnumerable<DocumentEntity>> QueryAsync(float[] embedding, int k, Dictionary<string,string> filters, CancellationToken ct)`
  - Returns: k documents ranked by cosine similarity
  - Filters: SQL LIKE patterns for repo_url, file_path, etc.
- `Task UpsertAsync(DocumentEntity doc, CancellationToken ct)`
  - Atomic: insert or update by (repo_url, file_path)
  - Validation: embedding dimensionality check
- `Task DeleteAsync(Guid id, CancellationToken ct)`
  - Hard delete
- `Task RebuildIndexAsync(CancellationToken ct)`
  - Maintenance: refresh columnstore index

**ITokenizationService Interface**:
- `Task<int> CountTokensAsync(string text, string modelId, CancellationToken ct)`
  - Returns: token count for given model
  - Support: OpenAI, Foundry, Ollama models
- `Task<IEnumerable<string>> ChunkAsync(string text, int maxTokens, CancellationToken ct)`
  - Returns: text chunks respecting max_tokens and word boundaries
  - Metadata: each chunk tagged with chunk_index, parent_id

**IEmbeddingService Interface**:
- `Task<float[]> EmbedAsync(string text, CancellationToken ct)`
  - Returns: 1536-dim vector
  - Retry: exponential backoff (3 attempts), fallback to cached
- `IAsyncEnumerable<float[]> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken ct)`
  - Returns: embeddings for multiple texts
  - Batching: configurable batch size
- `string Provider { get; }`
  - Returns: "openai" | "foundry" | "ollama"

### 1c. Quickstart (`quickstart.md` to generate)

- **Setup**: Configuration examples for each provider (appsettings.json, environment variables)
- **Usage**: Code samples for querying, upserting, chunking
- **Testing**: Running integration tests with in-memory SQLite + mock embeddings
- **Deployment**: Running with Ollama locally, Foundry Local on-premises, OpenAI in cloud

### 1d. Agent Context Update

Run `.specify/scripts/bash/update-agent-context.sh copilot` to add:
- Foundry Local APIs and SDK
- Ollama client libraries
- SQL Server vector type and EF Core mapping details

---

## Phase 2: Task Generation (`speckit.tasks` command)

**Deliverables (5 independently testable slices)**:

| Slice | Effort | Dependencies |
|-------|--------|--------------|
| 1. Vector Store (IVectorStore, SqlServerVectorStore, k-NN queries, tests) | 4-5 days | — |
| 2. Tokenization (ITokenizationService, 3 models, Chunker, parity tests) | 3-4 days | — |
| 3. Embedding Factory (3 providers, retry/fallback, provider tests) | 4-5 days | 1, 2 |
| 4. Document Ingestion (upsert, chunking, duplicate detection) | 3-4 days | 1, 2, 3 |
| 5. Integration & Docs (e2e tests, quickstart, contracts, CI setup) | 2-3 days | 1, 2, 3, 4 |

**Total Estimate**: 16-21 days (4-5 weeks with 1-2 developers)

---

## Constitution Re-Check (Post-Design)

✅ **All gates remain APPROVED** — Design maintains test-first, local-first, simplicity principles. Observability deferred as planned.

---

## Next Steps

1. ✅ **Phase 0 complete** — All clarifications integrated into spec
2. ✅ **Phase 1 design complete** — This plan documents architecture and contracts  
3. → **Run `/speckit.tasks`** to generate granular implementation tasks (5 slices)
4. → Begin **Slice 1: Vector Store** implementation
- **DeepWiki.Data.Abstractions** keeps interfaces decoupled from implementation (enables future Postgres backend).
- **DeepWiki.Rag.Core** contains all RAG logic (vector store, tokenization, embedding providers, ingestion).
- **ApiService** references both libraries for dependency injection and exposes RAG endpoints.
- Tests colocated with projects for easy maintenance and clear coverage.
- Fixtures directory for test data (documents, token samples, ground-truth results).
