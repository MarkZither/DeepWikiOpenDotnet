# Implementation Plan: DeepWiki Wiki System

**Branch**: `005-wiki-system` | **Date**: 2026-03-01 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/005-wiki-system/spec.md`

## Summary

Build a wiki system that lets users generate, browse, edit, and export structured wiki documentation from ingested document collections. The implementation adds three EF Core entities (WikiEntity, WikiPageEntity, WikiPageRelation) to the existing dual-provider data layer, three focused services in DeepWiki.Rag.Core (CRUD, generation orchestration, export), a WikiController with full REST API, and a Blazor UI with separate routed pages for project list, wiki viewer, and direct page links. Wiki generation uses a two-phase LLM pipeline (TOC generation → page content generation) with configurable sequential/parallel page generation and NDJSON progress streaming. Related pages use a proper join table for referential integrity. Export supports Markdown (with TOC) and JSON formats.

## Technical Context

**Language/Version**: C# 13 / .NET 10  
**Primary Dependencies**: EF Core 10.x (dual-provider), MudBlazor 7.x, Markdig, System.Text.Json, xUnit, FluentAssertions, Moq  
**Storage**: PostgreSQL 17+ (pgvector) and SQL Server 2025 — both via existing dual-provider setup. Three new tables: `Wikis`, `WikiPages`, `WikiPageRelations`.  
**Testing**: xUnit (unit), bUnit (Blazor components), FluentAssertions, inline test doubles (project convention), 90%+ coverage target  
**Target Platform**: Cross-platform (.NET 10), Blazor InteractiveServer  
**Project Type**: Multi-project — extends existing projects (no new .csproj files)  
**Performance Goals**:
- Wiki CRUD: <200ms for wikis with ≤100 pages (SC-002)
- Export: <3s for 50-page wiki (SC-003)
- Project list: <500ms for ≤100 projects (SC-004)
- Generation: <5 min for 20-document collection (SC-005), dependent on LLM
- UI page navigation: <300ms content render (SC-008)

**Constraints**:
- No authentication (internal-only MVP)
- Must work with both Postgres and SQL Server providers
- Must reuse existing streaming infrastructure (NDJSON, IAsyncEnumerable)
- Must not modify existing DocumentEntity or IVectorStore interfaces
- Wiki generation depends on existing IGenerationService/provider pipeline

**Scale/Scope**: Up to 500 pages per wiki (export streaming for large wikis), up to 100 wiki projects. Concurrent generation guard per collection+name pair.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Test-First (NON-NEGOTIABLE)
- ✅ **PASS**: SC-006 requires 100% CRUD test coverage; SC-007 requires bUnit component tests
- ✅ **PASS**: Export format validation tests against expected Markdown/JSON output
- **Action**: Tests MUST be written before implementation per project convention

### Reproducibility & Determinism
- ⚠️ **PARTIAL**: Wiki generation uses LLM — output is non-deterministic
- **Action**: Test orchestration flow with mock IGenerationService (deterministic stubs); validate structure not content

### Local-First ML
- ✅ **PASS**: Reuses existing Ollama-first provider pipeline; no new ML dependencies
- **Action**: Generation prompts must work with Ollama models (smaller context windows)

### Observability & Cost Visibility
- ✅ **PASS**: Generation streams progress events; existing OTel metrics cover LLM calls
- **Action**: Add wiki-specific counters: wikis_created, pages_generated, generation_duration_seconds, export_count

### Security & Privacy
- ✅ **PASS**: No auth for MVP (internal-only); no PII in wiki content beyond what's in source documents
- **Action**: Validate wiki name/description input length limits to prevent abuse

### Entity Framework Core
- ✅ **PASS**: Three new entities follow existing patterns; migrations for both providers required
- **Action**: Include indexes on WikiId (pages table), SourcePageId/TargetPageId (relations). Test migration up/down.

### Frontend Accessibility & i18n
- ⚠️ **DEFERRED**: WCAG 2.1 AA for wiki components — documented for Phase 2
- ✅ **PASS**: MudBlazor components provide baseline accessibility

**Overall Assessment**: ✅ PASS — proceed to implementation. LLM non-determinism mitigated by structural testing with mocks.

## Project Structure

### Documentation (this feature)

```text
specs/005-wiki-system/
├── plan.md              # This file
├── research.md          # Phase 0: Generation prompting strategies, tree-view UX
├── data-model.md        # Phase 1: Entity model, DTOs, state flows
├── quickstart.md        # Phase 1: How to use the wiki system
├── contracts/           # Phase 1: JSON schemas for wiki API
└── tasks.md             # Phase 2: Task breakdown (/speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── DeepWiki.Data.Abstractions/
│   ├── Entities/
│   │   ├── WikiEntity.cs                    # NEW: Wiki aggregate root
│   │   ├── WikiPageEntity.cs                # NEW: Wiki page with section hierarchy
│   │   ├── WikiPageRelation.cs              # NEW: Join table for related pages
│   │   └── WikiStatus.cs                    # NEW: Enum (Generating, Complete, Partial, Error)
│   └── Interfaces/
│       └── IWikiRepository.cs               # NEW: Wiki persistence contract
│
├── DeepWiki.Data.Postgres/
│   ├── Configuration/
│   │   ├── WikiEntityConfiguration.cs       # NEW: EF config for Postgres
│   │   ├── WikiPageEntityConfiguration.cs   # NEW
│   │   └── WikiPageRelationConfiguration.cs # NEW
│   ├── Repositories/
│   │   └── PostgresWikiRepository.cs        # NEW: IWikiRepository impl
│   └── Migrations/
│       └── YYYYMMDD_AddWikiTables.cs        # NEW: Migration
│
├── DeepWiki.Data.SqlServer/
│   ├── Configuration/
│   │   ├── WikiEntityConfiguration.cs       # NEW: EF config for SQL Server
│   │   ├── WikiPageEntityConfiguration.cs   # NEW
│   │   └── WikiPageRelationConfiguration.cs # NEW
│   ├── Repositories/
│   │   └── SqlServerWikiRepository.cs       # NEW: IWikiRepository impl
│   └── Migrations/
│       └── YYYYMMDD_AddWikiTables.cs        # NEW: Migration
│
├── DeepWiki.Rag.Core/
│   ├── Services/
│   │   ├── IWikiService.cs                  # NEW: CRUD orchestration interface
│   │   ├── WikiService.cs                   # NEW: Validation, timestamps, status
│   │   ├── IWikiGenerationService.cs        # NEW: Two-phase generation interface
│   │   ├── WikiGenerationOrchestrator.cs    # NEW: TOC → pages pipeline
│   │   ├── IWikiExportService.cs            # NEW: Export interface
│   │   └── WikiExportService.cs             # NEW: Markdown/JSON export
│   └── Models/
│       ├── WikiGenerationProgress.cs        # NEW: Progress event DTO
│       ├── WikiGenerationRequest.cs         # NEW: Generation input
│       └── WikiGenerationOptions.cs         # NEW: Sequential/parallel config
│
├── deepwiki-open-dotnet.ApiService/
│   ├── Controllers/
│   │   └── WikiController.cs                # NEW: Full wiki REST API
│   └── Models/
│       ├── CreateWikiRequest.cs             # NEW
│       ├── CreateWikiPageRequest.cs         # NEW
│       ├── UpdateWikiPageRequest.cs         # NEW
│       ├── WikiExportRequest.cs             # NEW
│       ├── GenerateWikiRequest.cs           # NEW
│       ├── WikiResponse.cs                  # NEW
│       ├── WikiPageResponse.cs              # NEW
│       └── WikiSummaryResponse.cs           # NEW
│
├── deepwiki-open-dotnet.Web/
│   ├── Components/
│   │   ├── Pages/
│   │   │   ├── WikiProjects.razor           # NEW: @page "/wiki" — project list
│   │   │   ├── WikiViewer.razor             # NEW: @page "/wiki/{Id}" — viewer
│   │   │   └── WikiPage.razor               # NEW: @page "/wiki/{WikiId}/pages/{PageId}" — direct link
│   │   ├── Shared/
│   │   │   ├── WikiProjectList.razor        # NEW: Paginated project table
│   │   │   ├── WikiSidebar.razor            # NEW: MudTreeView section nav
│   │   │   ├── WikiPageContent.razor        # NEW: Markdown rendering + related links
│   │   │   └── WikiGenerationProgress.razor # NEW: Live progress display
│   │   └── Layout/
│   │       └── NavMenu.razor                # MODIFY: Add wiki nav link
│   └── Services/
│       └── WikiApiClient.cs                 # NEW: Typed HTTP client for wiki API

tests/
├── DeepWiki.Data.Abstractions.Tests/
│   └── Entities/
│       └── WikiEntityTests.cs               # NEW: Entity validation/construction
├── DeepWiki.Data.Postgres.Tests/
│   └── WikiRepositoryTests.cs              # NEW: Integration tests (Testcontainers)
├── DeepWiki.Data.SqlServer.Tests/
│   └── WikiRepositoryTests.cs              # NEW: Integration tests (Testcontainers)
├── DeepWiki.Rag.Core.Tests/
│   └── Services/
│       ├── WikiServiceTests.cs              # NEW: CRUD service unit tests
│       ├── WikiGenerationOrchestratorTests.cs # NEW: Two-phase pipeline tests
│       └── WikiExportServiceTests.cs        # NEW: Export format tests
├── deepwiki-open-dotnet.Tests/
│   └── Controllers/
│       └── WikiControllerTests.cs           # NEW: Controller unit tests
└── deepwiki-open-dotnet.Web.Tests/
    └── Components/
        ├── WikiProjectListTests.cs          # NEW: bUnit tests
        ├── WikiSidebarTests.cs              # NEW: bUnit tests
        └── WikiPageContentTests.cs          # NEW: bUnit tests
```

**Structure Decision**: Extends all existing projects — no new .csproj files. Follows the established three-tier pattern: entities + interfaces in Abstractions, provider implementations in Postgres/SqlServer, services in Rag.Core, controller in ApiService, UI in Web. This matches the DocumentEntity/IDocumentRepository/GenerationService precedent exactly.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| WikiPageRelation join table (vs JSON array) | Enables bidirectional related-page queries, referential integrity on page deletion, and prevents orphaned references | JSON array would require manual consistency checks and cannot enforce FK constraints; deleted pages leave stale IDs |
| Two-phase generation (vs single-call) | TOC phase gives structural control; page phase can use section-scoped RAG context for better relevance | Single-call would hit token limits for 20+ page wikis and produce less coherent section structures |
| Configurable parallel/sequential | Different deployment contexts need different strategies (rate-limited API → sequential; local Ollama → parallel) | Hardcoding either limits deployment flexibility |

---

## Architecture

### Data Model

```
┌──────────────────────┐       ┌──────────────────────────┐
│     WikiEntity       │       │    WikiPageEntity         │
│──────────────────────│       │──────────────────────────│
│ Id: Guid (PK)        │──1:N─▸│ Id: Guid (PK)            │
│ CollectionId: string │       │ WikiId: Guid (FK)         │
│ Name: string         │       │ Title: string             │
│ Description: string? │       │ Content: string (text)    │
│ Status: WikiStatus   │       │ SectionPath: string       │
│ CreatedAt: DateTime  │       │ SortOrder: int            │
│ UpdatedAt: DateTime  │       │ ParentPageId: Guid? (FK)  │
└──────────────────────┘       │ Status: PageStatus        │
                               │ CreatedAt: DateTime       │
                               │ UpdatedAt: DateTime       │
                               └───────────┬──────────────┘
                                           │
                                    ┌──────┴──────┐
                                    │ self-ref FK  │
                                    │ (hierarchy)  │
                                    └─────────────┘

┌──────────────────────────────┐
│     WikiPageRelation         │
│──────────────────────────────│
│ SourcePageId: Guid (FK, PK)  │──▸ WikiPageEntity.Id
│ TargetPageId: Guid (FK, PK)  │──▸ WikiPageEntity.Id
└──────────────────────────────┘
  Composite PK: (SourcePageId, TargetPageId)
  Cascade delete: when either page is deleted, relation rows are removed
```

**WikiStatus enum**: `Generating`, `Complete`, `Partial`, `Error`  
**PageStatus enum**: `OK`, `Error`, `Generating`

**Indexes**:
- `WikiEntity`: Index on `CollectionId` (query wikis by collection)
- `WikiPageEntity`: Index on `WikiId` (query pages by wiki), Index on `SectionPath` (section queries)
- `WikiPageRelation`: Index on `TargetPageId` (reverse lookups — "what links to this page?")

### IWikiRepository Contract

```text
IWikiRepository
├── CreateWikiAsync(WikiEntity wiki, IEnumerable<WikiPageEntity> pages)       → Guid
├── GetWikiByIdAsync(Guid id, bool includePages = true)                       → WikiEntity?
├── GetProjectsAsync(int page, int pageSize)                                  → (IReadOnlyList<WikiSummary> Items, int TotalCount)
├── DeleteWikiAsync(Guid id)                                                  → bool
├── UpdateWikiStatusAsync(Guid id, WikiStatus status, DateTime updatedAt)     → void
│
├── GetPageByIdAsync(Guid wikiId, Guid pageId)                               → WikiPageEntity?
├── AddPageAsync(WikiPageEntity page)                                         → WikiPageEntity
├── UpdatePageAsync(WikiPageEntity page)                                      → bool
├── DeletePageAsync(Guid wikiId, Guid pageId)                                → bool
├── GetPageCountAsync(Guid wikiId)                                           → int
│
├── GetRelatedPagesAsync(Guid pageId)                                        → IReadOnlyList<WikiPageEntity>
├── SetRelatedPagesAsync(Guid pageId, IEnumerable<Guid> targetPageIds)       → void
│
├── ExistsGeneratingAsync(string collectionId, string name)                  → bool
└── UpsertPageAsync(Guid wikiId, WikiPageEntity page)                        → WikiPageEntity
     (used during generation — insert if new, update if retrying a failed page)
```

The implementation in Postgres and SQL Server will be nearly identical (both use EF Core) — the only difference is in the entity configurations (column types, naming conventions). Both register as `IWikiRepository` in their respective `AddPostgresDataLayer` / `AddSqlServerDataLayer` extension methods.

### Service Layer

#### IWikiService (CRUD orchestration)

Thin layer over `IWikiRepository`. Responsibilities:
- Validate incoming requests (name length, required fields)
- Set timestamps (CreatedAt/UpdatedAt)
- Map between DTOs and entities
- No business logic beyond validation and mapping

```text
IWikiService
├── CreateWikiAsync(CreateWikiCommand)           → WikiDto
├── GetWikiByIdAsync(Guid id)                    → WikiDto?
├── GetProjectsAsync(int page, int pageSize)     → PagedResult<WikiSummaryDto>
├── DeleteWikiAsync(Guid id)                     → bool
├── AddPageAsync(Guid wikiId, AddPageCommand)    → WikiPageDto
├── UpdatePageAsync(Guid wikiId, Guid pageId, UpdatePageCommand) → bool
├── DeletePageAsync(Guid wikiId, Guid pageId)    → bool
└── UpdateRelatedPagesAsync(Guid wikiId, Guid pageId, IEnumerable<Guid> relatedIds) → void
```

#### IWikiGenerationService (two-phase pipeline)

The core generation orchestrator. Returns `IAsyncEnumerable<WikiGenerationProgress>` for NDJSON streaming.

```text
IWikiGenerationService
└── GenerateAsync(WikiGenerationRequest request, CancellationToken ct)
      → IAsyncEnumerable<WikiGenerationProgress>
```

**Internal flow**:

```
┌─────────────────────────────────────────────────────────────────┐
│                    GenerateAsync Pipeline                        │
│                                                                 │
│  1. Guard: ExistsGeneratingAsync(collectionId, name) → 409     │
│  2. Create wiki shell (Status: Generating) → persist            │
│  yield: { event: "wiki_created", wikiId }                       │
│                                                                 │
│  ╔═══════════════════════════════════════════════════════╗       │
│  ║ PHASE 1: TOC Generation                              ║       │
│  ║                                                      ║       │
│  ║  a. Query IVectorStore: embed collection name,       ║       │
│  ║     retrieve top-K document summaries (titles,       ║       │
│  ║     first 200 chars) for the TOC prompt context      ║       │
│  ║  b. Build TOC prompt: "Given these documents,        ║       │
│  ║     produce a JSON TOC with sections and page        ║       │
│  ║     titles"                                          ║       │
│  ║  c. Call IGenerationService → collect full response   ║       │
│  ║  d. Parse JSON → List<TocEntry> (sectionPath,       ║       │
│  ║     pageTitle, keywords for RAG retrieval)           ║       │
│  ║  e. Persist empty WikiPageEntity rows (Status:       ║       │
│  ║     Generating)                                      ║       │
│  ╚═══════════════════════════════════════════════════════╝       │
│  yield: { event: "toc_complete", totalPages, sections[] }       │
│                                                                 │
│  ╔═══════════════════════════════════════════════════════╗       │
│  ║ PHASE 2: Page Content Generation                     ║       │
│  ║                                                      ║       │
│  ║  Mode: Sequential OR Parallel (from config)          ║       │
│  ║                                                      ║       │
│  ║  For each TocEntry:                                  ║       │
│  ║    a. Query IVectorStore: embed (pageTitle +         ║       │
│  ║       sectionPath + keywords) → top-K doc chunks     ║       │
│  ║    b. Build page prompt with:                        ║       │
│  ║       - Document context chunks                      ║       │
│  ║       - Full TOC (so LLM knows sibling pages)        ║       │
│  ║       - Instruction to tag related page titles       ║       │
│  ║    c. Call IGenerationService → stream tokens        ║       │
│  ║    d. On completion: parse content + related pages,  ║       │
│  ║       update WikiPageEntity (Content, Status: OK),   ║       │
│  ║       insert WikiPageRelation rows                   ║       │
│  ║    e. On error: set page Status: Error, continue     ║       │
│  ║                                                      ║       │
│  ║  yield per page:                                     ║       │
│  ║    { event: "page_start", pageIndex, pageTitle }     ║       │
│  ║    { event: "page_token", pageIndex, text }*         ║       │
│  ║    { event: "page_complete", pageIndex }             ║       │
│  ║    OR { event: "page_error", pageIndex, error }      ║       │
│  ╚═══════════════════════════════════════════════════════╝       │
│                                                                 │
│  3. Update wiki Status: Complete | Partial (if any errors)      │
│  yield: { event: "generation_complete", wikiId, status }        │
└─────────────────────────────────────────────────────────────────┘
```

**Parallel mode details**:
- Uses `SemaphoreSlim` with configurable max concurrency (default: 3, configurable via `Wiki:MaxParallelPages` in appsettings)
- Each page generation task yields progress events through a shared `Channel<WikiGenerationProgress>`
- Pages are assigned monotonic indices so progress events can be ordered by the UI regardless of completion order
- Cancellation propagated to all in-flight tasks via linked `CancellationTokenSource`

**Sequential mode details**:
- Simple `foreach` over TOC entries
- Each page's related-page inference benefits from knowing which pages have been generated so far (richer context)
- Default for rate-limited remote providers

**Configuration** (in `appsettings.json`):
```json
{
  "Wiki": {
    "Generation": {
      "Mode": "sequential",        // "sequential" | "parallel"
      "MaxParallelPages": 3,       // only used in parallel mode
      "MaxTocRetries": 2,          // retries for TOC generation parse failures
      "PageTokenLimit": 4000       // max tokens per page generation call
    }
  }
}
```

#### IWikiExportService (pure function)

Stateless service — takes wiki data, produces a stream. No DB calls.

```text
IWikiExportService
├── ExportAsMarkdownAsync(WikiEntity wiki, IReadOnlyList<WikiPageEntity> pages,
│     IDictionary<Guid, IReadOnlyList<WikiPageEntity>> relatedPages, Stream output)
└── ExportAsJsonAsync(WikiEntity wiki, IReadOnlyList<WikiPageEntity> pages,
      IDictionary<Guid, IReadOnlyList<WikiPageEntity>> relatedPages, Stream output)
```

**Markdown format**:
```markdown
# {Wiki.Name}

{Wiki.Description}

## Table of Contents

- [Section/PageTitle](#section-pagetitle)
- ...

---

## Section/PageTitle

{Page.Content}

### Related Pages
- [Other Page Title](#other-page-title)
- [Deleted Page](#) *(page removed)*

---

## Next Page...
```

**JSON format**:
```json
{
  "metadata": {
    "name": "...",
    "description": "...",
    "exportDate": "2026-03-01T...",
    "pageCount": 12
  },
  "pages": [
    {
      "title": "...",
      "section": "Architecture/Data Model",
      "sortOrder": 1,
      "content": "...",
      "relatedPages": ["Other Page Title", "Another Page"]
    }
  ]
}
```

For wikis with 100+ pages, export writes directly to the response stream using `Utf8JsonWriter` (JSON) or `StreamWriter` (Markdown) to avoid buffering the entire output in memory.

### API Layer — WikiController

Single controller at `/api/wiki` following existing `[ApiController]` conventions:

| Endpoint | HTTP | Request | Response | Notes |
|----------|------|---------|----------|-------|
| `/api/wiki` | POST | `CreateWikiRequest` | 201 + `WikiResponse` | Creates wiki with pages |
| `/api/wiki/{id}` | GET | — | 200 + `WikiResponse` | Full wiki with pages |
| `/api/wiki/{id}` | DELETE | — | 204 | Cascade-deletes pages + relations |
| `/api/wiki/projects` | GET | `?page=1&pageSize=20` | 200 + `PagedResult<WikiSummaryResponse>` | Paginated list |
| `/api/wiki/{id}/pages/{pageId}` | GET | — | 200 + `WikiPageResponse` | Single page with related pages |
| `/api/wiki/{id}/pages/{pageId}` | PUT | `UpdateWikiPageRequest` | 200 + `WikiPageResponse` | Update page fields |
| `/api/wiki/{id}/pages` | POST | `CreateWikiPageRequest` | 201 + `WikiPageResponse` | Add page to wiki |
| `/api/wiki/{id}/pages/{pageId}` | DELETE | — | 204 | Remove page |
| `/api/wiki/export` | POST | `WikiExportRequest` | 200 + file download | `Content-Disposition: attachment` |
| `/api/wiki/generate` | POST | `GenerateWikiRequest` | 200 + NDJSON stream | `application/x-ndjson` |

**Validation** (via `[ApiController]` auto-validation + FluentValidation or DataAnnotations):
- `CreateWikiRequest.Name`: Required, max 200 chars
- `CreateWikiRequest.CollectionId`: Required
- `CreateWikiRequest.Pages`: At least one page with Title + Content
- `GenerateWikiRequest`: CollectionId + Name required
- `WikiExportRequest.Format`: Must be "markdown" or "json"

**Error responses** follow existing `ProblemDetails` pattern:
- 400: Validation errors (missing fields, invalid format)
- 404: Wiki or page not found
- 409: Concurrent generation in progress for same collection+name

**Streaming endpoint** (`/api/wiki/generate`):
- Sets `Content-Type: application/x-ndjson`
- `await foreach` over `IWikiGenerationService.GenerateAsync()`
- Each `WikiGenerationProgress` serialized as one JSON line + `\n` + flush
- Catches `OperationCanceledException` → emits final `generation_cancelled` event
- Follows exact same pattern as `GenerationController.StreamGenerationAsync`

### Blazor UI — Separate Routed Pages

Three page-level routes for deep-linking and browser history:

#### `/wiki` — WikiProjects.razor

- Calls `WikiApiClient.GetProjectsAsync(page, pageSize)`
- Renders `WikiProjectList` component (MudTable with sorting)
- "Generate Wiki" button opens a dialog to select collection + enter name
- Empty state: MudAlert with guidance text
- On project click: `NavigationManager.NavigateTo($"/wiki/{id}")`

#### `/wiki/{Id}` — WikiViewer.razor

- Calls `WikiApiClient.GetWikiByIdAsync(id)` on init
- Layout: `MudGrid` — Col 3 sidebar, Col 9 content
- Sidebar: `WikiSidebar` component using `MudTreeView<WikiPageNode>` built from page SectionPath hierarchy
- Content area: `WikiPageContent` renders selected page Markdown via Markdig pipeline (shared with ChatMessage.razor)
- Default selection: first page in sort order
- On page click in sidebar: updates content area (no navigation, just component state)
- Direct page URL support: if loaded with `/{Id}?page={pageId}`, auto-selects that page
- Export button: calls export endpoint, triggers browser download
- "Generate" indicator: if wiki status is `Generating`, shows `WikiGenerationProgress` component instead of content

#### `/wiki/{WikiId}/pages/{PageId}` — WikiPage.razor

- Redirects to `/wiki/{WikiId}?page={PageId}` for consistent UX
- Provides shareable deep links to specific pages
- Also serves as API-accessible page URL for documentation tools

#### WikiApiClient.cs

Typed HttpClient registered via Aspire service discovery:

```text
WikiApiClient (injected as scoped)
├── GetProjectsAsync(int page, int pageSize)        → PagedResult<WikiSummaryResponse>
├── GetWikiByIdAsync(Guid id)                       → WikiResponse
├── DeleteWikiAsync(Guid id)                        → bool
├── CreateWikiAsync(CreateWikiRequest)               → WikiResponse
├── AddPageAsync(Guid wikiId, CreateWikiPageRequest) → WikiPageResponse
├── UpdatePageAsync(Guid wikiId, Guid pageId, UpdateWikiPageRequest) → WikiPageResponse
├── DeletePageAsync(Guid wikiId, Guid pageId)       → bool
├── ExportWikiAsync(WikiExportRequest)               → Stream (file download)
└── GenerateWikiAsync(GenerateWikiRequest)           → IAsyncEnumerable<WikiGenerationProgress>
     (uses HttpClient.GetStreamAsync + NdjsonStreamReader pattern from existing code)
```

Registration in Web `Program.cs`:
```text
builder.Services.AddHttpClient<WikiApiClient>(c => c.BaseAddress = new("https+http://apiservice"));
```

#### WikiSidebar — Tree Building Algorithm

Converts flat `WikiPageEntity[]` into a tree using `SectionPath`:

```
Input pages:
  - "Architecture/Data Model"     sortOrder: 1
  - "Architecture/Services"       sortOrder: 2
  - "API Reference/Endpoints"     sortOrder: 3
  - "API Reference/DTOs"          sortOrder: 4
  - "Getting Started"             sortOrder: 5

Output tree:
  ▸ Architecture
    ├── Data Model          (clickable → page)
    └── Services            (clickable → page)
  ▸ API Reference
    ├── Endpoints           (clickable → page)
    └── DTOs                (clickable → page)
  Getting Started           (clickable → page, no parent section)
```

Sections without direct page content are non-clickable folder nodes. MudTreeView with `MudTreeViewItem` provides expand/collapse and selection highlighting.

### DI Registration

**ApiService `Program.cs`** additions:
```text
// Wiki services (after existing service registrations)
builder.Services.AddScoped<IWikiService, WikiService>();
builder.Services.AddScoped<IWikiGenerationService, WikiGenerationOrchestrator>();
builder.Services.AddSingleton<IWikiExportService, WikiExportService>();
builder.Services.Configure<WikiGenerationOptions>(builder.Configuration.GetSection("Wiki:Generation"));
```

**Postgres `AddPostgresDataLayer`** addition:
```text
services.AddScoped<IWikiRepository, PostgresWikiRepository>();
```

**SqlServer `AddSqlServerDataLayer`** addition:
```text
services.AddScoped<IWikiRepository, SqlServerWikiRepository>();
```

**Both DbContexts** gain:
```text
public DbSet<WikiEntity> Wikis { get; set; }
public DbSet<WikiPageEntity> WikiPages { get; set; }
public DbSet<WikiPageRelation> WikiPageRelations { get; set; }
```

### Prompt Templates

Two prompt templates for wiki generation (stored as embedded resources or in a prompts folder):

**TOC Prompt** (`wiki-toc-prompt.txt`):
```
You are a technical documentation expert. Given the following document summaries from a collection, produce a structured table of contents for a wiki.

Documents:
{document_summaries}

Requirements:
- Organize into logical sections (2-5 top-level sections)
- Each section should have 1-5 pages
- Total pages should be between 5 and {max_pages}
- Each page should cover a distinct topic
- Include keywords for each page (used to find relevant source documents)

Output ONLY valid JSON in this exact format:
{
  "sections": [
    {
      "sectionPath": "Architecture",
      "pages": [
        { "title": "Data Model", "keywords": ["entity", "database", "schema"] },
        { "title": "Services", "keywords": ["service", "dependency injection"] }
      ]
    }
  ]
}
```

**Page Content Prompt** (`wiki-page-prompt.txt`):
```
You are a technical documentation writer. Write a wiki page based on the source documents provided.

Wiki: {wiki_name}
Section: {section_path}
Page Title: {page_title}
Full Table of Contents: {toc_json}

Source Documents:
{document_chunks}

Requirements:
- Write clear, well-structured Markdown content
- Use headings (###, ####) to organize subsections within the page
- Include code examples if relevant source documents contain code
- Length: 500-2000 words
- At the end, list 1-3 related page titles from the TOC that are topically connected to this page

Output format:
Write the page content in Markdown first, then on a new line output:
RELATED_PAGES: ["Page Title 1", "Page Title 2"]
```

### Observability

New counters (registered via `IMeterFactory` following existing pattern):
- `deepwiki.wiki.created` — Counter: wikis created
- `deepwiki.wiki.pages_generated` — Counter: pages generated (tag: status=ok|error)
- `deepwiki.wiki.generation_duration_seconds` — Histogram: total generation time
- `deepwiki.wiki.export_count` — Counter: exports (tag: format=markdown|json)

---

## Phase Completion Report

### 📋 Phase 0: Research & Discovery (TODO)

**Planned research topics**:
1. LLM prompt strategies for TOC generation (structured JSON output reliability)
2. Parallel generation patterns with rate-limit awareness
3. MudTreeView usage patterns for hierarchical navigation
4. Markdown export with internal anchor links
5. Large wiki export streaming strategies

### 📋 Phase 1: Design & Contracts (TODO)

**Planned deliverables**:
- `data-model.md` — Full entity model, DTO definitions, state machine diagrams
- `quickstart.md` — User guide for wiki system
- `contracts/` — JSON schemas for all wiki API endpoints

### 📋 Phase 2: Tasks (TODO)

**Ready for `/speckit.tasks`** after Phase 0 and Phase 1 are complete.

**Suggested task grouping** (high-level, refined during Phase 2):
1. **Task Group 1 — Data Layer** (P1): Entities, configurations, migrations, IWikiRepository, both provider implementations, repository unit tests
2. **Task Group 2 — Service Layer** (P1): IWikiService + WikiService, CRUD unit tests
3. **Task Group 3 — API Controller** (P1): WikiController, DTOs, validation, controller tests
4. **Task Group 4 — Export** (P2): IWikiExportService, Markdown/JSON formatters, export format tests
5. **Task Group 5 — Generation Orchestrator** (P3): IWikiGenerationService, two-phase pipeline, prompt templates, configurable parallel/sequential, progress streaming, orchestration tests with mock provider
6. **Task Group 6 — Blazor UI** (P1-P2): WikiProjects page, WikiViewer page, WikiPage redirect, WikiSidebar, WikiPageContent, WikiGenerationProgress, WikiApiClient, NavMenu link, bUnit tests
7. **Task Group 7 — Integration** (P3): End-to-end generation with real collection, DI wiring, migration smoke test

---

## Summary

**Branch**: `005-wiki-system`  
**Spec Location**: `/home/mark/docker/deepwiki-open-dotnet/specs/005-wiki-system/spec.md`  
**Plan Location**: `/home/mark/docker/deepwiki-open-dotnet/specs/005-wiki-system/plan.md`

**Key Architectural Decisions**:
- Two-phase generation (TOC → pages) with configurable sequential/parallel execution
- WikiPageRelation join table for related pages (referential integrity, bidirectional queries)
- Separate Blazor routes (`/wiki`, `/wiki/{id}`, `/wiki/{id}/pages/{pageId}`) for deep-linking
- Three services with clear boundaries: CRUD (WikiService), Generation (WikiGenerationOrchestrator), Export (WikiExportService)
- Extends existing projects — no new .csproj files
- Reuses existing streaming infrastructure (NDJSON, IAsyncEnumerable, NdjsonStreamReader)

**Constitution Status**: ✅ PASS (1 item deferred: WCAG 2.1 AA accessibility polish)

**New Files**: ~40 new files across 8 projects  
**Modified Files**: ~5 (2 DbContexts, 2 DI registrations, NavMenu, Program.cs additions)

**Readiness**: ✅ Ready for Phase 0 research, then Phase 1 design, then `/speckit.tasks`
