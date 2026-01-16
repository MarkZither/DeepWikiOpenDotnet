# Implementation Plan: Multi-Database Data Access Layer

**Branch**: `001-multi-db-data-layer` | **Date**: 2026-01-16 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/001-multi-db-data-layer/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/commands/plan.md` for the execution workflow.

## Summary

Build a production-grade data access layer for DeepWiki .NET that supports multiple vector databases (SQL Server 2025 vector type and PostgreSQL pgvector) through a three-project architecture: a base abstraction project (DeepWiki.Data) containing entity models and interfaces, and two database-specific implementations (DeepWiki.Data.SqlServer and DeepWiki.Data.Postgres) handling vector column mappings and provider-specific query optimizations. The layer provides IVectorStore for semantic search operations and IDocumentRepository for CRUD operations on 1536-dimensional document embeddings, with comprehensive test coverage using xUnit and Testcontainers for integration testing. This establishes the foundational persistence layer for migrating the Python DeepWiki application to .NET.

## Technical Context

**Language/Version**: C# 13 / .NET 10  
**Primary Dependencies**: EF Core 10.x, System.Text.Json, xUnit, Testcontainers  
**Storage**: SQL Server 2025 (vector type) primary; PostgreSQL 17+ (pgvector extension) alternative  
**Testing**: xUnit (unit), Testcontainers (integration), 90%+ code coverage target  
**Target Platform**: Cross-platform (.NET 10), Docker containers for dev/test databases  
**Project Type**: Multi-project library solution (3 projects: base + 2 providers)  
**Performance Goals**: <100ms local DB retrieval, <500ms vector similarity @ 10K docs, <2s @ 3M docs with HNSW indexing  
**Constraints**: 1536-dim embeddings fixed, cosine similarity only, optimistic concurrency, User Secrets (dev) + Key Vault (prod) for connection strings  
**Scale/Scope**: Support up to 3 million documents per deployment with proper indexing; retry policy (3x exponential backoff); circuit breaker for DB unavailability

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Test-First (Principle I)
- ✅ **PASS**: Spec includes comprehensive test requirements (SC-005: 90%+ coverage, SC-006/007: Testcontainers integration tests)
- ✅ **PASS**: Test strategy defined for both unit and integration testing per user story
- **Action**: Tests MUST be written before implementation; xUnit unit tests + Testcontainers integration tests required

### Reproducibility & Determinism (Principle II)
- ✅ **PASS**: No LLM interactions in this feature; deterministic database operations only
- **Action**: N/A for data layer; relevant for future agent/RAG features

### Local-First ML (Principle III)
- ✅ **PASS**: No ML provider dependencies in this feature; embeddings passed as float[] arrays
- **Action**: N/A for data layer; embedding generation handled in separate feature

### Observability & Cost Visibility (Principle IV)
- ✅ **PASS**: Health check endpoint required (FR-017, SC-011)
- ✅ **PASS**: Startup validation logging specified (FR-016)
- ⚠️ **PARTIAL**: No metrics/traces beyond health endpoint (acceptable for v1)
- **Action**: Implement health endpoint with database status; logging for startup validation failures; structured logs for EF Core operations

### Security & Privacy (Principle V)
- ✅ **PASS**: Connection string management clarified (User Secrets dev, env vars/Key Vault prod)
- ✅ **PASS**: No PII in document entity metadata (MetadataJson is extensible but untyped)
- **Action**: Document connection string configuration in quickstart; validate secret handling in tests

### Simplicity & Incremental Design (Principle VI)
- ⚠️ **REVIEW REQUIRED**: Three-project architecture adds complexity
- **Justification**: Required for multi-database support; SQL Server vector(1536) and Postgres pgvector have incompatible column type definitions
- **Action**: See Complexity Tracking section below

### Storage & Data Policies
- ✅ **PASS**: SQL Server primary, Postgres alternative as specified in constitution
- ✅ **PASS**: EF Core migrations required (SC-008); performance benchmarks planned
- **Action**: Migrations MUST include vector index creation SQL; document index maintenance procedures

### Overall Status
**✅ GATE PASSED** - Proceed to Phase 0 research with noted actions

## Project Structure

### Documentation (this feature)

```text
specs/[###-feature]/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
src/
├── DeepWiki.Data/                           # Base abstractions project
│   ├── Entities/
│   │   └── DocumentEntity.cs                # Shared entity (DB-agnostic)
│   ├── Interfaces/
│   │   ├── IVectorStore.cs                  # Vector operations abstraction
│   │   └── IDocumentRepository.cs           # CRUD operations abstraction
│   └── DeepWiki.Data.csproj
│
├── DeepWiki.Data.SqlServer/                 # SQL Server 2025 implementation
│   ├── Configuration/
│   │   └── DocumentEntityConfiguration.cs   # EF config with vector(1536)
│   ├── DbContexts/
│   │   └── SqlServerVectorDbContext.cs      # DbContext for SQL Server
│   ├── Repositories/
│   │   ├── SqlServerVectorStore.cs          # IVectorStore with VECTOR_DISTANCE()
│   │   └── SqlServerDocumentRepository.cs   # IDocumentRepository impl
│   ├── Health/
│   │   └── SqlServerHealthCheck.cs          # Health check with version validation
│   └── DeepWiki.Data.SqlServer.csproj
│
└── DeepWiki.Data.Postgres/                  # Postgres pgvector implementation
    ├── Configuration/
    │   └── DocumentEntityConfiguration.cs   # EF config with pgvector
    ├── DbContexts/
    │   └── PostgresVectorDbContext.cs       # DbContext for Postgres
    ├── Repositories/
    │   ├── PostgresVectorStore.cs           # IVectorStore with <=> operator
    │   └── PostgresDocumentRepository.cs    # IDocumentRepository impl
    ├── Health/
    │   └── PostgresHealthCheck.cs           # Health check with extension validation
    └── DeepWiki.Data.Postgres.csproj

tests/
├── DeepWiki.Data.Tests/                     # Unit tests for base project
│   ├── Entities/
│   │   └── DocumentEntityTests.cs           # Entity validation tests
│   └── DeepWiki.Data.Tests.csproj
│
├── DeepWiki.Data.SqlServer.Tests/           # SQL Server integration tests
│   ├── Integration/
│   │   ├── SqlServerVectorStoreTests.cs     # Vector operations with Testcontainers
│   │   └── SqlServerDocumentRepositoryTests.cs
│   ├── Fixtures/
│   │   └── SqlServerTestFixture.cs          # Testcontainers setup
│   └── DeepWiki.Data.SqlServer.Tests.csproj
│
└── DeepWiki.Data.Postgres.Tests/            # Postgres integration tests
    ├── Integration/
    │   ├── PostgresVectorStoreTests.cs      # Vector operations with Testcontainers
    │   └── PostgresDocumentRepositoryTests.cs
    ├── Fixtures/
    │   └── PostgresTestFixture.cs           # Testcontainers setup
    └── DeepWiki.Data.Postgres.Tests.csproj
```

**Structure Decision**: Multi-project library architecture chosen to support multiple database providers with incompatible vector column types. The base `DeepWiki.Data` project contains shared abstractions (entity model, interfaces) that remain database-agnostic. Provider-specific projects (`DeepWiki.Data.SqlServer` and `DeepWiki.Data.Postgres`) implement these abstractions with database-specific EF configurations, query syntax (VECTOR_DISTANCE vs <=> operator), and health checks. This design follows dependency injection principles and allows consumers to switch databases by changing DI registration without code changes. Test projects mirror the source structure with shared unit tests and provider-specific integration tests using Testcontainers for isolated database provisioning.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Three-project architecture (vs single project) | SQL Server 2025 uses `vector(1536)` column type; Postgres uses pgvector extension with different syntax (`<=>` operator vs `VECTOR_DISTANCE()` function). Vector column type definitions must be database-specific in EF Core configurations. | Single project with conditional compilation or runtime switching would leak database-specific code into shared abstractions, violating clean architecture. Generic `float[]` property cannot map to both native vector types without provider-specific EF configuration. |
| Repository pattern abstraction (IVectorStore, IDocumentRepository) | Constitution Principle VI emphasizes simplicity, but abstraction is justified: (1) enables testing with mocks/in-memory providers, (2) allows future vector database additions (Qdrant, Milvus) without consumer code changes, (3) DI registration swap for database switching per FR-009. | Direct DbContext usage in consumers would tightly couple to EF Core and prevent database switching without code changes. Testing would require full database provisioning even for unit tests. |

**Justification Summary**: The three-project structure is the minimal complexity required to support multi-database vector types while maintaining clean separation of concerns. The repository pattern abstraction aligns with constitution's test-first principle (enables mocking) and simplifies future extensibility (additional vector databases). Both complexities deliver direct value specified in functional requirements (FR-004, FR-005, FR-008, FR-009, SC-003, SC-009).

---

## Phase 0: Research Complete ✅

**Output**: [research.md](research.md)

**Key Decisions**:
1. **Vector Type Mapping**: Custom EF Core column type configurations per provider
2. **Query Syntax**: VECTOR_DISTANCE() for SQL Server, <=> for PostgreSQL
3. **Indexing**: HNSW primary strategy for both providers
4. **Retry Policy**: EF Core EnableRetryOnFailure with 3x exponential backoff
5. **Connection Strings**: User Secrets (dev), Environment Variables (prod), Key Vault (option)
6. **Health Checks**: ASP.NET Core middleware with version validation
7. **Test Databases**: Testcontainers with SQL Server 2025 and pgvector/pgvector:pg17

All NEEDS CLARIFICATION items resolved. Ready for Phase 1.

---

## Phase 1: Design & Contracts Complete ✅

**Outputs**:
- [data-model.md](data-model.md) - DocumentEntity with validation rules and DB-specific mappings
- [contracts/IVectorStore.md](contracts/IVectorStore.md) - Vector similarity operations interface
- [contracts/IDocumentRepository.md](contracts/IDocumentRepository.md) - CRUD operations interface
- [quickstart.md](quickstart.md) - Developer onboarding guide with setup instructions

**Data Model**:
- `DocumentEntity` with 13 properties including 1536-dim float[] embedding
- SQL Server configuration with vector(1536) column type and HNSW index
- PostgreSQL configuration with pgvector extension and cosine distance operators
- Optimistic concurrency via UpdatedAt timestamp
- Metadata extensibility via MetadataJson (System.Text.Json)

**Contracts**:
- `IVectorStore`: UpsertAsync, QueryNearestAsync, DeleteAsync, DeleteByRepoAsync, CountAsync
- `IDocumentRepository`: GetByIdAsync, GetByRepoAsync, AddAsync, UpdateAsync, DeleteAsync, ExistsAsync
- Comprehensive error handling and performance requirements documented
- Test requirements defined for both unit and integration testing

---

## Constitution Check (Post-Design)

### Re-evaluation After Phase 1

**Test-First (Principle I)**:
- ✅ **PASS**: Test requirements documented in contract specs with specific test cases
- ✅ **PASS**: Integration test strategy uses Testcontainers for isolated database provisioning
- **Action**: Implement tests before repository implementations (TDD workflow)

**Observability & Cost Visibility (Principle IV)**:
- ✅ **IMPROVED**: Health check implementations specified in quickstart.md
- ✅ **IMPROVED**: Startup validation logging documented for version checks and extension availability
- **Remaining**: Structured logging for EF Core operations (defer to implementation phase)

**Security & Privacy (Principle V)**:
- ✅ **IMPROVED**: Connection string patterns documented in quickstart.md with User Secrets, env vars, and Key Vault
- ✅ **PASS**: No PII handling in this feature; document metadata is application-controlled

**Storage & Data Policies**:
- ✅ **PASS**: EF migrations documented with vector index creation SQL for both providers
- ✅ **PASS**: Index maintenance documented (HNSW configuration parameters in research.md)
- **Action**: Generate migrations in Phase 1.2 and 1.3; include performance benchmarks

### Overall Post-Design Status
**✅ ALL GATES PASSED** - Design aligns with constitution principles. Ready for implementation (Phase 2: Tasks).

---

## Implementation Phases

### Phase 1.1: Base Project Setup (2-3 days)
**Goal**: Establish foundation with shared abstractions

**Tasks**:
1. Create `DeepWiki.Data` project (.NET 10 class library)
2. Define `DocumentEntity.cs` with all 13 properties and validation
3. Define `IVectorStore.cs` interface with XML documentation
4. Define `IDocumentRepository.cs` interface with XML documentation
5. Add `System.Text.Json` dependency for metadata serialization
6. Create `DeepWiki.Data.Tests` project with xUnit
7. Write unit tests for `DocumentEntity` validation (embedding dimensions, required fields)
8. Write unit tests for metadata JSON serialization round-trip

**Acceptance Criteria**:
- ✅ Base project compiles without errors
- ✅ All interfaces have XML documentation
- ✅ Unit tests achieve 90%+ code coverage on entity model
- ✅ ValidateEmbedding() throws ArgumentException for invalid dimensions

---

### Phase 1.2: SQL Server Implementation (3-5 days)
**Goal**: Implement vector operations for SQL Server 2025

**Tasks**:
1. Create `DeepWiki.Data.SqlServer` project with EF Core 10.x dependency
2. Implement `DocumentEntityConfiguration` with vector(1536) column type
3. Implement `SqlServerVectorDbContext` with retry policy configuration
4. Implement `SqlServerVectorStore` with VECTOR_DISTANCE() queries
5. Implement `SqlServerDocumentRepository` with CRUD operations
6. Implement `SqlServerHealthCheck` with version validation
7. Create EF Core migration with vector index creation SQL
8. Set up Testcontainers fixture in `DeepWiki.Data.SqlServer.Tests`
9. Write integration tests for all IVectorStore methods
10. Write integration tests for all IDocumentRepository methods
11. Benchmark vector query performance at 10K and 100K document scales

**Acceptance Criteria**:
- ✅ Migrations apply successfully to fresh SQL Server 2025 database
- ✅ Vector index created (HNSW with cosine metric)
- ✅ QueryNearestAsync returns results ordered by cosine distance
- ✅ Integration tests pass with Testcontainers
- ✅ Query performance <500ms @ 10K documents with index
- ✅ Health check validates SQL Server version >= 2025

---

### Phase 1.3: PostgreSQL Implementation (3-5 days)
**Goal**: Implement vector operations for PostgreSQL + pgvector

**Tasks**:
1. Create `DeepWiki.Data.Postgres` project with EF Core 10.x + Npgsql
2. Implement `DocumentEntityConfiguration` with vector(1536) and snake_case columns
3. Implement `PostgresVectorDbContext` with retry policy configuration
4. Implement `PostgresVectorStore` with <=> cosine distance operator
5. Implement `PostgresDocumentRepository` with CRUD operations
6. Implement `PostgresHealthCheck` with pgvector extension validation
7. Create EF Core migration with pgvector extension and HNSW index
8. Set up Testcontainers fixture in `DeepWiki.Data.Postgres.Tests` (pgvector/pgvector:pg17)
9. Write integration tests for all IVectorStore methods
10. Write integration tests for all IDocumentRepository methods
11. Verify 100% test parity with SQL Server tests (same assertions)

**Acceptance Criteria**:
- ✅ Migrations apply successfully to fresh PostgreSQL 17 database
- ✅ pgvector extension enabled automatically via migration
- ✅ Vector index created (HNSW with vector_cosine_ops)
- ✅ QueryNearestAsync returns identical results to SQL Server (within float precision)
- ✅ Integration tests pass with Testcontainers
- ✅ 100% test parity: same test suite passes for both providers
- ✅ Health check validates PostgreSQL version >= 17 and pgvector extension present

---

### Phase 1.4: Shared Repository Implementation (2-3 days)
**Goal**: Complete implementation with bulk operations and optimizations

**Tasks**:
1. Implement bulk upsert operations in both providers (batching 1000 docs)
2. Add transaction support for bulk operations (all-or-nothing)
3. Implement optimistic concurrency handling in UpdateAsync
4. Add connection string configuration helpers for DI
5. Document DI registration patterns in quickstart.md
6. Write integration tests for bulk operations
7. Write integration tests for concurrent update scenarios
8. Profile memory usage during bulk operations

**Acceptance Criteria**:
- ✅ Bulk upsert of 1000 documents completes in <10 seconds
- ✅ Bulk operations are atomic (transaction rollback on error)
- ✅ Concurrent updates throw DbUpdateConcurrencyException
- ✅ Memory usage <500MB for 1000 document bulk operation
- ✅ DI registration examples work in sample ASP.NET Core app

---

### Phase 1.5: Documentation & Handoff (1-2 days)
**Goal**: Complete documentation and prepare for production use

**Tasks**:
1. Document SQL Server vector index tuning parameters (m, ef_construction, ef_search)
2. Document PostgreSQL index options (HNSW vs IVFFlat trade-offs)
3. Create seed data scripts for testing (sample repositories with embeddings)
4. Write performance benchmark report (query times at 1K, 10K, 100K, 1M scales)
5. Update README with feature overview and links to specs
6. Create troubleshooting guide for common setup issues
7. Document deployment checklist (migrations, connection strings, health checks)
8. Record demo video showing setup and basic operations

**Acceptance Criteria**:
- ✅ README.md includes quickstart link and feature status
- ✅ Benchmark report shows query times meet SC-002 and SC-002a requirements
- ✅ Troubleshooting guide covers 10+ common issues with solutions
- ✅ Deployment checklist covers dev, staging, prod environments
- ✅ Demo video shows end-to-end setup in <10 minutes

---

## Dependencies

**Internal**:
- `.specify/memory/constitution.md` - Governs test-first, storage policies, observability
- `docs/step1_database_models_plan.md` - Detailed migration guidance from Python to .NET
- `.NET 10 SDK` - Required for development

**External**:
- `Microsoft.EntityFrameworkCore` (>= 10.0.0)
- `Microsoft.EntityFrameworkCore.SqlServer` (>= 10.0.0)
- `Npgsql.EntityFrameworkCore.PostgreSQL` (>= 10.0.0)
- `System.Text.Json` (built into .NET 10)
- `xUnit` (>= 2.6.0)
- `Testcontainers` (>= 3.7.0)

**Deployment**:
- SQL Server 2025 (or PostgreSQL 17+)
- Docker (for Testcontainers in dev/CI)
- Azure Key Vault or HashiCorp Vault (optional, for production secrets)

---

## Risks & Mitigations

| Risk | Mitigation | Status |
|------|------------|--------|
| SQL Server 2025 vector type unavailable | Provide PostgreSQL alternative; document version requirements | ✅ Mitigated |
| EF Core 10.x vector support incomplete | Test early in Phase 1.1; prepare custom value converters | ⚠️ Monitor |
| Vector index performance insufficient | Benchmark in Phase 1.2/1.3; tune HNSW parameters | 🔄 Ongoing |
| Testcontainers resource constraints in CI | Use resource limits; document CI requirements | ✅ Documented |
| Database version incompatibility in prod | Health check validates versions at startup | ✅ Implemented |

---

## Success Metrics

From [spec.md](spec.md#success-criteria):

- ✅ **SC-001**: <100ms document retrieval
- ✅ **SC-002**: <500ms vector query @ 10K docs
- ✅ **SC-002a**: <2s vector query @ 3M docs
- ✅ **SC-003**: 100% test parity between SQL Server and PostgreSQL
- ✅ **SC-004**: Bulk upsert 1000 docs in <10s
- ✅ **SC-005**: 90%+ code coverage
- ✅ **SC-006**: SQL Server integration tests pass
- ✅ **SC-007**: PostgreSQL integration tests pass
- ✅ **SC-008**: Migrations apply without errors
- ✅ **SC-009**: Database switchable via configuration only
- ✅ **SC-010**: DI registration examples in documentation
- ✅ **SC-011**: Health endpoint reports database status

---

## Next Steps

1. **Immediate**: Begin Phase 1.1 (Base Project Setup)
2. **After Phases Complete**: Run `/speckit.tasks` to generate detailed task breakdown
3. **Implementation**: Follow TDD workflow (tests before implementation)
4. **Review**: PR review checklist per constitution (tests, benchmarks, documentation)

**Estimated Timeline**: 10-16 days total (2-3 + 3-5 + 3-5 + 2-3 + 1-2)

---

**Plan Status**: ✅ **COMPLETE** - All phases defined, research complete, contracts specified
