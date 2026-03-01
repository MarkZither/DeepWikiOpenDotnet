# Tasks: DeepWiki Wiki System

**Input**: Design documents from `/specs/005-wiki-system/`
**Prerequisites**: plan.md (required), spec.md (required for user stories)

**Tests**: Included — spec SC-006 requires 100% CRUD test coverage and SC-007 requires bUnit component tests.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization — enums, entities, and abstractions that all stories depend on

- [X] T001 [P] Create WikiStatus enum in src/DeepWiki.Data.Abstractions/Entities/WikiStatus.cs — values: Generating, Complete, Partial, Error
- [X] T002 [P] Create PageStatus enum in src/DeepWiki.Data.Abstractions/Entities/PageStatus.cs — values: OK, Error, Generating
- [X] T003 [P] Create WikiEntity in src/DeepWiki.Data.Abstractions/Entities/WikiEntity.cs — Id (Guid), CollectionId (string), Name (string), Description (string?), Status (WikiStatus), CreatedAt (DateTime), UpdatedAt (DateTime), navigation: Pages collection
- [X] T004 [P] Create WikiPageEntity in src/DeepWiki.Data.Abstractions/Entities/WikiPageEntity.cs — Id (Guid), WikiId (Guid FK), Title (string), Content (string), SectionPath (string), SortOrder (int), ParentPageId (Guid? FK), Status (PageStatus), CreatedAt (DateTime), UpdatedAt (DateTime), navigation: Wiki, ParentPage, ChildPages, SourceRelations, TargetRelations
- [X] T005 [P] Create WikiPageRelation in src/DeepWiki.Data.Abstractions/Entities/WikiPageRelation.cs — composite PK (SourcePageId, TargetPageId), navigation: SourcePage, TargetPage
- [X] T006 Create IWikiRepository interface in src/DeepWiki.Data.Abstractions/Interfaces/IWikiRepository.cs — all 14 methods per plan (CreateWikiAsync, GetWikiByIdAsync, GetProjectsAsync, DeleteWikiAsync, UpdateWikiStatusAsync, GetPageByIdAsync, AddPageAsync, UpdatePageAsync, DeletePageAsync, GetPageCountAsync, GetRelatedPagesAsync, SetRelatedPagesAsync, ExistsGeneratingAsync, UpsertPageAsync)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: EF Core configurations, migrations, repository implementations, and DI wiring that MUST be complete before ANY user story can be implemented

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

### Postgres Provider

- [X] T007 [P] Create WikiEntityConfiguration in src/DeepWiki.Data.Postgres/Configuration/WikiEntityConfiguration.cs — table name, PK, required props, CollectionId index, snake_case naming per Postgres convention
- [X] T008 [P] Create WikiPageEntityConfiguration in src/DeepWiki.Data.Postgres/Configuration/WikiPageEntityConfiguration.cs — table name, PK, FK to Wiki (cascade delete), FK to ParentPage (nullable, restrict delete), WikiId index, SectionPath index, text column for Content
- [X] T009 [P] Create WikiPageRelationConfiguration in src/DeepWiki.Data.Postgres/Configuration/WikiPageRelationConfiguration.cs — composite PK (SourcePageId, TargetPageId), FKs to WikiPageEntity with cascade delete, TargetPageId index
- [X] T010 Add DbSet properties to Postgres DbContext in src/DeepWiki.Data.Postgres/DbContexts/PostgresVectorDbContext.cs — add DbSet<WikiEntity> Wikis, DbSet<WikiPageEntity> WikiPages, DbSet<WikiPageRelation> WikiPageRelations, apply configurations in OnModelCreating
- [X] T011 Generate EF Core migration for Postgres — run `dotnet ef migrations add AddWikiTables` in src/DeepWiki.Data.Postgres/; then document migration checklist in the migration file header comment: (1) index impact (CollectionId, WikiId, SectionPath, TargetPageId indexes added), (2) no downtime expected (additive-only schema change), (3) rollback: `dotnet ef migrations remove`; verify migration up/down on a clean local Postgres database
- [X] T012 Implement PostgresWikiRepository in src/DeepWiki.Data.Postgres/Repositories/PostgresWikiRepository.cs — implement all IWikiRepository methods using PostgresVectorDbContext, include eager loading for pages and relations; for `UpsertPageAsync` use EF Core ExecuteUpdate / AddOrUpdate pattern (Postgres `INSERT ON CONFLICT DO UPDATE` via EF); include an idempotency test in T021 verifying double-upsert produces a single row

### SQL Server Provider

- [X] T013 [P] Create WikiEntityConfiguration in src/DeepWiki.Data.SqlServer/Configuration/WikiEntityConfiguration.cs — table name, PK, required props, CollectionId index per SQL Server conventions
- [X] T014 [P] Create WikiPageEntityConfiguration in src/DeepWiki.Data.SqlServer/Configuration/WikiPageEntityConfiguration.cs — table name, PK, FK to Wiki (cascade delete), FK to ParentPage (nullable, no action), WikiId index, SectionPath index, nvarchar(max) for Content
- [X] T015 [P] Create WikiPageRelationConfiguration in src/DeepWiki.Data.SqlServer/Configuration/WikiPageRelationConfiguration.cs — composite PK, FKs with cascade delete, TargetPageId index
- [X] T016 Add DbSet properties to SQL Server DbContext in src/DeepWiki.Data.SqlServer/DbContexts/SqlServerVectorDbContext.cs — add DbSet<WikiEntity>, DbSet<WikiPageEntity>, DbSet<WikiPageRelation>, apply configurations
- [X] T017 Generate EF Core migration for SQL Server — run `dotnet ef migrations add AddWikiTables` in src/DeepWiki.Data.SqlServer/; then document migration checklist in the migration file header comment: (1) index impact (CollectionId, WikiId, SectionPath, TargetPageId indexes added), (2) no downtime expected (additive-only schema change), (3) rollback: `dotnet ef migrations remove`; verify migration up/down on a clean local SQL Server database
- [X] T018 Implement SqlServerWikiRepository in src/DeepWiki.Data.SqlServer/Repositories/SqlServerWikiRepository.cs — implement all IWikiRepository methods using SqlServerVectorDbContext; for `UpsertPageAsync` use EF Core MERGE-equivalent (check-then-insert-or-update within a transaction; SQL Server 2025 does not generate implicit MERGE from EF AddOrUpdate); include idempotency test verifying double-upsert produces a single row

### DI Registration

- [X] T019 Register IWikiRepository in Postgres DI — add `services.AddScoped<IWikiRepository, PostgresWikiRepository>()` to AddPostgresDataLayer in src/DeepWiki.Data.Postgres/
- [X] T020 Register IWikiRepository in SQL Server DI — add `services.AddScoped<IWikiRepository, SqlServerWikiRepository>()` to AddSqlServerDataLayer in src/DeepWiki.Data.SqlServer/

### Entity Tests

- [X] T021 [P] Create WikiEntityTests in tests/DeepWiki.Data.Abstractions.Tests/Entities/WikiEntityTests.cs — test entity construction, default values, status enum values, navigation property initialization
- [X] T021a [P] Create PostgresWikiRepositoryTests in tests/DeepWiki.Data.Postgres.Tests/WikiRepositoryTests.cs — Testcontainers integration tests using a real Postgres container: CreateWikiAsync persists all entities, GetWikiByIdAsync returns full graph, DeleteWikiAsync cascade-removes pages and relations, UpsertPageAsync is idempotent, ExistsGeneratingAsync returns true only during Generating status; use existing Testcontainers fixture pattern from project
- [X] T021b [P] Create SqlServerWikiRepositoryTests in tests/DeepWiki.Data.SqlServer.Tests/WikiRepositoryTests.cs — same test cases as T021a but against a SQL Server container, verifying 100% behaviour parity between providers; confirm UpsertPageAsync check-then-insert-or-update produces a single row under double-call

**Checkpoint**: Foundation ready — entities, migrations, repository implementations, and DI wiring complete. User story implementation can now begin.

---

## Phase 3: User Story 1 — Wiki CRUD via REST API (Priority: P1) 🎯 MVP

**Goal**: Internal developers can create, retrieve, update, and delete wiki structures and their child pages through REST endpoints, persisted in the database.

**Independent Test**: Send HTTP requests (POST, GET, PUT, DELETE) to wiki endpoints and verify database state. Delivers a usable API for any client.

### Tests for User Story 1

- [ ] T022 [P] [US1] Create WikiServiceTests in tests/DeepWiki.Rag.Core.Tests/Services/WikiServiceTests.cs — test CreateWikiAsync (valid + validation failures), GetWikiByIdAsync (found + not found), GetProjectsAsync (pagination), DeleteWikiAsync (exists + not exists), AddPageAsync, UpdatePageAsync, DeletePageAsync, UpdateRelatedPagesAsync; use inline test doubles for IWikiRepository
- [ ] T023 [P] [US1] Create WikiControllerTests in tests/deepwiki-open-dotnet.Tests/Controllers/WikiControllerTests.cs — test all CRUD endpoints: POST /api/wiki (201 + 400), GET /api/wiki/{id} (200 + 404), DELETE /api/wiki/{id} (204 + 404), GET /api/wiki/projects (200 + pagination), PUT /api/wiki/{id} (200 description update + 400 if name field included + 404), PUT /api/wiki/{id}/pages/{pageId} (200 + 404), POST /api/wiki/{id}/pages (201 + 400), DELETE /api/wiki/{id}/pages/{pageId} (204 + 404); use inline test doubles following existing GenerationControllerUnitTests pattern

### Implementation for User Story 1

- [ ] T024 [P] [US1] Create IWikiService interface in src/DeepWiki.Rag.Core/Services/IWikiService.cs — CreateWikiAsync, GetWikiByIdAsync, GetProjectsAsync, DeleteWikiAsync, AddPageAsync, UpdatePageAsync, DeletePageAsync, UpdateRelatedPagesAsync
- [ ] T025 [P] [US1] Create WikiGenerationOptions in src/DeepWiki.Rag.Core/Models/WikiGenerationOptions.cs — Mode (string: sequential/parallel), MaxParallelPages (int), MaxTocRetries (int), PageTokenLimit (int)
- [ ] T026 [US1] Implement WikiService in src/DeepWiki.Rag.Core/Services/WikiService.cs — inject IWikiRepository, implement all IWikiService methods with validation (name required/max 200 chars, collectionId required, page title+content required), timestamp management, DTO-to-entity mapping
- [ ] T027 [P] [US1] Create API request DTOs in src/deepwiki-open-dotnet.ApiService/Models/ — CreateWikiRequest.cs (Name, CollectionId, Description?, Pages[]), CreateWikiPageRequest.cs (Title, Content, SectionPath, SortOrder?, RelatedPageIds?), UpdateWikiPageRequest.cs (Title?, Content?, SectionPath?, SortOrder?, RelatedPageIds?), with DataAnnotations validation attributes
- [ ] T028 [P] [US1] Create API response DTOs in src/deepwiki-open-dotnet.ApiService/Models/ — WikiResponse.cs (Id, Name, Description, CollectionId, Status, Pages[], CreatedAt, UpdatedAt), WikiPageResponse.cs (Id, Title, Content, SectionPath, SortOrder, Status, RelatedPages[], CreatedAt, UpdatedAt), WikiSummaryResponse.cs (Id, Name, CollectionSource, PageCount, LastModified)
- [ ] T029 [US1] Implement WikiController CRUD endpoints in src/deepwiki-open-dotnet.ApiService/Controllers/WikiController.cs — POST /api/wiki (201), GET /api/wiki/{id} (200/404), DELETE /api/wiki/{id} (204/404), GET /api/wiki/projects (200), GET /api/wiki/{id}/pages/{pageId} (200/404), PUT /api/wiki/{id}/pages/{pageId} (200/404), POST /api/wiki/{id}/pages (201/400), DELETE /api/wiki/{id}/pages/{pageId} (204/404); follow existing [ApiController] + ProducesResponseType pattern
- [ ] T029a [US1] Add PUT /api/wiki/{id} endpoint to WikiController in src/deepwiki-open-dotnet.ApiService/Controllers/WikiController.cs — accepts UpdateWikiRequest.cs DTO (Description string? only; if request body contains a Name field return 400 with message "Wiki name is immutable after creation"); call IWikiService.UpdateWikiDescriptionAsync(id, description); return 200 with WikiSummaryResponse or 404; add UpdateWikiRequest.cs to src/deepwiki-open-dotnet.ApiService/Models/ and UpdateWikiDescriptionAsync to IWikiService + WikiService
- [ ] T030 [US1] Register wiki services in ApiService Program.cs — add IWikiService/WikiService (scoped), configure WikiGenerationOptions from "Wiki:Generation" config section; add after existing service registrations in src/deepwiki-open-dotnet.ApiService/Program.cs

**Checkpoint**: Wiki CRUD API is fully functional. Can create wikis with pages, retrieve, update individual pages, delete wikis/pages. All 8 acceptance scenarios testable via HTTP.

---

## Phase 4: User Story 2 — Browse Wiki Projects (Priority: P1)

**Goal**: Internal users navigate to the Wiki section and see a paginated list of all wiki projects with name, collection source, page count, and last-modified date. Clicking a project navigates to the wiki viewer.

**Independent Test**: Verify `/api/wiki/projects` endpoint returns correct paginated results and Blazor component renders the list correctly (bUnit test).

### Tests for User Story 2

- [ ] T031 [P] [US2] Create WikiProjectListTests in tests/deepwiki-open-dotnet.Web.Tests/Components/WikiProjectListTests.cs — bUnit tests: renders project table with name/collection/pageCount/lastModified columns, handles empty state with guidance message, handles pagination with >20 items, clicking a row triggers navigation callback

### Implementation for User Story 2

- [ ] T032 [US2] Create WikiApiClient in src/deepwiki-open-dotnet.Web/Services/WikiApiClient.cs — typed HttpClient with GetProjectsAsync, GetWikiByIdAsync, DeleteWikiAsync, CreateWikiAsync, AddPageAsync, UpdatePageAsync, DeletePageAsync methods; register in Web Program.cs with Aspire service discovery base address "https+http://apiservice"
- [ ] T033 [US2] Create WikiProjectList component in src/deepwiki-open-dotnet.Web/Components/Shared/WikiProjectList.razor — MudTable with columns (Name, Collection, PageCount, LastModified), MudPagination, loading state with MudProgressLinear, empty state with MudAlert, OnProjectClick EventCallback<Guid>
- [ ] T034 [US2] Create WikiProjects page in src/deepwiki-open-dotnet.Web/Components/Pages/WikiProjects.razor — @page "/wiki", loads project list on init via WikiApiClient, renders WikiProjectList component, on project click navigates to /wiki/{id}
- [ ] T035 [US2] Add Wiki nav link to NavMenu in src/deepwiki-open-dotnet.Web/Components/Layout/NavMenu.razor — add nav-item with bi-journal-text icon, "Wiki" label, href="wiki", placed after Documents link

**Checkpoint**: Users can visit /wiki and see all wiki projects. Clicking a project navigates to /wiki/{id} (viewer not yet implemented — Phase 5).

---

## Phase 5: User Story 3 — Read Wiki Content with Section Navigation (Priority: P2)

**Goal**: User clicks a wiki project and sees a sidebar with sections/pages. Clicking a page displays Markdown-rendered content. Related pages shown as navigable links.

**Independent Test**: bUnit component tests render wiki viewer with mock data, verify sidebar navigation, content rendering, and related-page links.

### Tests for User Story 3

- [ ] T036 [P] [US3] Create WikiSidebarTests in tests/deepwiki-open-dotnet.Web.Tests/Components/WikiSidebarTests.cs — bUnit tests: builds tree from flat SectionPath list, renders expandable section nodes, clicking page node triggers selection callback, handles nested sections with correct indentation, highlights selected page
- [ ] T037 [P] [US3] Create WikiPageContentTests in tests/deepwiki-open-dotnet.Web.Tests/Components/WikiPageContentTests.cs — bUnit tests: renders Markdown content via Markdig, displays related page links as MudChip, handles page with no related pages, marks deleted related pages as "(page removed)"

### Implementation for User Story 3

- [ ] T038 [US3] Create WikiSidebar component in src/deepwiki-open-dotnet.Web/Components/Shared/WikiSidebar.razor — accepts WikiPageResponse[] parameter, builds tree from SectionPath (split on "/"), renders MudTreeView<WikiPageNode> with expand/collapse, non-clickable section folders, clickable page leaves, OnPageSelected EventCallback<Guid>, highlights active page
- [ ] T039 [US3] Create WikiPageContent component in src/deepwiki-open-dotnet.Web/Components/Shared/WikiPageContent.razor — accepts WikiPageResponse parameter, renders Content as Markdown using shared Markdig pipeline, displays related pages as MudChip list below content, each chip navigates to related page via OnRelatedPageClick EventCallback<Guid>, handles missing related pages with "(page removed)" label
- [ ] T040 [US3] Create WikiViewer page in src/deepwiki-open-dotnet.Web/Components/Pages/WikiViewer.razor — @page "/wiki/{Id:guid}", loads wiki via WikiApiClient.GetWikiByIdAsync on init, MudGrid layout (Col 3 sidebar, Col 9 content), renders WikiSidebar and WikiPageContent, default-selects first page by SortOrder, sidebar page click updates content area, supports ?page={pageId} query param for direct page selection, export button triggers file download, delete button with MudDialog confirmation
- [ ] T041 [US3] Create WikiPage redirect in src/deepwiki-open-dotnet.Web/Components/Pages/WikiPage.razor — @page "/wiki/{WikiId:guid}/pages/{PageId:guid}", redirects to /wiki/{WikiId}?page={PageId} using NavigationManager on init

**Checkpoint**: Full wiki reading experience — project list → wiki viewer → sidebar navigation → Markdown content → related page links. All acceptance scenarios for US3 testable.

---

## Phase 6: User Story 4 — Export Wiki as Markdown or JSON (Priority: P2)

**Goal**: User exports a wiki as a single downloadable file (Markdown with TOC or structured JSON) for offline reading, sharing, or importing.

**Independent Test**: Call export endpoint with a known wiki and validate output format (Markdown TOC structure, JSON schema) against expected output.

### Tests for User Story 4

- [ ] T042 [P] [US4] Create WikiExportServiceTests in tests/DeepWiki.Rag.Core.Tests/Services/WikiExportServiceTests.cs — test ExportAsMarkdownAsync: produces valid Markdown with TOC anchor links, ## headings per page, Related Pages section with links, handles wiki with zero pages (empty doc with metadata), handles deleted related page with "(page removed)"; test ExportAsJsonAsync: produces valid JSON with metadata object (name, description, exportDate, pageCount), pages array with title/content/section/sortOrder/relatedPages, handles zero pages

### Implementation for User Story 4

- [ ] T043 [P] [US4] Create IWikiExportService interface in src/DeepWiki.Rag.Core/Services/IWikiExportService.cs — ExportAsMarkdownAsync(WikiEntity, IReadOnlyList<WikiPageEntity>, IDictionary<Guid, IReadOnlyList<WikiPageEntity>> relatedPages, Stream output), ExportAsJsonAsync (same signature)
- [ ] T044 [US4] Implement WikiExportService in src/DeepWiki.Rag.Core/Services/WikiExportService.cs — ExportAsMarkdownAsync: write heading, description, TOC with anchor links, then each page under ## with content and Related Pages subheading; explicitly handle zero-page wiki by emitting the metadata header followed by `> No pages have been generated for this wiki.`; ExportAsJsonAsync: use Utf8JsonWriter for streaming, write metadata object + pages array (empty array for zero-page wiki); register as singleton in ApiService Program.cs
- [ ] T045 [P] [US4] Create WikiExportRequest DTO in src/deepwiki-open-dotnet.ApiService/Models/WikiExportRequest.cs — WikiId (Guid, required), Format (string: "markdown" or "json", required with validation)
- [ ] T046 [US4] Add export endpoint to WikiController in src/deepwiki-open-dotnet.ApiService/Controllers/WikiController.cs — POST /api/wiki/export, validate format, load wiki + pages + related pages via IWikiService and IWikiRepository, call IWikiExportService, return FileStreamResult with Content-Disposition header, 404 if wiki not found
- [ ] T047 [US4] Add ExportWikiAsync to WikiApiClient in src/deepwiki-open-dotnet.Web/Services/WikiApiClient.cs — POST to /api/wiki/export, return Stream for browser download; in WikiViewer.razor wire the export button to inject `IJSRuntime` and call a `downloadFileFromStream(fileName, stream)` JS interop helper (add `wwwroot/js/fileDownload.js` with the helper if not already present, and register it in `App.razor`); pass `fileName` as `"{wikiName}.md"` or `"{wikiName}.json"` based on selected format

**Checkpoint**: Users can export any wiki as Markdown or JSON via API and UI. All 4 acceptance scenarios for US4 testable.

---

## Phase 7: User Story 5 — Generate Wiki from a Collection (Priority: P3)

**Goal**: User selects a collection and triggers wiki generation. System uses two-phase LLM pipeline (TOC → pages), streams progress, persists result. User can edit structure after generation.

**Independent Test**: Mock generation service and test collection — verify orchestration creates correct pages, progress streams, final wiki persists.

### Tests for User Story 5

- [ ] T048 [P] [US5] Create WikiGenerationOrchestratorTests in tests/DeepWiki.Rag.Core.Tests/Services/WikiGenerationOrchestratorTests.cs — test with mock IGenerationService + mock IVectorStore + mock IEmbeddingService + mock IWikiRepository: Phase 1 TOC generation calls LLM and parses JSON TOC, Phase 2 generates pages sequentially (default mode), pages persisted with correct content/status, related pages resolved and stored as WikiPageRelation rows, handles TOC parse failure with retry, handles page generation failure (marks page Error, continues), cancellation saves completed pages and sets wiki Partial, concurrent generation guard returns 409, progress events emitted in correct order (wiki_created → toc_complete → page_start/page_complete per page → generation_complete)

### Implementation for User Story 5

- [ ] T049 [P] [US5] Create WikiGenerationProgress model in src/DeepWiki.Rag.Core/Models/WikiGenerationProgress.cs — EventType (string: wiki_created, toc_complete, page_start, page_token, page_complete, page_error, generation_complete, generation_cancelled), WikiId (Guid), PageIndex (int?), TotalPages (int?), PageTitle (string?), TokenText (string?), ErrorMessage (string?), Status (string?)
- [ ] T050 [P] [US5] Create WikiGenerationRequest model in src/DeepWiki.Rag.Core/Models/WikiGenerationRequest.cs — CollectionId (string, required), Name (string, required), Description (string?)
- [ ] T051 [P] [US5] Create IWikiGenerationService interface in src/DeepWiki.Rag.Core/Services/IWikiGenerationService.cs — GenerateAsync(WikiGenerationRequest request, CancellationToken ct) returning IAsyncEnumerable<WikiGenerationProgress>
- [ ] T052 [P] [US5] Create TOC prompt template as embedded resource in src/DeepWiki.Rag.Core/Prompts/wiki-toc-prompt.txt — per plan specification with {document_summaries} and {max_pages} placeholders; add version comment header `# Version: 1.0.0 | Feature: wiki-toc-generation | Date: 2026-03-01` as first line; create companion src/DeepWiki.Rag.Core/Prompts/wiki-toc-prompt.CHANGELOG.md documenting initial version, rationale, and expected output format
- [ ] T053 [P] [US5] Create page content prompt template as embedded resource in src/DeepWiki.Rag.Core/Prompts/wiki-page-prompt.txt — per plan specification with {wiki_name}, {section_path}, {page_title}, {toc_json}, {document_chunks} placeholders and RELATED_PAGES output format; add version comment header `# Version: 1.0.0 | Feature: wiki-page-generation | Date: 2026-03-01` as first line; create companion src/DeepWiki.Rag.Core/Prompts/wiki-page-prompt.CHANGELOG.md documenting initial version, rationale, and RELATED_PAGES parsing contract
- [ ] T053a [P] [US5] Create LLM snapshot directory and baseline fixtures — create `llm-snapshots/wiki/README.md` documenting snapshot format, redaction policy, and playback instructions per constitution §LLM Policy; create `llm-snapshots/wiki/toc-v1.0.0.json` and `llm-snapshots/wiki/page-v1.0.0.json` as initial baseline snapshot fixtures using the canonical snapshot JSON schema (id, created_at, model, request, stream, response_hash, redacted, retention_policy fields)
- [ ] T053b [US5] Create WikiTocParserTests in tests/DeepWiki.Rag.Core.Tests/Services/WikiTocParserTests.cs — snapshot-based parsing tests that replay `llm-snapshots/wiki/toc-v1.0.0.json`; verify the TOC JSON parser correctly extracts sections and page entries from the fixture stream; test parse failure cases (malformed JSON, missing sections array) triggering retry logic; these MUST pass before T054 is implemented per constitution §I (Test-First) and §II (Reproducibility)
- [ ] T053c [US5] Create WikiPageParserTests in tests/DeepWiki.Rag.Core.Tests/Services/WikiPageParserTests.cs — snapshot-based parsing tests that replay `llm-snapshots/wiki/page-v1.0.0.json`; verify the page content parser correctly splits the RELATED_PAGES footer from the Markdown body; test edge cases: no RELATED_PAGES line present, empty RELATED_PAGES array, page titles with special characters; these MUST pass before T054 is implemented
- [ ] T054 [US5] Implement WikiGenerationOrchestrator in src/DeepWiki.Rag.Core/Services/WikiGenerationOrchestrator.cs — inject IWikiRepository, IGenerationService, IVectorStore (optional), IEmbeddingService (optional), IOptions<WikiGenerationOptions>; implement two-phase pipeline: Phase 1 (embed collection name → query IVectorStore for doc summaries → build TOC prompt → call IGenerationService → parse JSON TOC → persist empty pages), Phase 2 (for each TOC entry: embed page title+section → query IVectorStore for relevant chunks → build page prompt → call IGenerationService → parse content + RELATED_PAGES → upsert page → insert WikiPageRelation rows); support sequential and parallel modes via options; include ConcurrentDictionary guard for collection+name; emit WikiGenerationProgress events throughout; handle errors per page (mark Error, continue); handle cancellation (save completed, set Partial)
- [ ] T055 [P] [US5] Create GenerateWikiRequest API DTO in src/deepwiki-open-dotnet.ApiService/Models/GenerateWikiRequest.cs — CollectionId (string, required), Name (string, required, max 200), Description (string?)
- [ ] T056 [US5] Add generate endpoint to WikiController in src/deepwiki-open-dotnet.ApiService/Controllers/WikiController.cs — POST /api/wiki/generate, set Content-Type application/x-ndjson, await foreach over IWikiGenerationService.GenerateAsync, serialize each WikiGenerationProgress as JSON line + newline + flush, catch OperationCanceledException → emit generation_cancelled event, return 409 if ExistsGeneratingAsync returns true
- [ ] T057 [US5] Register IWikiGenerationService in ApiService Program.cs — add services.AddScoped<IWikiGenerationService, WikiGenerationOrchestrator>() in src/deepwiki-open-dotnet.ApiService/Program.cs
- [ ] T058 [US5] Add GenerateWikiAsync to WikiApiClient in src/deepwiki-open-dotnet.Web/Services/WikiApiClient.cs — POST to /api/wiki/generate, return IAsyncEnumerable<WikiGenerationProgress> using HttpClient.GetStreamAsync + NdjsonStreamReader pattern from existing code
- [ ] T059 [US5] Create WikiGenerationProgress component in src/deepwiki-open-dotnet.Web/Components/Shared/WikiGenerationProgress.razor — accepts IAsyncEnumerable<WikiGenerationProgress> or list of progress events, displays MudTimeline or MudList with page generation status (generating/complete/error), MudProgressLinear for overall progress (pagesComplete/totalPages), shows current page title being generated, error messages per failed page, cancel button that triggers CancellationToken
- [ ] T060 [US5] Add generation trigger to WikiProjects page in src/deepwiki-open-dotnet.Web/Components/Pages/WikiProjects.razor — "Generate Wiki" MudButton opens MudDialog with collection selector (use existing `DocumentScopeSelector` component at src/deepwiki-open-dotnet.Web/Components/Shared/DocumentScopeSelector.razor — confirmed present) and wiki name MudTextField, on confirm calls WikiApiClient.GenerateWikiAsync, navigates to /wiki/{newWikiId} showing WikiGenerationProgress overlay
- [ ] T061 [US5] Add generation status display to WikiViewer in src/deepwiki-open-dotnet.Web/Components/Pages/WikiViewer.razor — if wiki Status is Generating, show WikiGenerationProgress component instead of page content; once generation_complete event received, reload wiki and switch to normal view

**Checkpoint**: Full wiki generation pipeline working end-to-end. Two-phase generation, progress streaming, error handling, cancellation, and post-generation editing all functional.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Observability, configuration, and quality improvements across all stories

- [ ] T062 [P] Add wiki-specific OTel counters in src/DeepWiki.Rag.Core/Services/WikiService.cs and WikiGenerationOrchestrator.cs — deepwiki.wiki.created (Counter), deepwiki.wiki.pages_generated (Counter, tag: status=ok|error), deepwiki.wiki.generation_duration_seconds (Histogram), deepwiki.wiki.export_count (Counter, tag: format=markdown|json); register via IMeterFactory following existing pattern
- [ ] T063 [P] Add Wiki:Generation configuration section to src/deepwiki-open-dotnet.ApiService/appsettings.json — Mode: "sequential", MaxParallelPages: 3, MaxTocRetries: 2, PageTokenLimit: 4000
- [ ] T064 [P] Add wiki API endpoint documentation via OpenAPI attributes on WikiController — [EndpointSummary], [EndpointDescription], [ProducesResponseType] for all endpoints including error responses, in src/deepwiki-open-dotnet.ApiService/Controllers/WikiController.cs
- [ ] T064a Add LLM snapshot recording to WikiGenerationOrchestrator — define `IWikiSnapshotRecorder` interface in src/DeepWiki.Rag.Core/Snapshots/IWikiSnapshotRecorder.cs with a single `RecordAsync(WikiSnapshotEntry entry, CancellationToken ct)` method; implement `WikiSnapshotRecorder` (writes JSON to `OutputPath`) and `NullWikiSnapshotRecorder` (no-op) in the same folder; register as singleton in ApiService Program.cs using `IWikiSnapshotRecorder`; add `Wiki:Snapshots:Enabled` (bool, default: false) and `Wiki:Snapshots:OutputPath` (string, default: `./llm-snapshots/wiki/`) to appsettings.json; wire into WikiGenerationOrchestrator.cs around each IGenerationService call to record request prompt, streaming chunks, and response hash per the constitution snapshot JSON schema; snapshots tagged with `feature: wiki-toc-generation` or `wiki-page-generation`
- [ ] T064b Perform Agent Framework Compatibility Review for WikiGenerationOrchestrator per constitution §VIII — verify: (1) `IWikiGenerationService.GenerateAsync` is callable from Agent Framework tool bindings without wrapper logic, (2) `WikiGenerationProgress` is fully JSON-serializable, (3) error handling returns agent-recoverable results (no unhandled exceptions that break reasoning loops), (4) add integration example to examples/WikiGenerationAgentTool.cs documenting usage from tool context
- [ ] T065 Run all wiki tests and verify passes — execute `dotnet test` across all test projects for wiki-related test files
- [ ] T066 Validate quickstart flow — create a wiki via POST, add pages, retrieve, export as both formats, delete; verify all steps succeed per spec acceptance scenarios
- [ ] T066b [P] Create WikiPerformanceSmokeTests in tests/deepwiki-open-dotnet.Tests/Performance/WikiPerformanceSmokeTests.cs — seed database with a 100-page wiki and 100 total wiki projects; execute timed requests and assert: GET /api/wiki/{id} median latency <200ms (SC-002), GET /api/wiki/projects median latency <500ms (SC-004), POST /api/wiki with 50 pages completes <2s (SC-001), export 50-page wiki completes <3s (SC-003); use xUnit with a shared Testcontainers fixture; mark with [Trait("Category", "Performance")] so they can be run independently

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 entities — BLOCKS all user stories
- **US1 (Phase 3)**: Depends on Phase 2 — delivers MVP API
- **US2 (Phase 4)**: Depends on Phase 2 + US1 (needs wiki data to browse)
- **US3 (Phase 5)**: Depends on Phase 2 + US2 (needs project list navigation) + US1 API
- **US4 (Phase 6)**: Depends on Phase 2 + US1 (needs wiki data to export); can parallel with US2/US3
- **US5 (Phase 7)**: Depends on Phase 2 + US1 (needs CRUD to persist generated wikis); can parallel with US3/US4
- **Polish (Phase 8)**: Depends on all desired user stories being complete

### User Story Dependencies

- **US1 (P1)**: Foundation only — no other story dependencies. **MVP scope.**
- **US2 (P1)**: US1 must be complete (needs projects endpoint and wiki data to list)
- **US3 (P2)**: US1 must be complete (needs wiki + page data). US2 provides navigation entry point but not strictly required for testing.
- **US4 (P2)**: US1 must be complete (needs wiki data to export). **Independent of US2/US3** — can be implemented in parallel.
- **US5 (P3)**: US1 must be complete (needs CRUD for persisting generated wikis). Benefits from US2/US3 UI for viewing results but can be tested via API alone.

### Within Each User Story

- Tests MUST be written and FAIL before implementation
- Interfaces before implementations
- DTOs before controller (can parallel with service)
- Service before controller
- Backend before frontend components
- Story complete before moving to next priority

### Parallel Opportunities

**Phase 1**: All 6 tasks (T001–T006) can run in parallel (different files)

**Phase 2**: Postgres tasks (T007–T012) can run in parallel with SQL Server tasks (T013–T018); entity tests (T021) and Testcontainers integration tests (T021a, T021b) can parallel with both

**Phase 3 (US1)**: Tests (T022, T023) parallel; interface + DTOs (T024, T025, T027, T028) parallel; then service (T026) + controller (T029) sequential

**Phase 4 (US2)**: Test (T031) parallel with WikiApiClient (T032) until component implementation

**Phase 5 (US3)**: Tests (T036, T037) parallel; sidebar + content components (T038, T039) parallel; then viewer page (T040)

**Phase 6 (US4)**: Test (T042) parallel with interface + DTO (T043, T045); then implementation + controller

**Phase 7 (US5)**: Tests (T048) parallel with models + interface + prompts (T049, T050, T051, T052, T053, T055); snapshot fixtures + parser tests (T053a, T053b, T053c) MUST complete before T054; then orchestrator (T054) → controller (T056) → UI (T058–T061)

---

## Parallel Example: User Story 1

```bash
# Launch all [P] tasks for US1 together:
Task T022: "WikiServiceTests in tests/DeepWiki.Rag.Core.Tests/Services/WikiServiceTests.cs"
Task T023: "WikiControllerTests in tests/deepwiki-open-dotnet.Tests/Controllers/WikiControllerTests.cs"
Task T024: "IWikiService interface in src/DeepWiki.Rag.Core/Services/IWikiService.cs"
Task T025: "WikiGenerationOptions in src/DeepWiki.Rag.Core/Models/WikiGenerationOptions.cs"
Task T027: "API request DTOs in src/deepwiki-open-dotnet.ApiService/Models/"
Task T028: "API response DTOs in src/deepwiki-open-dotnet.ApiService/Models/"

# Then sequential:
Task T026: "WikiService implementation" (depends on T024)
Task T029: "WikiController CRUD endpoints" (depends on T026, T027, T028)
Task T030: "Register wiki services in Program.cs" (depends on T026)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (entities + enums)
2. Complete Phase 2: Foundational (EF configs, migrations, repos, DI)
3. Complete Phase 3: User Story 1 (service + controller + tests)
4. **STOP and VALIDATE**: Test all CRUD operations via HTTP
5. Deploy/demo if ready — full wiki API usable by any client

### Incremental Delivery

1. Setup + Foundational → Data layer ready
2. Add US1 → Wiki CRUD API ✅ → **MVP!**
3. Add US2 → Browsable project list in UI ✅
4. Add US3 → Full wiki reading experience ✅
5. Add US4 → Export capability ✅ (can parallel with US3)
6. Add US5 → Wiki generation from collections ✅
7. Each story adds value without breaking previous stories

### Parallel Team Strategy

With multiple developers:

1. Team completes Setup + Foundational together
2. Once Foundational is done:
   - Developer A: User Story 1 (MVP API) → then US5 (generation)
   - Developer B: User Story 4 (export — only needs US1 foundation) → then US2 (browse)
   - Developer C: User Story 3 (viewer — after US1 is merged) → then Polish
3. Stories complete and integrate independently

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks
- [Story] label maps task to specific user story for traceability
- Each user story is independently completable and testable after Phase 2
- Verify tests fail before implementing
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- Avoid: vague tasks, same-file conflicts, cross-story dependencies that break independence
- Total: 75 tasks across 8 phases (6 setup + 17 foundational + 10 US1 + 5 US2 + 6 US3 + 6 US4 + 17 US5 + 8 polish)
