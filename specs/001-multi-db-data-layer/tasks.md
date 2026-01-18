# Tasks: Multi-Database Data Access Layer

**Feature**: Multi-Database Data Access Layer  
**Specification**: [spec.md](spec.md)  
**Implementation Plan**: [plan.md](plan.md)  
**Branch**: `001-multi-db-data-layer`  
**Status**: Ready for Implementation

---

## Summary

This document breaks down the implementation plan into discrete, executable tasks organized by user story and implementation phase. Each task follows the format:

```
- [ ] [TaskID] [P] [Story] Description with exact file path
```

**Total Tasks**: 52  
**Phases**: 5 (Setup → SQL Server → PostgreSQL → Bulk Ops → Documentation)  
**Parallel Opportunities**: 12+ tasks can run in parallel within each phase  
**MVP Scope**: Phase 1.1 + Phase 1.2 (Store documents, Vector search on SQL Server)

---

## Phase 1.1: Base Project Setup (2-3 days)
**Goal**: Establish foundation with shared abstractions and unit tests  
**Independent Test Criteria**: 
- ✅ DocumentEntity validates 1536-dimensional embeddings
- ✅ 90%+ code coverage on entity model
- ✅ JSON metadata serialization round-trip works
- ✅ All base interfaces properly documented

### Setup Tasks

- [x] T001 Create .NET 10 project structure and solution file
- [x] T002 [P] Create DeepWiki.Data class library project in src/DeepWiki.Data
- [x] T003 [P] Create DeepWiki.Data.Tests xUnit project in tests/DeepWiki.Data.Tests
- [x] T004 Add System.Text.Json and xUnit dependencies to projects
- [x] T005 Create directory structure: Entities/, Interfaces/, obj/, bin/

### Entity Implementation (US1, US2)

- [x] T006 [P] [US1] Write unit tests for DocumentEntity embedding validation in tests/DeepWiki.Data.Tests/Entities/DocumentEntityTests.cs
- [x] T007 [P] [US1] Write unit tests for required properties (Id, RepoUrl, FilePath, Text) initialization
- [x] T008 [P] [US1] Write unit tests for optional properties and defaults (IsCode, IsImplementation, TokenCount)
- [x] T009 [P] [US1] Write unit tests for JSON metadata serialization round-trip in tests/DeepWiki.Data.Tests/Entities/DocumentEntityTests.cs
- [x] T010 [P] [US1] Write unit tests for edge cases (max lengths, null handling, timestamps)
- [x] T011 [US1] Implement DocumentEntity.cs in src/DeepWiki.Data/Entities/ with 13 properties and ValidateEmbedding() method
- [x] T012 [US1] Verify all unit tests pass and achieve ≥90% code coverage

### Interface Definitions (US1, US2)

- [x] T013 [P] [US2] Implement IVectorStore interface in src/DeepWiki.Data/Interfaces/IVectorStore.cs
- [x] T013a Includes methods: UpsertAsync, QueryNearestAsync, DeleteAsync, DeleteByRepoAsync, CountAsync
- [x] T013b Add comprehensive XML documentation with performance remarks
- [x] T014 [P] [US2] Implement IDocumentRepository interface in src/DeepWiki.Data/Interfaces/IDocumentRepository.cs
- [x] T014a Includes methods: GetByIdAsync, GetByRepoAsync, AddAsync, UpdateAsync, DeleteAsync, ExistsAsync
- [x] T014b Add comprehensive XML documentation with pagination and concurrency remarks

### Phase 1.1 Completion

- [x] T015 Run full build: `dotnet build`
- [x] T016 Run unit test suite: `dotnet test tests/DeepWiki.Data.Tests/`
- [x] T017 Verify code coverage ≥90%: `dotnet test --collect:"XPlat Code Coverage"`
- [x] T018 Document Phase 1.1 completion in PHASE_1_1_COMPLETION_REPORT.md
- [x] T019 Commit changes: `git add . && git commit -m "Phase 1.1: Base project setup with entities and interfaces"`

---

## Phase 1.2: SQL Server Implementation (3-5 days)
**Goal**: Implement vector operations for SQL Server 2025  
**Dependencies**: Phase 1.1 complete  
**Independent Test Criteria**:
- ✅ Migrations apply to SQL Server 2025
- ✅ Vector queries return sorted results by distance
- ✅ HNSW index created successfully
- ✅ Integration tests pass with Testcontainers
- ✅ Query performance <500ms @ 10K documents

### Project & Configuration

- [x] T020 [P] Create DeepWiki.Data.SqlServer project in src/DeepWiki.Data.SqlServer
- [x] T021 [P] Add EF Core 10.x and Microsoft.EntityFrameworkCore.SqlServer dependencies
- [x] T022 [P] Add Testcontainers[MsSql] dependency to tests/DeepWiki.Data.SqlServer.Tests
- [x] T023 Create directory structure: Configuration/, DbContexts/, Repositories/, Health/, Tests/

### Entity Configuration (US1, US2, US3)

- [x] T024 [P] [US3] Write integration test for DocumentEntity with SQL Server vector(1536) column in tests/DeepWiki.Data.SqlServer.Tests/Integration/SqlServerVectorStoreTests.cs
- [x] T025 [P] [US3] Write integration test for vector index HNSW creation with VECTOR_DISTANCE() operator
- [x] T026 [P] [US3] Implement DocumentEntityConfiguration.cs in src/DeepWiki.Data.SqlServer/Configuration/
- [x] T026a Configure vector(1536) column type mapping
- [x] T026b Configure HNSW index with m=16, ef_construction=200
- [x] T027 [US3] Implement SqlServerVectorDbContext.cs with DbSet<DocumentEntity> and retry policy

### Repository Implementation (US1, US2)

- [x] T028 [P] [US1] Write integration tests for SqlServerDocumentRepository.AddAsync() in tests/DeepWiki.Data.SqlServer.Tests/Integration/SqlServerDocumentRepositoryTests.cs
- [x] T029 [P] [US1] Write integration tests for GetByIdAsync() and GetByRepoAsync() with pagination
- [x] T030 [P] [US2] Write integration tests for SqlServerVectorStore.QueryNearestAsync() with various k values
- [x] T031 [P] [US2] Write integration tests for VECTOR_DISTANCE() query returning sorted results
- [x] T032 [P] [US1] Implement SqlServerDocumentRepository.cs with IDocumentRepository methods
- [x] T032a AddAsync, UpdateAsync, DeleteAsync, GetByIdAsync, GetByRepoAsync, ExistsAsync
- [x] T033 [P] [US2] Implement SqlServerVectorStore.cs with IVectorStore methods
- [x] T033a UpsertAsync, QueryNearestAsync, DeleteAsync, DeleteByRepoAsync, CountAsync
- [x] T033b Use VECTOR_DISTANCE() for cosine similarity queries
- [x] T034 [US3] Implement SqlServerHealthCheck.cs with version validation (SQL Server ≥2025)

### Migrations & Indexes (US1, US2)

- [x] T035 [P] Create EF Core migration for DocumentEntity table creation
- [x] T036 [P] Add HNSW vector index SQL to migration script
- [x] T037 Write seed data script for test documents (sample repositories with embeddings)
- [x] T038 Test migration applies without errors on fresh SQL Server 2025 database

### Integration Testing (US1, US2)

- [x] T039 [P] Create SqlServerTestFixture.cs in tests/DeepWiki.Data.SqlServer.Tests/Fixtures/ using Testcontainers
- [x] T040 [P] Write integration tests for bulk operations (100 documents in transaction)
- [x] T041 [P] Write integration tests for concurrent update scenarios
- [ ] T042 Write performance benchmark for vector queries at 10K document scale
- [x] T043 Run full SQL Server test suite: `dotnet test tests/DeepWiki.Data.SqlServer.Tests/`
- [ ] T044 Verify query performance <500ms @ 10K documents
- [x] T045 Verify all integration tests pass with Testcontainers

### Phase 1.2 Completion

- [x] T046 Document SQL Server implementation in docs/sql-server-setup.md
- [x] T047 Commit changes: `git add . && git commit -m "Phase 1.2: SQL Server 2025 implementation with HNSW indexing"`

---

## Phase 1.3: PostgreSQL Implementation (3-5 days)
**Goal**: Implement vector operations for PostgreSQL + pgvector  
**Dependencies**: Phase 1.1, Phase 1.2 complete (for test parity verification)  
**Independent Test Criteria**:
- ✅ Migrations apply to PostgreSQL 17+ with pgvector
- ✅ Vector queries return identical results to SQL Server
- ✅ HNSW index created successfully
- ✅ Integration tests pass with Testcontainers
- ✅ 100% test parity with SQL Server

### Project & Configuration

- [x] T048 [P] Create DeepWiki.Data.Postgres project in src/DeepWiki.Data.Postgres
- [x] T049 [P] Add EF Core 10.x and Npgsql.EntityFrameworkCore.PostgreSQL dependencies
- [x] T050 [P] Add Testcontainers[PostgreSql] dependency to tests/DeepWiki.Data.Postgres.Tests
- [x] T051 Create directory structure: Configuration/, DbContexts/, Repositories/, Health/, Tests/

### Entity Configuration (US1, US2, US3)

- [x] T052 [P] [US3] Write integration test for DocumentEntity with pgvector column in tests/DeepWiki.Data.Postgres.Tests/Integration/PostgresVectorStoreTests.cs
- [x] T053 [P] [US3] Write integration test for vector index HNSW creation with <=> cosine distance operator
- [x] T054 [P] [US3] Implement DocumentEntityConfiguration.cs in src/DeepWiki.Data.Postgres/Configuration/
- [x] T054a Configure pgvector column type with snake_case naming
- [x] T054b Configure HNSW index with m=16, ef_construction=200
- [x] T055 [US3] Implement PostgresVectorDbContext.cs with pgvector extension creation in migration

### Repository Implementation (US1, US2)

- [x] T056 [P] [US1] Write integration tests for PostgresDocumentRepository.AddAsync() in tests/DeepWiki.Data.Postgres.Tests/Integration/PostgresDocumentRepositoryTests.cs
- [x] T057 [P] [US1] Write integration tests for GetByIdAsync() and GetByRepoAsync() with pagination
- [x] T058 [P] [US2] Write integration tests for PostgresVectorStore.QueryNearestAsync() with various k values
- [x] T059 [P] [US2] Write integration tests for <=> operator returning sorted results matching SQL Server
- [x] T060 [P] [US1] Implement PostgresDocumentRepository.cs with IDocumentRepository methods
- [x] T061 [P] [US2] Implement PostgresVectorStore.cs with IVectorStore methods
- [x] T061a Use <=> operator for cosine similarity queries (identical to SQL Server results)
- [x] T062 [US3] Implement PostgresHealthCheck.cs with pgvector extension validation

### Migrations & Extensions (US1, US2)

- [x] T063 [P] Create EF Core migration for DocumentEntity table with pgvector extension creation
- [x] T064 [P] Add pgvector extension installation SQL to migration
- [x] T065 [P] Add HNSW vector index SQL to migration script
- [x] T066 Write seed data script for test documents (replicate SQL Server seeds)
- [x] T067 Test migration applies without errors on fresh PostgreSQL 17+ database with pgvector

### Integration Testing & Parity (US1, US2, US3)

- [x] T068 [P] Create PostgresTestFixture.cs in tests/DeepWiki.Data.Postgres.Tests/Fixtures/ using Testcontainers
- [x] T069 [P] Run identical test suite as SQL Server (copy tests and verify 100% parity)
- [x] T070 [P] Write cross-database parity test: store in SQL Server, compare results with PostgreSQL
- [x] T071 [P] Write performance benchmark for vector queries at 10K document scale
- [x] T072 Run full PostgreSQL test suite: `dotnet test tests/DeepWiki.Data.Postgres.Tests/`
- [x] T073 Verify query performance <500ms @ 10K documents (match SQL Server results)
- [x] T074 Verify all integration tests pass with Testcontainers
- [x] T075 Confirm 100% test parity: same tests pass for both SQL Server and PostgreSQL

### Phase 1.3 Completion

- [x] T076 Document PostgreSQL implementation in docs/postgres-setup.md
- [x] T077 Commit changes: `git add . && git commit -m "Phase 1.3: PostgreSQL pgvector implementation with test parity"`

---

## Phase 1.4: Bulk Operations & Optimization (2-3 days)
**Goal**: Complete implementation with bulk operations and performance optimization  
**Dependencies**: Phase 1.1, Phase 1.2, Phase 1.3 complete  
**Independent Test Criteria**:
- ✅ Bulk upsert 1000 documents in <10 seconds
- ✅ Bulk operations are atomic (all-or-nothing)
- ✅ Concurrent updates handled with optimistic concurrency
- ✅ Memory usage <500MB for bulk operations
- ✅ DI registration patterns documented and working

### Bulk Operations (US4)

- [ ] T078 [P] [US4] Write integration tests for BulkUpsertAsync with 100 documents (SQL Server)
- [ ] T079 [P] [US4] Write integration tests for BulkUpsertAsync with 100 documents (PostgreSQL)
- [ ] T080 [P] [US4] Write integration tests for transaction rollback on bulk operation failure
- [ ] T081 [P] [US4] Implement bulk upsert in SqlServerVectorStore with batching (1000 docs per batch)
- [ ] T082 [P] [US4] Implement bulk upsert in PostgresVectorStore with batching
- [ ] T083 [US4] Add transaction support to ensure all-or-nothing semantics

### Concurrency & Optimization (US1, US2)

- [ ] T084 [P] Write integration tests for optimistic concurrency conflicts in UpdateAsync
- [ ] T085 [P] Write integration tests for concurrent document updates (simulated race conditions)
- [ ] T086 [P] Verify optimistic concurrency throws DbUpdateConcurrencyException on conflict
- [ ] T087 [P] Profile memory usage during bulk operations (1000 documents)
- [ ] T088 Optimize batch sizes for memory efficiency if needed

### Configuration & DI (US3)

- [ ] T089 Write connection string configuration helper class for DI setup
- [ ] T090 Create sample DI registration code for ASP.NET Core in docs/di-registration.md
- [ ] T091 Write unit tests for DI registration patterns
- [ ] T092 Create sample console app demonstrating DI registration and usage
- [ ] T093 Test database switching via configuration only (no code changes)

### Phase 1.4 Completion

- [ ] T094 Document bulk operations in docs/bulk-operations.md
- [ ] T095 Document DI configuration in docs/dependency-injection.md
- [ ] T096 Commit changes: `git add . && git commit -m "Phase 1.4: Bulk operations, optimization, and DI configuration"`

---

## Phase 1.5: Documentation & Handoff (1-2 days)
**Goal**: Complete documentation and prepare for production use  
**Dependencies**: All previous phases complete  

### Performance & Benchmarking

- [ ] T097 Write performance benchmark report with query times at 1K, 10K, 100K, 1M document scales
- [ ] T098 Document SQL Server HNSW tuning parameters (m, ef_construction, ef_search)
- [ ] T099 Document PostgreSQL index options (HNSW vs IVFFlat trade-offs)
- [ ] T100 Create seed data scripts for testing different data sizes

### Documentation

- [ ] T101 Update README.md with feature overview and quickstart link
- [ ] T102 Create troubleshooting guide (docs/troubleshooting.md) covering 10+ common issues
- [ ] T103 Document deployment checklist for dev, staging, production
- [ ] T104 Create health check endpoint documentation
- [ ] T105 Document connection string configuration for all environments

### Quality & Release

- [ ] T106 Review all code for constitution compliance (test-first, observability, security)
- [ ] T107 Verify 90%+ code coverage across all test suites
- [ ] T108 Ensure all integration tests pass on both SQL Server and PostgreSQL
- [ ] T109 Final build verification: `dotnet build && dotnet test`
- [ ] T110 Create release notes documenting all features and breaking changes

### Phase 1.5 Completion

- [ ] T111 Tag release: `git tag -a v1.0.0 -m "Multi-database data access layer"`
- [ ] T112 Commit final documentation: `git add . && git commit -m "Phase 1.5: Complete documentation and release"`

---

## Dependencies Graph

```
Phase 1.1 (Setup, Entities, Interfaces)
    ↓
Phase 1.2 (SQL Server Implementation)
    ↓ (can run in parallel with 1.3 after 1.1)
Phase 1.3 (PostgreSQL Implementation)
    ↓ (sequential - verify parity)
Phase 1.4 (Bulk Operations & Optimization)
    ↓
Phase 1.5 (Documentation & Release)
```

---

## Parallel Execution Example

**MVP (Minimum Viable Product) - Days 1-3 (Phase 1.1 + 1.2)**:
- Day 1: Phase 1.1 tasks (T001-T019) in sequence - 1 person
- Day 2-3: Phase 1.2 tasks with parallelism:
  - Person A: T020-T027 (SQL Server setup & config) in parallel with
  - Person B: T028-T038 (Repository & migrations) in parallel with
  - Person C: T039-T045 (Testing & benchmarking)
  - Result: Store & query documents with SQL Server ✅

**Full Implementation - Days 4-7 (Phase 1.3)**:
- Phase 1.3 mirrors Phase 1.2 with similar parallelism structure

**Enhancement & Release - Days 8-9 (Phase 1.4 + 1.5)**:
- Bulk operations and documentation can run mostly in parallel
- Final testing and release

---

## Task Checklist Formats Reference

**Single Task**: `- [ ] T001 Create project structure`  
**Parallelizable**: `- [ ] T005 [P] Add dependencies`  
**By Story**: `- [ ] T012 [US1] Implement DocumentEntity`  
**All Combined**: `- [ ] T026 [P] [US3] Implement DocumentEntityConfiguration`  

---

## Acceptance Criteria Verification

Before marking each phase complete:

### Phase 1.1 Checklist
- ✅ DeepWiki.Data project compiles
- ✅ DocumentEntity has 13 properties
- ✅ IVectorStore defined with 5 methods
- ✅ IDocumentRepository defined with 6 methods
- ✅ 31+ unit tests all passing
- ✅ 90%+ code coverage
- ✅ ValidateEmbedding() validates 1536 dimensions

### Phase 1.2 Checklist
- ✅ SQL Server migrations apply
- ✅ HNSW index created
- ✅ Vector queries sorted by distance
- ✅ Integration tests pass with Testcontainers
- ✅ Performance <500ms @ 10K docs
- ✅ Health check validates version

### Phase 1.3 Checklist
- ✅ PostgreSQL migrations apply with pgvector
- ✅ HNSW index created
- ✅ Test parity verified (100% same tests pass)
- ✅ Vector query results match SQL Server (float precision)
- ✅ Integration tests pass with Testcontainers
- ✅ Health check validates version + extension

### Phase 1.4 Checklist
- ✅ Bulk upsert 1000 docs <10s
- ✅ Transactions atomic (rollback on error)
- ✅ Concurrency conflicts handled
- ✅ Memory <500MB for bulk ops
- ✅ DI registration working

### Phase 1.5 Checklist
- ✅ README updated
- ✅ Troubleshooting guide complete (10+ issues)
- ✅ Deployment checklist done
- ✅ Performance report generated
- ✅ Release notes created

---

**Generated**: 2026-01-16  
**Status**: Ready for Implementation  
**MVP Scope**: Phase 1.1 + 1.2 (~5-8 days)  
**Full Implementation**: All phases (~10-16 days)
