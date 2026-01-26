# Feature Specification: RAG Query API

**Feature Branch**: `003-rag-query-api`  
**Created**: 2026-01-26  
**Status**: Draft  
**Input**: User description: "Create a feature specification for a RAG Query API in a .NET 10 ASP.NET Core application. The API should expose REST endpoints for document management and semantic search, integrating with an existing vector store abstraction (IVectorStore), embedding service (IEmbeddingService), and document ingestion pipeline (IDocumentIngestionService). The endpoints should support the existing SQL Server and PostgreSQL vector store implementations with HNSW indexing. Configuration should be provider-agnostic using the existing factory patterns."

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Search Documents by Semantic Query (Priority: P1)

As a developer integrating with the DeepWiki API, I want to submit a natural language query and receive semantically similar documents from the vector store, so that I can build RAG-powered applications without implementing embedding logic myself.

**Why this priority**: This is the core RAG capability that enables all downstream use cases. Without semantic search, the API provides no unique value. This single endpoint validates the entire embedding → vector search → retrieval pipeline.

**Independent Test**: Can be fully tested by sending a POST request to `/api/query` with a text query and receiving ranked document results. Delivers immediate value for any RAG integration.

**Acceptance Scenarios**:

1. **Given** the vector store contains indexed documents, **When** a user sends a POST request to `/api/query` with a text query and optional filters, **Then** the system returns the top-k most semantically similar documents with similarity scores, ordered by relevance.

2. **Given** a valid query request, **When** the embedding service successfully generates an embedding, **Then** the query completes within acceptable latency (target: under 2 seconds for 10 results).

3. **Given** a query with repository filter, **When** the user specifies a `repoUrl` filter, **Then** only documents from that repository are returned.

4. **Given** an empty vector store or no matching documents, **When** a user submits a query, **Then** the system returns an empty results array with a 200 OK status (not an error).

5. **Given** the embedding service is unavailable, **When** a user submits a query, **Then** the system returns a 503 Service Unavailable with a clear error message.

---

### User Story 2 - Ingest Documents via API (Priority: P2)

As a developer, I want to submit documents for ingestion through an API endpoint, so that I can programmatically add content to the vector store without direct database access.

**Why this priority**: Ingestion enables populating the vector store, which is required for meaningful search results. This complements the query endpoint to create a complete RAG workflow.

**Independent Test**: Can be tested by POSTing documents to `/api/documents/ingest` and verifying they appear in subsequent query results.

**Acceptance Scenarios**:

1. **Given** valid document data with text content, **When** a user sends a POST request to `/api/documents/ingest`, **Then** the system chunks the content, generates embeddings, and stores the document in the vector store.

2. **Given** a batch of documents, **When** the user submits multiple documents in a single request, **Then** all documents are processed with a summary response showing success/failure counts.

3. **Given** a document with the same RepoUrl and FilePath already exists, **When** a new version is ingested, **Then** the existing document is updated (upsert behavior).

4. **Given** an invalid document (missing required fields), **When** ingestion is attempted, **Then** the system returns a 400 Bad Request with validation details.

---

### User Story 3 - Retrieve Document by ID (Priority: P3)

As a developer, I want to retrieve a specific document by its ID, so that I can display full document details after identifying it through search.

**Why this priority**: Supports drill-down from search results to full document content. Lower priority because search results already include document data.

**Independent Test**: Can be tested by creating a document, noting its ID, and fetching it via GET `/api/documents/{id}`.

**Acceptance Scenarios**:

1. **Given** a document exists in the vector store, **When** a user sends a GET request to `/api/documents/{id}`, **Then** the full document details are returned including metadata.

2. **Given** a document ID that does not exist, **When** a user requests it, **Then** the system returns a 404 Not Found.

---

### User Story 4 - Delete Document (Priority: P3)

As a developer, I want to delete a document from the vector store, so that I can remove outdated or incorrect content.

**Why this priority**: Maintenance capability needed for production use but not essential for initial demo.

**Independent Test**: Can be tested by creating a document, deleting it via DELETE `/api/documents/{id}`, and verifying it no longer appears in search results.

**Acceptance Scenarios**:

1. **Given** a document exists, **When** a user sends a DELETE request to `/api/documents/{id}`, **Then** the document is removed from the vector store and returns 204 No Content.

2. **Given** a document ID that does not exist, **When** deletion is attempted, **Then** the system returns 404 Not Found.

---

### User Story 5 - List Documents with Pagination (Priority: P4)

As a developer, I want to list all documents in the vector store with pagination, so that I can browse and audit stored content.

**Why this priority**: Administrative capability for visibility into stored data. Lower priority as not required for core RAG functionality.

**Independent Test**: Can be tested by ingesting multiple documents and fetching paginated results via GET `/api/documents`.

**Acceptance Scenarios**:

1. **Given** documents exist in the vector store, **When** a user sends a GET request to `/api/documents` with optional pagination parameters, **Then** a paginated list of documents is returned.

2. **Given** pagination parameters (page, pageSize), **When** the user requests a specific page, **Then** only that page of results is returned with total count metadata.

3. **Given** a repository filter, **When** the user specifies `repoUrl` query parameter, **Then** only documents from that repository are listed.

---

### Edge Cases

- What happens when the configured vector store provider (SQL Server/PostgreSQL) is unavailable at startup?
- How does the system handle concurrent upserts of the same document from multiple requests?
- What happens when embedding dimension mismatches between provider and stored documents?
- How does the system handle extremely large text content that exceeds chunking limits?
- What happens when rate limits are exceeded during batch ingestion?

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST expose a POST `/api/query` endpoint that accepts a text query and returns semantically similar documents from the vector store.
- **FR-002**: System MUST embed the query text using the configured IEmbeddingService before performing vector similarity search.
- **FR-003**: System MUST support optional filters on query (repoUrl, filePath patterns) passed through to IVectorStore.
- **FR-004**: System MUST return query results ordered by similarity score (highest first) with configurable top-k limit (default: 10).
- **FR-005**: System MUST expose a POST `/api/documents/ingest` endpoint that accepts documents for chunking, embedding, and storage.
- **FR-006**: System MUST support batch ingestion of multiple documents in a single request.
- **FR-007**: System MUST implement upsert semantics - updating existing documents when RepoUrl and FilePath match.
- **FR-008**: System MUST expose GET `/api/documents/{id}` to retrieve a single document by ID.
- **FR-009**: System MUST expose DELETE `/api/documents/{id}` to remove a document from the vector store.
- **FR-010**: System MUST expose GET `/api/documents` with pagination support (page, pageSize parameters).
- **FR-011**: System MUST select the vector store provider (SQL Server or PostgreSQL) based on configuration at startup.
- **FR-012**: System MUST use the existing factory patterns (EmbeddingServiceFactory) for provider-agnostic service registration.
- **FR-013**: System MUST return appropriate HTTP status codes (200, 201, 204, 400, 404, 429, 500, 503) based on operation outcomes.
- **FR-014**: System MUST include structured error responses with problem details for client debugging.
- **FR-015**: System MUST respect the existing rate limiting configuration for all new endpoints.

### Key Entities

- **Query Request**: Contains the search text, optional filters (repoUrl, filePath), and result limit (k).
- **Query Response**: Contains an array of VectorQueryResult objects with Document and SimilarityScore.
- **Ingestion Request**: Contains documents to ingest with repository context and optional chunking configuration.
- **Ingestion Response**: Contains success/failure counts, list of ingested document IDs, and any errors.
- **Document**: Represents stored content with Id, RepoUrl, FilePath, Title, Text, metadata, and timestamps.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Developers can perform end-to-end RAG queries (submit text → receive similar documents) via the API.
- **SC-002**: Query endpoint returns results within 2 seconds for vector stores with up to 10,000 documents.
- **SC-003**: Batch ingestion of 100 documents completes within 60 seconds (dependent on embedding provider latency).
- **SC-004**: All endpoints return consistent, well-documented JSON responses that match OpenAPI schema.
- **SC-005**: API integrations can be demonstrated using standard HTTP tools (curl, Postman, REST Client) without custom client code.
- **SC-006**: Switching between SQL Server and PostgreSQL providers requires only configuration changes, no code modifications.
- **SC-007**: API endpoints are discoverable via OpenAPI/Swagger documentation in development mode.

---

## Assumptions

- The existing IVectorStore, IEmbeddingService, and IDocumentIngestionService implementations are production-ready and tested.
- SQL Server 2025 and PostgreSQL with pgvector are available as deployment targets.
- Embedding dimension is standardized at 1536 across all configured providers.
- Authentication/authorization will be added in a future feature (endpoints start with anonymous access for MVP).
- The existing rate limiting configuration (100 requests/minute per IP) is appropriate for initial deployment.
- Aspire service defaults provide health checks and telemetry automatically.

---

## Clarifications

### Session 2026-01-26

- Q: Should the semantic search endpoint use POST `/api/query` or GET `/api/documents/search`? → A: POST `/api/query` with JSON body (supports long queries, cleaner filter encoding)
- Q: Should responses use envelope format or raw results? → A: Raw results (parity with Python API which returns data directly, errors as `{"detail": "..."}`)
- Q: Should vector store provider be selected via appsettings or environment variables? → A: Both with env var override (standard .NET configuration hierarchy)
- Q: What fields should be returned in search results? → A: Configurable via `includeFullText` parameter (default: true for Python parity)
- Q: Should endpoints require authentication? → A: Anonymous access for MVP (parity with Python API)

#### Architecture Decisions (Planning Inputs)

- Q: Controller structure - single or separate controllers? → A: Use separate `DocumentsController` and `QueryController`
- Q: Vector store provider registration pattern? → A: Factory pattern similar to `EmbeddingServiceFactory`

#### Implementation Phases (Planning Inputs)

- Q: Milestone breakdown? → A: 1) Wire real vector store DI, 2) Document CRUD endpoints, 3) Query endpoint, 4) Ingestion endpoint
- Q: Minimum viable endpoint set for end-to-end demo? → A: All endpoints needed (query + ingest + CRUD for complete flow)

#### Error Handling (Planning Inputs)

- Q: Error response format? → A: Standard HTTP status codes with `{"detail": "..."}` format (Python parity)
- Q: Embedding service failure handling during query? → A: Polly policy with backoff and circuit breaker

#### Testing Strategy (Planning Inputs)

- Q: Integration tests for query pipeline? → A: Full pipeline tests (query → embed → search → return)
- Q: API-level tests? → A: Yes, use `WebApplicationFactory` for integration testing

#### Configuration Schema (Planning Inputs)

- Q: appsettings structure for provider selection? → A: Use Options pattern with strongly-typed configuration classes
- Q: Provider-specific HNSW settings? → A: Both SQL Server and PostgreSQL support HNSW - configure via provider-specific options sections (e.g., `VectorStore:SqlServer:HnswM`, `VectorStore:Postgres:HnswEfConstruction`)
