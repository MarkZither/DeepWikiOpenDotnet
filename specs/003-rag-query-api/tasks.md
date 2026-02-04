# Tasks: RAG Query API

**Input**: Design documents from `/specs/003-rag-query-api/`  
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/openapi.yaml ✅

---

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, etc.)
- All paths are absolute from repository root

---

## Phase 1: Setup (Project Initialization)

**Purpose**: Create project structure and install dependencies

- [X] T001 Add Microsoft.Extensions.Http.Polly package to src/deepwiki-open-dotnet.ApiService/deepwiki-open-dotnet.ApiService.csproj
- [X] T002 [P] Create src/deepwiki-open-dotnet.ApiService/Configuration/ directory structure
- [X] T003 [P] Create src/deepwiki-open-dotnet.ApiService/Models/ directory structure
- [X] T004 [P] Create src/deepwiki-open-dotnet.ApiService/Controllers/ directory structure
- [X] T005 [P] Create tests/deepwiki-open-dotnet.Tests/Api/ directory structure

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before ANY user story

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

### Configuration & Options

- [X] T006 Create VectorStoreOptions class in src/deepwiki-open-dotnet.ApiService/Configuration/VectorStoreOptions.cs
- [X] T007 [P] Create SqlServerVectorStoreOptions class in src/deepwiki-open-dotnet.ApiService/Configuration/SqlServerVectorStoreOptions.cs
- [X] T008 [P] Create PostgresVectorStoreOptions class in src/deepwiki-open-dotnet.ApiService/Configuration/PostgresVectorStoreOptions.cs

### VectorStore Factory (Provider Selection)

- [X] T009 Create VectorStoreFactory class in src/DeepWiki.Rag.Core/VectorStore/VectorStoreFactory.cs
- [X] T010 Create PostgresVectorStoreAdapter in src/DeepWiki.Data.Postgres/VectorStore/PostgresVectorStoreAdapter.cs
- [X] T011 Modify src/DeepWiki.Data.Postgres/DependencyInjection/ServiceCollectionExtensions.cs to register PostgresVectorStoreAdapter as IVectorStore

### API Models (Shared DTOs)

- [X] T012 [P] Create ErrorResponse record in src/deepwiki-open-dotnet.ApiService/Models/ErrorResponse.cs
- [X] T013 [P] Create QueryRequest and QueryFilters records in src/deepwiki-open-dotnet.ApiService/Models/QueryRequest.cs
- [X] T014 [P] Create QueryResultItem record in src/deepwiki-open-dotnet.ApiService/Models/QueryResultItem.cs
- [X] T015 [P] Create IngestRequest and IngestDocument records in src/deepwiki-open-dotnet.ApiService/Models/IngestRequest.cs
- [X] T016 [P] Create IngestResponse and IngestError records in src/deepwiki-open-dotnet.ApiService/Models/IngestResponse.cs
- [X] T017 [P] Create DocumentListResponse and DocumentSummary records in src/deepwiki-open-dotnet.ApiService/Models/DocumentListResponse.cs

### Program.cs DI Updates

- [X] T018 Register VectorStoreOptions in src/deepwiki-open-dotnet.ApiService/Program.cs using Options pattern
- [X] T019 Replace NoOpVectorStore registration with VectorStoreFactory-based registration in src/deepwiki-open-dotnet.ApiService/Program.cs

### Test Infrastructure

- [X] T020 Create ApiTestFixture with WebApplicationFactory in tests/deepwiki-open-dotnet.Tests/Api/ApiTestFixture.cs
- [X] T021 Create VectorStoreFactoryTests in tests/DeepWiki.Rag.Core.Tests/VectorStore/VectorStoreFactoryTests.cs

**Checkpoint**: Foundation ready - VectorStoreFactory working, all DTOs defined, test infrastructure in place

---

## Phase 3: User Story 1 - Search Documents by Semantic Query (Priority: P1) 🎯 MVP

**Goal**: POST /api/query endpoint that accepts natural language query and returns semantically similar documents

**Independent Test**: Send POST to `/api/query` with `{"query": "test", "k": 5}` and receive ranked results array

### Implementation for User Story 1

- [X] T022 [US1] Create QueryController with POST /api/query endpoint in src/deepwiki-open-dotnet.ApiService/Controllers/QueryController.cs
- [X] T023 [US1] Implement query embedding via IEmbeddingService in QueryController
- [X] T024 [US1] Implement vector similarity search via IVectorStore.QueryAsync in QueryController
- [X] T025 [US1] Add Polly retry/circuit breaker policy for embedding service calls in QueryController
- [X] T026 [US1] Implement includeFullText parameter handling in QueryController
- [X] T027 [US1] Implement repoUrl and filePath filter passthrough in QueryController
- [X] T028 [US1] Add error handling returning ErrorResponse with {"detail": "..."} format in QueryController
- [X] T029 [US1] Create QueryControllerTests integration tests in tests/deepwiki-open-dotnet.Tests/Api/QueryControllerTests.cs

**Checkpoint**: User Story 1 complete - semantic search working end-to-end

---

## Phase 4: User Story 2 - Ingest Documents via API (Priority: P2)

**Goal**: POST /api/documents/ingest endpoint for batch document ingestion

**Independent Test**: POST documents to `/api/documents/ingest`, verify success response with ingested IDs

### Implementation for User Story 2

- [X] T030 [US2] Create DocumentsController skeleton in src/deepwiki-open-dotnet.ApiService/Controllers/DocumentsController.cs
- [X] T031 [US2] Implement POST /api/documents/ingest endpoint in DocumentsController
- [X] T032 [US2] Map IngestRequest to IngestionRequest for IDocumentIngestionService in DocumentsController
- [X] T033 [US2] Map IngestionResult to IngestResponse in DocumentsController
- [X] T034 [US2] Add request validation for IngestRequest (required fields, max document count) in DocumentsController
- [X] T035 [US2] Add Polly resilience for embedding calls during ingestion in DocumentsController
- [X] T036 [US2] Create DocumentsControllerIngestTests in tests/deepwiki-open-dotnet.Tests/Api/DocumentsControllerIngestTests.cs

**Checkpoint**: User Story 2 complete - can ingest documents and verify via US1 query

---

## Phase 5: User Story 3 - Retrieve Document by ID (Priority: P3)

**Goal**: GET /api/documents/{id} endpoint to retrieve single document

**Independent Test**: Ingest a document, note ID, GET `/api/documents/{id}` returns full document

### Implementation for User Story 3

- [X] T037 [US3] Implement GET /api/documents/{id} endpoint in src/deepwiki-open-dotnet.ApiService/Controllers/DocumentsController.cs
- [X] T038 [US3] Add IVectorStore method or query to retrieve document by ID (may need interface extension)  # Implemented via IDocumentRepository lookup (preferred abstraction)
- [X] T039 [US3] Return 404 ErrorResponse when document not found
- [X] T040 [US3] Create DocumentsControllerGetTests in tests/deepwiki-open-dotnet.Tests/Api/DocumentsControllerGetTests.cs

**Checkpoint**: User Story 3 complete - document retrieval working

---

## Phase 6: User Story 4 - Delete Document (Priority: P3)

**Goal**: DELETE /api/documents/{id} endpoint to remove document

**Independent Test**: Ingest document, DELETE it, verify 204 response and document no longer in query results

### Implementation for User Story 4

- [X] T041 [US4] Implement DELETE /api/documents/{id} endpoint in src/deepwiki-open-dotnet.ApiService/Controllers/DocumentsController.cs
- [X] T042 [US4] Call IVectorStore.DeleteAsync with document ID
- [X] T043 [US4] Return 204 No Content on success, 404 if not found
- [X] T044 [US4] Create DocumentsControllerDeleteTests in tests/deepwiki-open-dotnet.Tests/Api/DocumentsControllerDeleteTests.cs

**Checkpoint**: User Story 4 complete - document deletion working

---

## Phase 7: User Story 5 - List Documents with Pagination (Priority: P4)

**Goal**: GET /api/documents endpoint with pagination support

**Independent Test**: Ingest multiple documents, GET `/api/documents?page=1&pageSize=10` returns paginated list

### Implementation for User Story 5

- [X] T045 [US5] Add IDocumentRepository or extend IVectorStore for paginated listing capability
- [X] T046 [US5] Implement GET /api/documents endpoint in src/deepwiki-open-dotnet.ApiService/Controllers/DocumentsController.cs
- [X] T047 [US5] Implement pagination parameters (page, pageSize) with defaults
- [X] T048 [US5] Implement repoUrl filter parameter
- [X] T049 [US5] Map results to DocumentListResponse with DocumentSummary items
- [X] T050 [US5] Create DocumentsControllerListTests in tests/deepwiki-open-dotnet.Tests/Api/DocumentsControllerListTests.cs

**Checkpoint**: User Story 5 complete - document listing with pagination working

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Final improvements affecting all user stories

- [X] T051 [P] Add OpenAPI annotations to all endpoints in QueryController and DocumentsController
- [X] T052 [P] Update appsettings.json with VectorStore configuration section
- [X] T053 [P] Update appsettings.Development.json with sample VectorStore configuration
- [X] T054 Add endpoint registration in Program.cs (MapControllers or minimal API groups)
- [X] T055 [P] Validate all endpoints match contracts/openapi.yaml specification
- [X] T056 [P] Run quickstart.md validation - verify all curl examples work
- [X] T057 Final code review and cleanup

---

## Dependencies & Execution Order

### Phase Dependencies

```
Phase 1 (Setup) ─────────────────────────────────────┐
                                                      │
Phase 2 (Foundational) ◄──────────────────────────────┘
    │
    ├── T006-T008: Options classes (parallel)
    ├── T009-T011: VectorStoreFactory + PostgresAdapter (sequential)
    ├── T012-T017: API models (all parallel)
    ├── T018-T019: Program.cs DI (sequential)
    └── T020-T021: Test infrastructure (parallel)
    │
    ▼ BLOCKS all user stories
    │
    ├── Phase 3 (US1: Query) ──────► MVP Checkpoint
    │
    ├── Phase 4 (US2: Ingest) ─────► Can verify via US1
    │
    ├── Phase 5 (US3: Get) ────────► Can verify via US2
    │
    ├── Phase 6 (US4: Delete) ─────► Can verify deletion removes from US1 results
    │
    └── Phase 7 (US5: List) ───────► Can verify via US2 ingestion
    │
    ▼
Phase 8 (Polish)
```

### User Story Dependencies

| Story | Depends On | Can Start After |
|-------|------------|-----------------|
| US1 (Query) | Foundational complete | T021 |
| US2 (Ingest) | Foundational complete | T021 |
| US3 (Get) | Foundational complete | T021 |
| US4 (Delete) | Foundational complete | T021 |
| US5 (List) | Foundational complete | T021 |

**Note**: All user stories can technically start in parallel after Foundational phase. However, US1 (Query) is recommended first as it validates the core RAG pipeline.

### Parallel Opportunities per Phase

**Phase 1** (all parallel):
```
T002, T003, T004, T005
```

**Phase 2** (grouped parallel):
```
Group 1: T006, T007, T008 (Options - parallel)
Group 2: T012, T013, T014, T015, T016, T017 (Models - parallel)
Group 3: T020, T021 (Tests - parallel)
Sequential: T009 → T010 → T011 (Factory chain)
Sequential: T018 → T019 (Program.cs updates)
```

**Phase 3-7** (within each story):
- Implementation tasks within each story are mostly sequential
- Tests can be written first (TDD) or last

**Phase 8** (parallel):
```
T051, T052, T053, T055, T056
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (~5 min)
2. Complete Phase 2: Foundational (~2-3 hours)
3. Complete Phase 3: User Story 1 - Query (~1-2 hours)
4. **STOP and VALIDATE**: Test query endpoint with curl
5. Deploy/demo if ready - core RAG capability working

### Incremental Delivery

| Increment | Stories | Cumulative Value |
|-----------|---------|------------------|
| MVP | US1 | Semantic search working |
| +Ingest | US1 + US2 | Complete RAG workflow |
| +CRUD | US1 + US2 + US3 + US4 | Full document management |
| +List | All | Administrative visibility |

### Estimated Effort

| Phase | Tasks | Estimated Time |
|-------|-------|----------------|
| Phase 1: Setup | 5 | 15 min |
| Phase 2: Foundational | 16 | 3-4 hours |
| Phase 3: US1 Query | 8 | 2-3 hours |
| Phase 4: US2 Ingest | 7 | 2 hours |
| Phase 5: US3 Get | 4 | 1 hour |
| Phase 6: US4 Delete | 4 | 45 min |
| Phase 7: US5 List | 6 | 1.5 hours |
| Phase 8: Polish | 7 | 1 hour |
| **Total** | **57** | **~12-14 hours** |

---

## Notes

- All tasks use exact file paths from plan.md project structure
- [P] = parallelizable (different files, no dependencies)
- [USn] = belongs to User Story n for traceability
- Constitution compliance: EF Core only (no raw SQL), test-first approach
- Python API parity: raw JSON responses, `{"detail": "..."}` errors
- Polly policies required for embedding service calls (external dependency)
