# Feature Specification: DeepWiki Wiki System

**Feature Branch**: `005-wiki-system`  
**Created**: 2026-03-01  
**Status**: Draft  
**Input**: User description: "DeepWiki Wiki System — Enable users to generate, browse, and export structured wiki documentation from ingested document collections, with full CRUD lifecycle and Markdown/JSON export"

## Clarifications

### Session 2026-03-01

- Q: Should wikis be keyed by collection ID (one wiki per collection, replace-on-regenerate) or should users be able to create multiple named wikis from the same collection? → A: Multiple named wikis per collection — no uniqueness constraint on CollectionId; users name each wiki; project list shows all.
- Q: When generating a wiki from a collection, should the section/page hierarchy be LLM-determined, user-defined, or hybrid (LLM proposes, user confirms)? → A: LLM-determined with post-generation editing — the LLM proposes the TOC and generates pages automatically (no confirmation gate); the user can then edit the resulting structure (rename, reorder, add, remove sections/pages).
- Q: Should 'related pages' links be automatically inferred during generation, manually curated by users, or both? → A: Auto-inferred during generation — the LLM tags related pages as part of each page's generation output (based on topical overlap); users can edit links afterwards.
- Q: Should the system support granular page-level updates or only full-wiki replacement for post-generation editing? → A: Granular page-level endpoints — PUT for updating a single page, POST for adding a page, DELETE for removing a page, all scoped under the wiki resource.
- Q: Should wiki generation use a single LLM call or a two-phase approach (TOC then pages)? → A: Two-phase — Phase 1 generates a structured TOC (sections + page titles); Phase 2 generates page content per TOC entry. This avoids token-limit issues and gives structural control.
- Q: Should page generation run sequentially or in parallel? → A: Configurable — support both sequential and parallel page generation via a configuration option. Progress communication is the priority regardless of mode.
- Q: Should related-page links use a JSON array on the page entity or a proper join table? → A: Proper join table — a WikiPageRelation entity (SourcePageId, TargetPageId) to enable bidirectional queries and referential integrity.
- Q: Should the wiki UI use a single `/wiki` page with component switching or separate routes? → A: Separate routes — `/wiki` (project list), `/wiki/{id}` (wiki viewer), `/wiki/{id}/pages/{pageId}` (direct page link) for deep-linking and browser history support.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Wiki CRUD via REST API (Priority: P1)

An internal developer or automated pipeline creates, retrieves, updates, and deletes wiki structures and their child pages through a set of REST endpoints. The wiki is persisted in the database so it survives restarts and can be queried by any client.

**Why this priority**: Without the data model and CRUD endpoints, no other feature (browsing, export, generation) can function. This is the foundational slice.

**Independent Test**: Can be fully tested by sending HTTP requests (POST, GET, DELETE) to the wiki endpoints and verifying the database state. Delivers a usable API for any client.

**Acceptance Scenarios**:

1. **Given** no wiki exists for collection "my-repo", **When** the user POSTs a valid wiki structure (name, collectionId, pages with title/content/sectionPath), **Then** the system returns 201 with the new wiki ID, and the wiki and all pages are persisted in the database.
2. **Given** a wiki with ID "abc-123" exists with 10 pages, **When** the user GETs `/api/wiki/abc-123`, **Then** the system returns the wiki structure including metadata, pages, sections, and rootSections within 200ms.
3. **Given** a wiki with ID "abc-123" exists, **When** the user DELETEs `/api/wiki/abc-123`, **Then** the system removes the wiki and all child pages from the database and returns 204.
4. **Given** a wiki POST request is missing required fields (e.g., no name), **When** the request is submitted, **Then** the system returns 400 with a validation error describing the missing fields.
5. **Given** the user GETs `/api/wiki/nonexistent-id`, **When** the wiki does not exist, **Then** the system returns 404.
6. **Given** a wiki with ID "abc-123" exists, **When** the user PUTs `/api/wiki/abc-123/pages/{pageId}` with an updated title and content, **Then** the page is updated and the wiki's last-modified timestamp is refreshed.
7. **Given** a wiki with ID "abc-123" exists, **When** the user POSTs `/api/wiki/abc-123/pages` with a new page (title, content, sectionPath), **Then** the page is added to the wiki and the response returns 201 with the new page ID.
8. **Given** a wiki page exists, **When** the user DELETEs `/api/wiki/abc-123/pages/{pageId}`, **Then** the page is removed and the wiki's page count decreases by one.

---

### User Story 2 - Browse Wiki Projects (Priority: P1)

An internal user navigates to the Wiki section of the application and sees a list of all available wiki projects. They can see each project's name, source collection, page count, and last-modified date. Clicking a project opens the wiki viewer.

**Why this priority**: Discoverability is critical — users must find existing wikis before they can read or export them. This is the entry point for all wiki interaction in the UI.

**Independent Test**: Can be tested by verifying the `/api/wiki/projects` endpoint returns correct paginated results and the Blazor component renders the list correctly (bunit test).

**Acceptance Scenarios**:

1. **Given** three wiki projects exist in the database, **When** the user visits the `/wiki` page, **Then** the project list displays all three wikis with name, collection source, page count, and last-modified date.
2. **Given** more than 20 wiki projects exist, **When** the user visits the `/wiki` page, **Then** the list is paginated with navigation controls (next/previous/page number).
3. **Given** no wiki projects exist, **When** the user visits the `/wiki` page, **Then** a friendly empty-state message is displayed with guidance on how to generate a wiki.
4. **Given** the user calls `GET /api/wiki/projects`, **Then** the response includes a paginated array of wiki summaries with `id`, `name`, `collectionSource`, `pageCount`, and `lastModified` fields.

---

### User Story 3 - Read Wiki Content with Section Navigation (Priority: P2)

An internal user clicks on a wiki project and is presented with a sidebar showing sections and pages. Clicking a page displays the full Markdown-rendered content in the main area. Related pages are shown as navigable links.

**Why this priority**: Reading wiki content is the primary consumption experience. Once wikis exist (P1), users need an intuitive way to navigate and read them.

**Independent Test**: Can be tested with bunit component tests that render the wiki viewer with mock data and verify sidebar navigation, content rendering, and related-page links.

**Acceptance Scenarios**:

1. **Given** a wiki with sections "Architecture" and "API Reference" exists, **When** the user opens the wiki, **Then** the sidebar displays the section hierarchy with expandable nodes.
2. **Given** the user clicks a page titled "Data Model" in the sidebar, **Then** the main area renders the page content as formatted Markdown (headings, code blocks, lists, tables).
3. **Given** a page has related pages defined, **When** the user views that page, **Then** related-page links appear below the content and navigate to the correct pages when clicked.
4. **Given** the wiki has nested sections (e.g., "API Reference > Endpoints > POST /wiki"), **When** the sidebar renders, **Then** the hierarchy is displayed correctly with indentation and expand/collapse controls.

---

### User Story 4 - Export Wiki as Markdown or JSON (Priority: P2)

An internal user selects a wiki project and exports it as a single downloadable file — either Markdown (with a table of contents) or structured JSON. The export is useful for offline reading, sharing, or importing into other documentation systems.

**Why this priority**: Export enables wikis to leave the application, making them portable and shareable. It builds on the existing wiki data (P1) and provides significant standalone value.

**Independent Test**: Can be tested by calling the export endpoint with a known wiki and validating the output format (Markdown TOC structure, JSON schema) against expected output.

**Acceptance Scenarios**:

1. **Given** a wiki with 5 pages across 2 sections exists, **When** the user POSTs `/api/wiki/export` with format "markdown" and the wiki ID, **Then** the response contains a downloadable `.md` file with a `Content-Disposition` header, a table of contents linking to each page heading, and each page's full content under `##` headings.
2. **Given** the same wiki, **When** the user POSTs `/api/wiki/export` with format "json", **Then** the response contains a downloadable `.json` file with a structure containing `metadata` (name, description, exportDate) and `pages` array (each with `title`, `content`, `section`, `relatedPages`).
3. **Given** a wiki has pages with related-page references, **When** exported as Markdown, **Then** each page section includes a "Related Pages" list with links to other page headings in the same document.
4. **Given** the export request references a non-existent wiki, **Then** the system returns 404.

---

### User Story 5 - Generate Wiki from a Collection (Priority: P3)

An internal user selects a document collection and triggers wiki generation. The system retrieves the collection's documents, uses the LLM to determine a logical section/page structure and generate content for each page automatically (no confirmation step), and streams progress to the UI. The resulting wiki is automatically saved. After generation, the user can edit the structure — renaming sections, reordering pages, adding new pages, or removing pages.

**Why this priority**: Generation is the highest-value feature but depends on the CRUD layer (P1) and benefits from the browsing UI (P2). It also requires integration with the existing generation pipeline, making it more complex.

**Independent Test**: Can be tested with a mock generation service and a test collection — verify that the orchestration creates the correct number of pages, that progress is streamed, and that the final wiki is persisted.

**Acceptance Scenarios**:

1. **Given** a collection "my-docs" with 15 ingested documents, **When** the user triggers wiki generation for that collection, **Then** the system creates a wiki with a logical section structure and one or more pages per section, each containing LLM-generated content summarising the relevant documents.
2. **Given** wiki generation is in progress, **When** the UI is displaying the generation view, **Then** the user sees progress indicators (e.g., "Generating page 3 of 12: API Reference") updating in real time via streaming.
3. **Given** an LLM generation call fails for a specific page, **When** the error occurs, **Then** that page is marked with an error status, the remaining pages continue generating, and the user is notified of the partial failure.
4. **Given** wiki generation completes successfully, **When** the user navigates to the wiki project list, **Then** the newly generated wiki appears with the correct page count and timestamp.
5. **Given** the user cancels wiki generation while in progress, **Then** already-generated pages are saved, generation stops, and the wiki is marked as partially complete.
6. **Given** wiki generation has completed, **When** the user views the wiki, **Then** they can rename sections, reorder pages, add new pages, and remove pages — all changes are persisted immediately.

---

### Edge Cases

- What happens when a wiki export is requested for a wiki with zero pages? The system returns an empty document with only metadata/headers and a note that no pages exist.
- How does the system handle concurrent wiki generation requests for the same collection and wiki name? The system rejects a second generation request with 409 Conflict if one is already in progress for that collection+name combination. Generating a differently-named wiki from the same collection is allowed concurrently.
- What happens if the database connection is lost during wiki creation? Standard transaction semantics apply — the operation rolls back and the client receives a 500 error with a retry-friendly message.
- How does the system handle extremely large wikis (500+ pages) during export? Export streams the file to the response rather than buffering in memory, preventing out-of-memory errors.
- What happens when a page references a related page that has been deleted? The `WikiPageRelation` row is removed by cascade delete when the target page is deleted, so a fresh API response will never include a stale related-page reference. The "(page removed)" UI state is a guard for **stale cached data only** — e.g., a `WikiResponse` loaded before the deletion occurred. The UI component (WikiPageContent) MUST handle this gracefully by checking whether a related-page ID still exists in the loaded wiki's page list; if not, render the link label as "(page removed)" and disable navigation. Tests for this scenario MUST use a manually constructed stale response object rather than relying on the live API.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST persist wiki structures using a wiki entity (Id, CollectionId, Name, Description, Status (WikiStatus), CreatedAt (DateTime), UpdatedAt (DateTime)), a wiki page entity (Id, WikiId, Title, Content, SectionPath, SortOrder, ParentPageId, Status (PageStatus), CreatedAt (DateTime), UpdatedAt (DateTime)), and a wiki page relation join table (SourcePageId, TargetPageId) for related-page links, with support for both configured database providers.
- **FR-002**: System MUST expose a `POST /api/wiki` endpoint that accepts a wiki structure with pages, validates required fields (Name, at least one page with Title and Content), and returns 201 with the created wiki ID.
- **FR-003**: System MUST expose a `GET /api/wiki/{id}` endpoint that returns the full wiki structure including pages, sections, and rootSections. Response time SHOULD be under 200ms for wikis with fewer than 100 pages on a warmed local database (see SC-002 for the measurable success criterion).
- **FR-004**: System MUST expose a `DELETE /api/wiki/{id}` endpoint that removes the wiki and all child pages, returning 204 on success or 404 if the wiki does not exist.
- **FR-005**: System MUST expose a `GET /api/wiki/projects` endpoint that returns a paginated list of wiki project summaries (id, name, collectionSource, pageCount, lastModified) with configurable page size (default 20).
- **FR-006**: System MUST expose a `POST /api/wiki/export` endpoint that accepts a wiki ID and format (markdown or json) and returns a downloadable file with a `Content-Disposition` header.
- **FR-007**: Markdown export MUST include a table of contents at the top with anchor links, each page rendered under a `##` heading, and a "Related Pages" section per page listing links to related page headings. Related pages are stored in a join table (WikiPageRelation), auto-inferred by the LLM during generation, and editable by users afterwards.
- **FR-008**: JSON export MUST include a `metadata` object (name, description, exportDate, pageCount) and a `pages` array where each element contains `title`, `content`, `section`, `sortOrder`, and `relatedPages`.
- **FR-009**: System MUST provide a browsable wiki page at `/wiki` that displays the project list, wiki viewer with sidebar navigation, and Markdown-rendered page content.
- **FR-010**: System MUST provide a wiki generation capability that, given a collection ID, retrieves collection documents, uses the LLM to determine a section structure and generate page content automatically (no user confirmation gate), and persists the resulting wiki. Users MUST be able to edit the generated structure after generation (rename, reorder, add, remove sections and pages).
- **FR-011**: Wiki generation MUST stream progress events to the UI (page-by-page status updates) using the existing streaming infrastructure.
- **FR-012**: System MUST reject concurrent wiki generation requests that target the same collection and wiki name with 409 Conflict.
- **FR-013**: System MUST handle partial generation failures gracefully — failed pages are marked with an error status while remaining pages continue generating.
- **FR-014**: System MUST support cancellation of in-progress wiki generation, saving already-generated pages and marking the wiki as partially complete. Pages whose content generation was not yet complete at the time of cancellation MUST be discarded (Status remains Error or Generating); only pages with Status=OK are retained in the final wiki.
- **FR-015**: System MUST validate all incoming wiki data — missing required fields return 400 with descriptive error messages.
- **FR-016**: System MUST expose a `PUT /api/wiki/{id}/pages/{pageId}` endpoint that updates a single page's title, content, sectionPath, sortOrder, or relatedPages, and refreshes the wiki's last-modified timestamp. When `relatedPageIds` is included in the request, it MUST fully replace the existing relation set (delete-all-insert-new semantics via `SetRelatedPagesAsync`).
- **FR-017**: System MUST expose a `POST /api/wiki/{id}/pages` endpoint that adds a new page to an existing wiki, validates required fields (Title, Content), and returns 201 with the new page ID.
- **FR-018**: System MUST expose a `DELETE /api/wiki/{id}/pages/{pageId}` endpoint that removes a single page from a wiki, returning 204 on success or 404 if the page or wiki does not exist.
- **FR-019**: System MUST expose a `GET /api/wiki/{id}/pages/{pageId}` endpoint that returns the full page detail (title, content, sectionPath, sortOrder, status, relatedPages) with 200, or 404 if the page or wiki does not exist.
- **FR-020**: System MUST expose a `PUT /api/wiki/{id}` endpoint that accepts a `description` field (string, optional) and updates the wiki's description and `UpdatedAt` timestamp, returning 200 with the updated wiki summary or 404 if the wiki does not exist. Wiki `Name` is immutable after creation — requests that attempt to change it MUST return 400.

### Key Entities

- **Wiki**: Represents a complete wiki project. Key attributes: unique identifier, link to the source collection, display name, description, creation and last-modified timestamps. A wiki has many pages.
- **Wiki Page**: Represents a single page within a wiki. Key attributes: unique identifier, parent wiki reference, title, Markdown content body, section path (e.g., "Architecture/Data Model"), sort order for display sequencing, optional parent page reference for hierarchical nesting. A page belongs to one wiki and may reference related pages.
- **Collection** (existing): The source of documents from which a wiki is generated. A collection may have zero or many associated wikis (no uniqueness constraint).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can create a wiki with up to 50 pages in under 2 seconds (typical database latency).
- **SC-002**: Users can retrieve a wiki with up to 100 pages (full structure with content) in under 200ms.
- **SC-003**: Users can export a 50-page wiki as Markdown or JSON and receive the downloadable file in under 3 seconds.
- **SC-004**: Wiki project list loads within 500ms for up to 100 projects and displays correct page counts and timestamps.
- **SC-005**: Wiki generation from a 20-document collection produces a structured wiki within 5 minutes (dependent on LLM response time), with progress visible in the UI throughout.
- **SC-006**: 100% of wiki CRUD operations are covered by unit tests, with export format correctness validated against expected Markdown TOC structure and JSON schema.
- **SC-007**: Browsable wiki components (project list, page viewer, section navigation) have component test coverage for primary rendering and interaction flows.
- **SC-008**: Users can navigate between wiki pages and sections entirely via the sidebar without page reloads, with content rendering in under 300ms.

## Assumptions

- The existing dual-provider database setup will accommodate the new wiki and wiki page tables without architectural changes — a standard migration is sufficient.
- The existing streaming generation pipeline can be reused for wiki page generation by providing section-specific context and a wiki-generation prompt template.
- Tree-view components are available for rendering the section/page sidebar hierarchy.
- The existing Markdown rendering library handles all required formatting including code blocks, tables, and math expressions.
- Pagination defaults (page size 20) follow existing patterns in the application.
- Wiki generation prompt templates will be added to the existing prompt template system (or as embedded resources for MVP).
- The "related pages" relationship is auto-inferred by the LLM during generation (included in the generation prompt so the model tags topically related pages). Stored in a WikiPageRelation join table (SourcePageId, TargetPageId) for referential integrity and bidirectional queries. Users can edit these links post-generation.
- No authentication is required for MVP — the system is internal only.
- Wiki `Name` is immutable after creation (enforced by FR-020). `Description` is the only wiki-level field that may be updated post-creation via `PUT /api/wiki/{id}`.
