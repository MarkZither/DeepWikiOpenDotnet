# Feature Specification: Multi-Database Data Access Layer

**Feature Branch**: `1-multi-db-data-layer`  
**Created**: 2026-01-16  
**Status**: Draft  
**Input**: User description: "create data access layer for a multi-database deepwiki clone"

## Clarifications

### Session 2026-01-16

- Q: How should database connection strings with credentials be managed? → A: Use User Secrets (development), environment variables or Key Vault (production)
- Q: What should happen when database operations fail due to transient errors (network timeout, connection pool exhaustion)? → A: Retry 3x exponential backoff (EF Core built-in or Polly if superior); circuit breaker for persistent DB unavailability
- Q: What is the maximum expected database size (document count) before performance degrades unacceptably? → A: 3 million documents
- Q: What diagnostic information should be logged or exposed when database startup validation fails (version checks, extension availability)? → A: Log error + version details, expose health endpoint

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Store Document with Vector Embedding (Priority: P1)

As a developer integrating the data layer, I need to store document entities with their vector embeddings so that the application can persist knowledge base content with semantic search capabilities.

**Why this priority**: This is the foundation of the entire data layer - without the ability to store documents and their embeddings, no other functionality is possible. This represents the minimum viable data layer.

**Independent Test**: Can be fully tested by creating a DocumentEntity with text and a 1536-dimensional embedding vector, calling the repository's AddAsync method, and verifying the document is retrievable with all properties intact including the embedding vector.

**Acceptance Scenarios**:

1. **Given** a DocumentEntity with Id, RepoUrl, FilePath, Title, Text, and a 1536-dimensional float array embedding, **When** the developer calls AddAsync on the repository, **Then** the document is persisted to the database with all properties including the embedding vector stored correctly
2. **Given** a document has been stored with an embedding, **When** the developer retrieves it by Id, **Then** the returned entity contains the exact same embedding vector (all 1536 dimensions match)
3. **Given** a document with metadata (FileType, IsCode, IsImplementation, TokenCount), **When** stored and retrieved, **Then** all metadata properties are preserved accurately

---

### User Story 2 - Query Similar Documents by Vector (Priority: P1)

As a developer building semantic search, I need to query documents by vector similarity so that users can find relevant documents based on meaning rather than just keywords.

**Why this priority**: Vector similarity search is the core value proposition of the data layer for a RAG system. Without this capability, storing embeddings provides no benefit.

**Independent Test**: Can be fully tested by storing multiple documents with different embeddings, calling QueryNearestAsync with a query embedding and k=10, and verifying that the returned documents are ordered by cosine similarity distance.

**Acceptance Scenarios**:

1. **Given** 20 documents stored with embeddings, **When** a developer queries with a target embedding and k=10, **Then** the system returns the 10 most similar documents ordered by cosine similarity (closest first)
2. **Given** documents from multiple repositories, **When** querying with a repoUrlFilter parameter, **Then** only documents from the specified repository are considered in similarity search
3. **Given** a query embedding that matches exactly one document, **When** k=5, **Then** that document appears first with a distance of 0 (or near-zero allowing for floating-point precision)

---

### User Story 3 - Switch Between SQL Server and PostgreSQL (Priority: P2)

As a system administrator, I need to configure the application to use either SQL Server or PostgreSQL as the vector database so that deployment can adapt to existing infrastructure and organizational standards.

**Why this priority**: Multi-database support is a key requirement but not essential for initial development. A single database implementation (P1) allows early testing while this P2 story enables production flexibility.

**Independent Test**: Can be tested by running the same test suite against both SqlServerVectorStore and PostgresVectorStore implementations, verifying both pass identical acceptance tests for storage and retrieval operations.

**Acceptance Scenarios**:

1. **Given** configuration specifies SQL Server connection string, **When** the application starts, **Then** all vector operations use SQL Server 2025's native vector type
2. **Given** configuration specifies PostgreSQL connection string, **When** the application starts, **Then** all vector operations use PostgreSQL's pgvector extension
3. **Given** a set of documents stored in SQL Server, **When** the same data is migrated to PostgreSQL, **Then** vector similarity queries return identical results (within floating-point tolerance)

---

### User Story 4 - Bulk Operations for Repository Ingestion (Priority: P2)

As a developer building document ingestion pipelines, I need to add or update multiple documents in a single transaction so that importing an entire repository is efficient and atomic.

**Why this priority**: While individual document operations work (P1), bulk operations are essential for production performance when ingesting large repositories with hundreds or thousands of files.

**Independent Test**: Can be tested by calling a bulk upsert method with 100 documents, verifying all are inserted in a single database transaction (by checking transaction logs or using a transaction scope), and confirming performance is significantly better than 100 individual calls.

**Acceptance Scenarios**:

1. **Given** 100 new DocumentEntity objects, **When** calling BulkUpsertAsync, **Then** all documents are inserted in a single transaction and commit together atomically
2. **Given** 50 existing documents and 50 new documents, **When** calling BulkUpsertAsync with all 100, **Then** existing documents are updated and new documents are inserted without errors
3. **Given** a bulk operation fails mid-transaction, **When** the transaction rolls back, **Then** no partial data is persisted to the database

---

### User Story 5 - Delete Documents by Repository (Priority: P3)

As a developer managing the knowledge base, I need to delete all documents belonging to a specific repository so that outdated or removed repositories can be cleaned from the system.

**Why this priority**: Deletion is important for data hygiene but not essential for initial functionality. Documents can be added and queried (P1-P2) before implementing comprehensive deletion features.

**Independent Test**: Can be tested by adding documents from three different repositories, calling DeleteByRepoAsync for one repository, and verifying only that repository's documents are removed while others remain.

**Acceptance Scenarios**:

1. **Given** documents from repositories A, B, and C exist in the database, **When** calling DeleteByRepoAsync("repo-A-url"), **Then** all documents with RepoUrl matching "repo-A-url" are deleted and documents from B and C remain
2. **Given** a repository has 1000 documents, **When** calling DeleteByRepoAsync, **Then** all 1000 documents are deleted in a performant manner (under 5 seconds)
3. **Given** no documents exist for a repository, **When** calling DeleteByRepoAsync, **Then** the operation completes successfully without errors

---

### Edge Cases

- What happens when an embedding vector has fewer or more than 1536 dimensions?
  - System validates embedding length before storage and throws ArgumentException with clear message
- How does the system handle storing a document with a null or empty embedding?
  - Null embeddings are allowed (for documents pending embedding generation), but vector search operations skip these documents
- What happens when querying with k=10 but only 5 documents exist in the database?
  - System returns all 5 available documents without errors
- How does the system handle concurrent writes to the same document ID?
  - Database enforces optimistic concurrency using UpdatedAt timestamp; last write wins with ConcurrencyException on conflict
- What happens when a repository URL filter doesn't match any documents?
  - QueryNearestAsync returns an empty list (not null or exception)
- How does the system handle extremely large metadata JSON (>10KB)?
  - Database schema allows up to 1MB for MetadataJson; larger values throw DataException at persistence layer
- What happens during a vector similarity query when the database contains documents without embeddings?
  - Documents with null embeddings are automatically excluded from vector search results

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide a DocumentEntity class with properties: Guid Id, string RepoUrl (required, max 2000 chars), string FilePath (required, max 1000 chars), string Title (optional, max 500 chars), string Text (required, max 50MB), float[] Embedding (optional, exactly 1536 dimensions when non-null), string FileType (optional), bool IsCode, bool IsImplementation, int TokenCount, DateTime CreatedAt, DateTime UpdatedAt, string MetadataJson (optional)
- **FR-002**: System MUST provide IVectorStore interface with methods: Task UpsertAsync(DocumentEntity doc), Task<List<DocumentEntity>> QueryNearestAsync(float[] queryEmbedding, int k = 10, string? repoUrlFilter = null), Task DeleteAsync(Guid id), Task DeleteByRepoAsync(string repoUrl), Task<int> CountAsync(string? repoUrlFilter = null)
- **FR-003**: System MUST provide IDocumentRepository interface with methods: Task<DocumentEntity?> GetByIdAsync(Guid id), Task<List<DocumentEntity>> GetByRepoAsync(string repoUrl, int skip = 0, int take = 100), Task AddAsync(DocumentEntity doc), Task UpdateAsync(DocumentEntity doc), Task DeleteAsync(Guid id), Task<bool> ExistsAsync(Guid id)
- **FR-004**: System MUST implement IVectorStore for SQL Server 2025 using the native vector(1536) column type with HNSW indexing for cosine similarity search
- **FR-005**: System MUST implement IVectorStore for PostgreSQL 17+ using pgvector extension with cosine distance operator (<=>)
- **FR-006**: System MUST provide SqlServerVectorStore that uses VECTOR_DISTANCE() SQL function for similarity calculations returning results ordered by distance ascending
- **FR-007**: System MUST provide PostgresVectorStore that uses the <=> cosine distance operator for similarity calculations returning results ordered by distance ascending
- **FR-008**: System MUST implement each database provider in a separate project (DeepWiki.Data.SqlServer, DeepWiki.Data.Postgres) depending on a base DeepWiki.Data project containing interfaces and DocumentEntity
- **FR-009**: System MUST serialize/deserialize MetadataJson using System.Text.Json with case-insensitive property matching
- **FR-010**: System MUST automatically set CreatedAt to UTC now on document creation and UpdatedAt to UTC now on every update
- **FR-011**: System MUST validate embedding dimensions (exactly 1536 floats when non-null) before database operations and throw ArgumentException if invalid
- **FR-012**: System MUST support upsert semantics (insert if new, update if exists) in IVectorStore.UpsertAsync based on document Id
- **FR-013**: System MUST use EF Core 10.x for all database operations with async/await patterns throughout
- **FR-013a**: System MUST implement retry policy for transient database failures (3 retries with exponential backoff) using EF Core EnableRetryOnFailure or Polly if superior
- **FR-013b**: System MUST implement circuit breaker pattern to fail fast when database is persistently unavailable
- **FR-014**: System MUST support dependency injection registration for IVectorStore and IDocumentRepository with scoped lifetime
- **FR-015**: System MUST provide separate DbContext classes for SQL Server (SqlServerVectorDbContext) and PostgreSQL (PostgresVectorDbContext) inheriting from DbContext
- **FR-016**: System MUST validate database version and extension availability at startup and log errors with version details when validation fails
- **FR-017**: System MUST expose health check endpoint indicating database connectivity and version compatibility status

### Key Entities *(include if feature involves data)*

- **DocumentEntity**: Represents a document in the knowledge base with its text content, vector embedding, and metadata. Contains identity (Id), source location (RepoUrl, FilePath), content (Title, Text, Embedding), classification (FileType, IsCode, IsImplementation), metrics (TokenCount), and timestamps (CreatedAt, UpdatedAt). The Embedding property is a 1536-dimensional float array representing semantic meaning. MetadataJson stores additional properties as JSON.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Developers can store a document with a 1536-dimensional embedding and retrieve it with all properties intact in under 100ms for local database
- **SC-002**: Vector similarity search returns the 10 nearest documents from a collection of 10,000 documents in under 500ms with proper indexing (HNSW or IVFFlat)
- **SC-002a**: Vector similarity search maintains acceptable performance (<2 seconds) at scale up to 3 million documents with proper indexing
- **SC-003**: The same test suite passes against both SQL Server and PostgreSQL implementations with 100% test parity (no database-specific test exclusions)
- **SC-004**: Bulk upsert operations handle 1000 documents in under 10 seconds with proper batching and transaction management
- **SC-005**: All public methods in IVectorStore and IDocumentRepository have corresponding unit tests achieving 90%+ code coverage
- **SC-006**: Integration tests successfully run against SQL Server 2025 in Docker container (Testcontainers) and verify vector operations work end-to-end
- **SC-007**: Integration tests successfully run against PostgreSQL 17 with pgvector in Docker container (Testcontainers) and verify vector operations work end-to-end
- **SC-008**: Database migrations are generated for both SQL Server and PostgreSQL and can be applied to fresh databases without errors
- **SC-009**: Developers can switch between SQL Server and PostgreSQL by changing a single configuration setting (connection string provider) without code changes
- **SC-010**: Documentation includes code examples showing how to register and use both database providers via dependency injection
- **SC-011**: Health check endpoint returns database status and fails gracefully with detailed error messages when version requirements are not met

## Assumptions *(mandatory)*

- Documents will have embeddings generated before being stored (null embeddings allowed temporarily but not for vector search)
- Vector dimensions are fixed at 1536 (OpenAI embedding model standard) and won't change dynamically
- Cosine similarity is the appropriate distance metric for all vector comparisons (not Euclidean or dot product)
- Database instances are properly configured with appropriate collations (case-insensitive string comparisons)
- Network latency between application and database is minimal (local or same-region deployment)
- SQL Server 2025 or later is available for deployments choosing SQL Server (for native vector type support)
- PostgreSQL 17 or later with pgvector extension is available for deployments choosing PostgreSQL
- Maximum document text size of 50MB is sufficient for all anticipated documents
- Concurrent write conflicts are rare enough that optimistic concurrency (UpdatedAt checks) is acceptable
- Developers will use dependency injection for all repository and vector store access
- Connection strings use User Secrets in development; production deployments use environment variables or secure vaults (Azure Key Vault, HashiCorp Vault)
- Transient database failures are temporary and retrying with exponential backoff resolves most issues within 3 attempts
- Circuit breaker thresholds (failure count, timeout duration) will be tuned based on operational requirements
- Maximum database size will not exceed 3 million documents; performance optimization and indexing strategies are designed for this scale

## Scope *(mandatory)*

### In Scope

- DocumentEntity class definition with all required properties
- IVectorStore and IDocumentRepository interfaces
- SQL Server implementation with vector(1536) column type and HNSW indexing
- PostgreSQL implementation with pgvector extension and cosine distance operator
- EF Core DbContext classes for both providers
- Entity Framework Core migrations for both databases
- Unit tests for entity model, interfaces, and shared logic
- Integration tests using Testcontainers for both SQL Server and PostgreSQL
- Dependency injection configuration examples
- Basic repository operations (CRUD) for documents
- Vector operations (upsert, nearest neighbor search with k parameter, delete, count)
- Repository filtering (by RepoUrl) for queries
- Validation of embedding dimensions (1536 floats)
- Automatic timestamp management (CreatedAt, UpdatedAt)
- Upsert semantics (insert or update based on Id)
- Health check endpoint for database connectivity and version validation
- Startup validation logging for database version and extension availability

### Out of Scope

- Embedding generation pipeline (handled in separate feature)
- Document text processing or transformation logic
- API layer or HTTP endpoints (handled in separate feature)
- Authentication or authorization for data access
- Data migration tools for moving between SQL Server and PostgreSQL
- Performance optimization beyond basic indexing (HNSW/IVFFlat)
- Distributed transactions across multiple databases
- Read replicas or database scaling strategies
- Backup and recovery procedures
- Database monitoring or observability instrumentation (metrics, logging beyond basic EF Core logging)
- Multi-tenancy support or data isolation beyond RepoUrl filtering
- Full-text search capabilities (keyword-based search)
- Document versioning or audit trails
- Soft deletes (deletion is permanent)
- Batch processing frameworks or job scheduling
- Connection pooling configuration (uses EF Core defaults)
- Database security hardening (encryption at rest, TLS, etc.)

## Dependencies *(mandatory)*

### Internal Dependencies

- `.specify/memory/constitution.md`: Defines test-first development requirements, storage policies, and EF Core standards
- `docs/step1_database_models_plan.md`: Provides detailed implementation guidance for the three-project structure (base, SQL Server, PostgreSQL)
- `.NET 10 SDK`: Required for development and building the projects

### External Dependencies

- **EF Core 10.x**: ORM for database operations and migrations
- **System.Text.Json**: Metadata JSON serialization
- **SQL Server 2025 or later**: For native vector(1536) type support (only if SQL Server provider is used)
- **PostgreSQL 17+ with pgvector extension**: For vector similarity search (only if PostgreSQL provider is used)
- **xUnit**: Unit testing framework
- **Testcontainers**: Integration testing with Docker containers for database provisioning

### Deployment Dependencies

- Docker or compatible container runtime (for integration tests)
- SQL Server 2025 instance or PostgreSQL 17+ instance (based on chosen provider)
- Network connectivity from application to database instance

## Risks & Mitigations *(mandatory)*

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| SQL Server 2025 vector type not available in target environment | High - Cannot deploy SQL Server provider | Medium | Provide PostgreSQL alternative; document minimum SQL Server version requirements clearly; add version check at startup |
| pgvector extension not installed or incompatible version | High - Cannot deploy PostgreSQL provider | Low | Document extension installation steps; add extension version check at startup; include installation script in docs |
| EF Core 10.x doesn't fully support vector types | High - May need custom value converters | Low | Test EF Core vector support early (Phase 1.1); prepare custom converters if needed; monitor EF Core 10 release notes |
| Vector similarity performance inadequate for large datasets | Medium - Slow query response | Medium | Implement proper indexing (HNSW/IVFFlat) from start; benchmark with realistic data volumes; document index configuration |
| Embedding dimension mismatches (not 1536) cause runtime errors | Medium - Application crashes | Medium | Add validation in DocumentEntity; validate before UpsertAsync; throw clear exceptions; add integration tests for validation |
| Memory issues with large embedding arrays in bulk operations | Medium - OOM exceptions | Low | Batch bulk operations (max 1000 documents); use streaming where possible; document memory requirements |
| DbContext lifecycle management issues in DI | Low - Transient bugs | Low | Use scoped lifetime; document DI registration; add integration tests verifying proper disposal |
| Test database provisioning fails in CI environment | Low - CI failures | Medium | Use Testcontainers with proper resource limits; add retry logic; document CI requirements; provide fallback to local databases |

## Notes *(optional)*

- This feature implements Phase 1.1 through 1.3 of the Step 1 migration plan documented in `docs/step1_database_models_plan.md`
- The three-project structure (DeepWiki.Data, DeepWiki.Data.SqlServer, DeepWiki.Data.Postgres) ensures clean separation of concerns and prevents accidental coupling to specific database implementations
- Embedding dimension is fixed at 1536 to match OpenAI's text-embedding-ada-002 model; changing this requires schema migrations for both databases
- SQL Server 2025's native vector type is significantly more efficient than storing embeddings as binary or JSON, providing 40-60% storage savings and 3-5x faster similarity search
- PostgreSQL pgvector extension supports multiple indexing strategies (IVFFlat, HNSW); HNSW is recommended for better recall but requires PostgreSQL 16+ with pgvector 0.5.0+
- Cosine similarity is preferred over Euclidean distance for normalized embeddings typical of modern embedding models
- The IVectorStore abstraction allows future addition of other vector databases (Qdrant, Milvus, Pinecone) without changing consumer code
- Integration tests use Testcontainers to provision temporary database instances, ensuring tests are isolated and repeatable without requiring manual database setup
- MetadataJson provides extensibility for additional properties without schema changes, following the constitution's storage policy of preferring structured columns for queryable data and JSON for metadata
