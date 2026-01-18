# Feature Specification: Vector Store Service Layer for RAG Document Retrieval

**Feature Branch**: `002-vector-store-service`  
**Created**: 2026-01-18  
**Status**: Draft  
**Input**: Implement Vector Store Service Layer for RAG Document Retrieval

---

## Overview

The .NET application needs a pluggable abstraction layer for semantic document retrieval against SQL Server 2025's native vector type. Currently, document management relies on basic relational queries; we need the ability to store embeddings and retrieve semantically similar documents (k-nearest neighbors) to enable RAG (Retrieval Augmented Generation) flows. This layer must support multiple embedding providers (OpenAI, Azure, Bedrock, Google, Ollama) and allow future swapping of vector storage backends without changing application code.

## Clarifications

### Session 2026-01-18

- Q: When embedding provider is unreachable, should the system retry/fail/queue? → A: Retry with exponential backoff (max 3 attempts), then fall back to cached/secondary embedding if available, else fail with clear error
- Q: When identical content exists in different repos/paths, deduplicate or store separately? → A: Store both as separate documents; each independently retrievable with own embedding and file path context
- Q: Should MVP include all 5 embedding providers or reduce scope? → A: Implement 3 core providers: OpenAI (API compatibility baseline), Microsoft AI Foundry (Foundry Local), Ollama (local development). Documentation emphasizes Foundry and Ollama. Others added in follow-up feature.
- Q: For metadata filter matching (e.g., file_path: "*.md"), use LIKE / regex / string contains? → A: SQL LIKE pattern matching (SQL Server native). Intuitive for file paths, performant, aligns with common practice.

---

## User Scenarios & Testing

### User Story 1 - Query Similar Documents for RAG Context (Priority: P1)

When a user submits a question to the chat endpoint, the system needs to find the most relevant documents from the knowledge base to include in the LLM prompt. This is the core RAG workflow.

**Why this priority**: This is the essential MVP feature—without document retrieval, RAG cannot function. Every chat query depends on this capability.

**Independent Test**: Can be tested independently by:
1. Storing 10-20 sample documents with embeddings in the database
2. Querying with a known embedding
3. Verifying the top k results are the most similar documents

**Acceptance Scenarios**:

1. **Given** documents are stored with embeddings, **When** a user provides a query, **Then** the system generates a query embedding and retrieves the top 10 most similar documents ranked by cosine similarity
2. **Given** multiple documents exist in multiple repositories, **When** filtered by repository URL, **Then** only documents from that repository are returned
3. **Given** a query has no matching documents, **When** the query executes, **Then** an empty result set is returned (not an error)
4. **Given** a query embedding matches many documents, **When** k=5 is specified, **Then** exactly 5 results are returned
5. **Given** documents with identical metadata, **When** both match the query, **Then** both are returned ranked by similarity

---

### User Story 2 - Ingest and Index Documents with Embeddings (Priority: P1)

When processing a repository (GitHub, GitLab, etc.), the system must accept raw document content, calculate embeddings, and persist them efficiently to enable future retrieval.

**Why this priority**: Co-equal with querying—you can't retrieve documents without first ingesting and storing them. Ingestion is blocking for any knowledge base population.

**Independent Test**: Can be tested independently by:
1. Providing raw document content (title, text, file path, repo metadata)
2. Calling the upsert endpoint
3. Verifying document is stored with embedding and metadata
4. Querying by the same content to ensure it's retrievable

**Acceptance Scenarios**:

1. **Given** a document with title, content, and metadata, **When** upsert is called, **Then** the system calculates an embedding and stores the document with all metadata in SQL Server
2. **Given** a document with the same file path and repo already exists, **When** upsert is called, **Then** the document is updated without creating a duplicate
3. **Given** a batch of 100 documents to ingest, **When** batch upsert is called, **Then** all documents are processed with retry logic and backoff on provider rate limits
4. **Given** an embedding service call fails, **When** retry logic is triggered, **Then** the operation retries with exponential backoff before failing
5. **Given** documents are upserted successfully, **When** queried, **Then** they are immediately available for retrieval

---

### User Story 3 - Validate Document Chunks Respect Token Limits (Priority: P1)

During ingestion, documents are split into chunks for embedding. The system must prevent chunks from exceeding the embedding model's token limit to avoid embedding failures.

**Why this priority**: P1—tokenization validation prevents runtime failures and ensures chunking parity with Python implementation. Without this, we risk invalid embeddings.

**Independent Test**: Can be tested independently by:
1. Creating text that spans multiple token counts
2. Calling chunking logic with different token limits
3. Verifying each chunk is under the limit
4. Comparing token counts to Python tiktoken for 10+ representative text samples

**Acceptance Scenarios**:

1. **Given** a 50,000 token document and 8192 token limit, **When** chunking is executed, **Then** document is split into chunks each under 8192 tokens
2. **Given** chunk boundaries between words, **When** chunking completes, **Then** chunks respect word boundaries (no mid-word splits)
3. **Given** text in multiple languages, **When** token counting is executed, **Then** tokens are counted correctly for all supported embedding models (OpenAI, Google, Bedrock, Ollama)
4. **Given** Python tiktoken reference outputs, **When** .NET tokenizer processes the same text, **Then** token count difference is ≤2% (accounting for minor encoding variations)

---

### User Story 4 - Support Multiple Embedding Providers (Priority: P2)

The system must abstract the embedding provider so operators can choose OpenAI, Microsoft AI Foundry, or Ollama without code changes. Focus is on Foundry Local and Ollama for deployment; OpenAI as API compatibility baseline.

**Why this priority**: P2—enables flexibility and cost optimization. MVP focuses on Foundry (production on-premises) and Ollama (local/testing). OpenAI serves as compatibility baseline. Support for Bedrock and Google deferred to follow-up feature.

**Independent Test**: Can be tested independently by:
1. Configuring each provider (via environment variables or config)
2. Calling embedding service with a test string
3. Verifying correct provider client is instantiated
4. Comparing embedding vector dimensions

**Acceptance Scenarios**:

1. **Given** configuration specifies OpenAI, **When** embedding service initializes, **Then** OpenAI-compatible client is used for embedding calls
2. **Given** configuration specifies Microsoft AI Foundry, **When** embedding service initializes, **Then** Foundry client is used with configured model
3. **Given** configuration specifies Ollama, **When** embedding service initializes, **Then** Ollama client is used with local model
4. **Given** embedding provider is changed in configuration, **When** service is reinitialized, **Then** new provider is used without application restart
5. **Given** provider API fails, **When** embedding call is attempted, **Then** error is logged and propagated with provider context

---

### User Story 5 - Support Metadata Filtering in Retrieval (Priority: P2)

Users want to limit searches to specific repositories or file types. The vector store must support optional metadata filters without requiring full table scans.

**Why this priority**: P2—nice-to-have for MVP but important for usability in production with large corpora. Can default to "no filter" initially.

**Independent Test**: Can be tested independently by:
1. Storing documents with different repo URLs and file paths
2. Querying with metadata filters
3. Verifying only matching documents are returned

**Acceptance Scenarios**:

1. **Given** documents from repos A and B exist, **When** filtered by repo A, **Then** only repo A documents are returned
2. **Given** filter for `{repo_url: "%a%", file_path: "%.md"}`, **When** query executes with SQL LIKE matching, **Then** only documents matching both patterns are returned (e.g., `file_path` ending in `.md`)
3. **Given** invalid filter keys, **When** query executes, **Then** filter is ignored or error is clearly reported

---

### Edge Cases

- **Embedding service unavailable**: System retries with exponential backoff (3 attempts); on failure, falls back to cached/secondary embedding if available; if no cache exists, operation fails with clear "provider unavailable" error
- **Documents with identical embeddings**: Both are stored as separate documents (no deduplication); retrieval ranking handles this transparently
- **SQL Server vector index corruption**: RebuildIndexAsync() operation triggered manually; corrupted documents identified and flagged for re-embedding
- **Extremely long documents (>100K tokens)**: Documents are pre-chunked during ingestion to respect 8192 token limit; chunks stored as separate documents with parent reference in metadata
- **Concurrent upsert to same document**: First write wins; subsequent write detects conflict (by repo + file path) and updates existing document atomically or fails with clear conflict error

---

## Requirements

### Functional Requirements

- **FR-001**: System MUST expose `IVectorStore` interface with `QueryAsync(embedding[], k, filters)`, `UpsertAsync(document)`, `DeleteAsync(id)`, and `RebuildIndexAsync()` methods
- **FR-002**: System MUST implement `SqlServerVectorStore` that uses SQL Server 2025 vector type with parameterized k-NN queries via `FromSqlInterpolated`
- **FR-003**: System MUST retrieve documents ranked by cosine similarity to query embedding, with highest similarity first
- **FR-004**: System MUST support optional metadata filtering (e.g., by repo URL, file path) using SQL LIKE pattern matching without requiring full table scans (e.g., `file_path LIKE '%.md'` matches all markdown files)
- **FR-005**: System MUST provide `ITokenizationService` that counts tokens accurately for OpenAI, Microsoft AI Foundry, and Ollama embedding models in MVP
- **FR-006**: System MUST implement text chunking that respects token limits (8192 max) and maintains word boundaries
- **FR-007**: System MUST provide `IEmbeddingService` with factory pattern supporting OpenAI (API compatibility), Microsoft AI Foundry, and Ollama providers in MVP; abstraction enables adding Bedrock and Google in follow-up feature
- **FR-008**: System MUST implement batch embedding with configurable batch size and exponential backoff retry on rate limits
- **FR-009**: System MUST support document upsert without creating duplicates (identified by repo + file path)
- **FR-010**: System MUST validate upsert operations succeed or fail atomically (no partial updates)
- **FR-011**: System MUST persist document metadata (repo URL, file path, title, created timestamp) alongside embeddings
- **FR-012**: System MUST support concurrent upsert operations without data corruption
- **FR-013**: System MUST provide clear error messages when embedding or token counting operations fail, with context about which provider or model caused the failure
- **FR-014**: System MUST implement exponential backoff retry (max 3 attempts) for embedding service calls; if all retries fail, fall back to cached/secondary embedding if available, otherwise fail with clear error message indicating provider unavailability

### Key Entities

- **Document**: Represents a code/documentation file with content and metadata (ID, repo URL, file path, title, text, embedding, metadata JSON, created timestamp)
- **Embedding**: A 1536-dimensional float vector representing semantic meaning of document text
- **Metadata**: Key-value filters for narrowing search results (repo URL, file path, language, etc.)
- **VectorQueryResult**: Result set containing ranked documents with similarity scores

---

## Success Criteria

### Measurable Outcomes

- **SC-001**: Retrieval queries complete in <500ms for corpora up to 10,000 documents (measured on standard test SQL Server instance)
- **SC-002**: Token counting produces identical results to Python tiktoken for ≥95% of representative text samples
- **SC-003**: Embedding service successfully handles ≥50 documents per second with batching and exponential backoff retry logic (including fallback to cached embeddings)
- **SC-004**: Unit tests achieve ≥90% code coverage for `IVectorStore`, `ITokenizationService`, and `IEmbeddingService` implementations
- **SC-005**: Integration tests demonstrate end-to-end flow (document ingest → embed → store → retrieve) completing successfully
- **SC-006**: K-NN retrieval accuracy verified against ground-truth results (top 5 retrieved documents match expected similar documents)
- **SC-007**: Metadata filtering reduces result set by ≥95% when applied to large corpus
- **SC-008**: Architecture supports swapping SQL Server implementation for Postgres (pgvector) without changing consumer code
- **SC-009**: Documentation includes examples of: configuring OpenAI, Microsoft AI Foundry, and Ollama providers; using IVectorStore; extending with additional providers
- **SC-010**: Zero data loss on upsert (all documents successfully persisted with correct embeddings and metadata)

---

## Assumptions

- SQL Server 2025 supports `vector(1536)` column type mapped via EF Core with `SqlVector<float>`
- All embedding providers (OpenAI, Foundry, Ollama) return 1536-dimensional vectors
- EF Core 9.0+ is available in .NET 10
- Microsoft AI Foundry APIs are available and compatible with standard embedding interface
- Embedding providers are called synchronously (async/queuing deferred to future feature if needed)
- Document corpus fits in SQL Server with standard storage (no archival needed for MVP)
- Conversation history versioning not required (mutable in memory OK for this feature)
- Hard deletes are acceptable; soft deletes/versioning deferred to future feature

---

## Out of Scope (MVP Phase)

- Additional embedding providers (Bedrock, Google) - planned for follow-up feature
- Agent orchestration layer (separate feature)
- Streaming chat endpoints (separate feature)
- WebSocket/SignalR implementation (separate feature)
- Authentication/authorization (handled by existing ApiService)
- Conversation history persistence (deferred to future feature)
- Document versioning or soft deletes (deferred to future feature)
- Async embedding job queues (deferred to future feature)
- UI/frontend components

---

## Implementation Notes

- New class library `DeepWiki.Rag.Core` for RAG service, vector store, tokenization, embedding service
- New class library `DeepWiki.Data.Abstractions` for shared interfaces (`IVectorStore`, `ITokenizationService`, `IEmbeddingService`)
- ApiService references both libraries for dependency injection
- Port tokenization logic from `data_pipeline.py` (tiktoken wrapper)
- Port embedding factory from `tools/embedder.py`
- Port retrieval patterns from `rag.py` (context selection logic)
- All unit tests mock external services; integration tests use test SQL Server instance
