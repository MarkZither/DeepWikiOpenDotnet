# Implementation Readiness Checklist: DeepWiki Wiki System

**Purpose**: Validates that requirements across all 5 domains (data layer, API, LLM generation, Blazor UI, non-functional) are sufficiently complete, clear, consistent, and measurable to implement and test without hitting specification blockers. Use ongoing throughout all 8 phases — return to the relevant category before starting each phase.
**Created**: 2026-03-01
**Feature**: [spec.md](../spec.md) | [plan.md](../plan.md) | [tasks.md](../tasks.md)

---

## Data Model & EF Core Requirements

- [ ] CHK001 — Are optimistic concurrency requirements (e.g., `UpdatedAt` as concurrency token) specified for `WikiEntity` and `WikiPageEntity`? The constitution §VII requires concurrency handling but FR-001 omits it. [Completeness, Gap]
- [ ] CHK002 — Is the migration "verified" criterion defined precisely — what constitutes passing migration up+down verification for T011 and T017? [Measurability, Spec T011/T017]
- [ ] CHK003 — Are `WikiPageRelation` cascade delete semantics fully specified for concurrent deletion of both `SourcePage` and `TargetPage` in the same operation? [Edge Case, Gap]
- [ ] CHK004 — Is the Testcontainers provider parity requirement measurable — are the specific test cases explicitly enumerated so both T021a and T021b can be verified as covering identical scenarios? [Measurability, Spec §FR-001]
- [ ] CHK005 — Are `SectionPath` index requirements documented with the query patterns they support (e.g., equality lookup, prefix scan, grouping by section)? [Clarity, Spec §FR-001]
- [ ] CHK006 — Is the `rootSections` field referenced in FR-003 defined — what data shape is returned and how is it derived (pages with no `ParentPageId`? pages with a single-segment `SectionPath`?)? [Clarity, Spec §FR-003, Ambiguity]
- [ ] CHK007 — Is a maximum page size defined for `GET /api/wiki/projects` pagination — can a caller request all projects in one page, and if not, what is the cap? [Completeness, Spec §FR-005]
- [ ] CHK008 — Is the SC-002 latency target (<200ms GET wiki) specified with an explicit infrastructure baseline (warmed connection pool, local database), so the performance smoke test in T066b has a reproducible pass condition? [Measurability, Spec §SC-002]

---

## REST API Contract Requirements

- [ ] CHK009 — Is the pagination response envelope format for FR-005 consistent with existing paginated endpoints in the codebase (same `total`, `page`, `pageSize`, `items` shape)? [Consistency, Spec §FR-005]
- [ ] CHK010 — Is the 409 Conflict response body for FR-012 specified — does it include the conflicting wiki's ID or generation start time, or only a human-readable message? [Clarity, Spec §FR-012, Ambiguity]
- [ ] CHK011 — Is `SortOrder` auto-assignment behaviour defined for FR-017 (POST new page) — does the system assign the next sequential value, or is it caller-provided with no default? [Completeness, Spec §FR-017]
- [ ] CHK012 — Is the `collectionSource` field in `WikiSummaryResponse` (FR-005) defined as the raw `CollectionId` value or a display name resolved from the existing collection data model? [Clarity, Spec §FR-005, Ambiguity]
- [ ] CHK013 — Are all error responses (400, 404, 409, 500) specified to use `ProblemDetails` format consistent with existing controllers, or are custom response shapes permitted? [Consistency, Spec §FR-015]
- [ ] CHK014 — Is the `Content-Disposition` filename sanitisation defined for wiki names containing special characters (slashes, quotes, Unicode) in FR-006? [Clarity, Spec §FR-006]
- [ ] CHK015 — Does FR-020 define behaviour when the `PUT /api/wiki/{id}` body contains neither `name` nor `description` — is an empty-body request a no-op 200 or a 400? [Completeness, Spec §FR-020]
- [ ] CHK016 — Are connection timeout or server-sent heartbeat requirements defined for the long-running NDJSON streaming endpoint (FR-011) to prevent proxy/load-balancer timeouts? [Completeness, Spec §FR-011, Gap]

---

## LLM Generation Pipeline Requirements

- [ ] CHK017 — Is the TOC JSON output schema fully defined — exact field names (`sectionPath`, `pages`, `title`, `keywords`), types, and which fields are required vs. optional? [Clarity, Spec §FR-010, Gap]
- [ ] CHK018 — Is the `RELATED_PAGES` parsing contract specified precisely — exact delimiter, format (`["Page A", "Page B"]`), and parser behaviour for malformed or missing output? [Clarity, Spec §FR-010, Gap]
- [ ] CHK019 — Are LLM token limit constraints defined per provider? The `PageTokenLimit` config default of 4000 — is this appropriate for both Ollama (smaller context) and OpenAI-compatible APIs? [Completeness, Spec plan §Wiki:Generation, Gap]
- [ ] CHK020 — Are TOC retry conditions fully specified — which failure modes (parse error, empty response, timeout) trigger a retry up to `MaxTocRetries`, vs. which trigger immediate abort? [Completeness, Spec §FR-013]
- [ ] CHK021 — Is the LLM snapshot retention policy (short-term 30 days, medium-term 365 days, long-term archival) explicitly applied to wiki generation snapshots in the `llm-snapshots/wiki/README.md`? [Completeness, Constitution §LLM Policy, Gap]
- [ ] CHK022 — Is the PII redaction scope defined for wiki generation snapshots — which prompt fields (e.g., document content, wiki name, collection ID) could contain PII requiring redaction? [Completeness, Constitution §V, Gap]
- [ ] CHK023 — Are progress event ordering guarantees defined for parallel mode — can a `page_complete` event for page N arrive before `page_start` for page N+1, and must the UI handle out-of-order events? [Clarity, Spec §FR-011]
- [ ] CHK024 — Is the Agent Framework tool binding interface for `IWikiGenerationService.GenerateAsync` defined with an explicit tool parameter shape (input type, output type, error return contract)? [Completeness, Constitution §VIII, Gap]

---

## Blazor UI & Component Requirements

- [ ] CHK025 — Are loading state requirements defined for `WikiSidebar` and `WikiPageContent` while data is in flight — spinner, skeleton, or disabled state? [Completeness, Spec §FR-009, Gap]
- [ ] CHK026 — Is the wiki export format selection UI interaction defined — is it a dropdown, radio buttons, two separate buttons, or a format inferred from a single action? [Completeness, Spec §FR-006, Gap]
- [ ] CHK027 — Are accessibility requirements specified for the `MudTreeView` sidebar — keyboard navigation (arrow keys, enter/space), ARIA roles, and focus management on page selection? [Coverage, Constitution §Frontend]
- [ ] CHK028 — Is the responsive/mobile layout requirement defined for the `/wiki/{id}` two-column (sidebar + content) layout on smaller viewports? [Completeness, Gap]
- [ ] CHK029 — Is the `WikiViewer` deep-link behaviour defined when `?page={pageId}` references a page that does not belong to the loaded wiki? [Edge Case, Spec §FR-009, Gap]
- [ ] CHK030 — Is the generation cancel button interaction specified — does it disable optimistically on click, or does it wait for a `generation_cancelled` event before disabling? [Clarity, Spec §FR-014]
- [ ] CHK031 — Are `WikiProjectList` column sort requirements defined — is the project list sortable and, if so, by which columns and in which default order? [Completeness, Spec §FR-005, Gap]
- [ ] CHK032 — Is the Markdown rendering scope defined precisely — are any extensions beyond CommonMark expected (math, Mermaid diagrams, syntax highlighting), and are these supported by the configured Markdig pipeline? [Clarity, Spec §Assumptions]

---

## Non-Functional & Success Criteria Requirements

- [ ] CHK033 — Are SC-001 through SC-004 latency targets testable with a defined seeding specification — how many rows, what data size, what index state is required for the T066b smoke test to be reproducible? [Measurability, Spec §SC-001–SC-004]
- [ ] CHK034 — Is SC-008 (content rendering <300ms) measurable without a defined baseline — what is the measurement point: Blazor component init complete, Markdown HTML output rendered, or first contentful paint? [Measurability, Spec §SC-008, Ambiguity]
- [ ] CHK035 — Are OTel metric tag cardinalities bounded — is `wikiId` or any high-cardinality identifier ever used as a metric tag? [Clarity, Spec plan §Observability]
- [ ] CHK036 — Is the `generation_duration_seconds` histogram start time defined — from HTTP request receipt, wiki shell creation (post-guard), or TOC generation start? [Clarity, Spec plan §Observability, Ambiguity]
- [ ] CHK037 — Is the `WikiSnapshotRecorder` failure mode defined for missing output directory — silent no-op, auto-create, or startup exception? [Edge Case, Spec T064a, Gap]
- [ ] CHK038 — Are `Wiki:Generation` configuration validation rules defined — what happens at startup or runtime if `Mode` is unrecognised, `MaxParallelPages` is ≤0, or `PageTokenLimit` exceeds the provider's context window? [Edge Case, Spec plan §Wiki:Generation]

---

## Cross-Cutting & Consistency

- [ ] CHK039 — Is the `WikiStatus` transition graph defined — which status transitions are valid transitions (e.g., `Complete` → `Generating` for re-generation), and which are illegal? [Completeness, Gap]
- [ ] CHK040 — Does US5 acceptance scenario 6 ("rename, reorder, add, remove sections and pages") map completely to FR-016/FR-017/FR-018 — is "rename section" (SectionPath update across multiple pages) covered by a single endpoint or requires multiple calls? [Completeness, Spec §US5, Spec §FR-016]
- [ ] CHK041 — Is `UpdatedAt` refresh behaviour consistent across all wiki mutation endpoints — do FR-016 (page update), FR-017 (page add), and FR-018 (page delete) all refresh the parent wiki's `UpdatedAt` timestamp? [Consistency, Spec §FR-016–FR-018]
- [ ] CHK042 — Are all US1 acceptance scenarios (8 scenarios) covered by at least one FR — is there a scenario testing the `PUT /api/wiki/{id}` description update (FR-020) missing from the US1 scenario list? [Consistency, Spec §US1, Spec §FR-020]
- [ ] CHK043 — Is the `rootSections` concept consistent between FR-003 and the plan's WikiSidebar tree-building algorithm — are root sections always single-segment `SectionPath` values, or all pages at depth 0 of the `ParentPageId` hierarchy? [Consistency, Spec §FR-003, Spec plan §WikiSidebar]
- [ ] CHK044 — Are the user story priority ordering and inter-story dependencies consistent between spec priorities (P1/P2/P3) and the tasks.md phase execution ordering (Phase 3–7)? [Consistency, Spec §US1–US5]

---

## Notes

- Check items off as completed: `[x]`
- Return to the relevant category before starting each phase:
  - Phase 1–2: CHK001–CHK008 (data model)
  - Phase 3: CHK009–CHK016 (API) + CHK039–CHK044 (cross-cutting)
  - Phase 4–5: CHK025–CHK032 (UI)
  - Phase 6: CHK013, CHK014 (export API) + CHK026 (UI)
  - Phase 7: CHK017–CHK024 (LLM generation)
  - Phase 8: CHK033–CHK038 (non-functional)
- Items with `[Gap]` indicate a requirement that is **missing from the spec** and should be resolved before implementing the related task
- Items with `[Ambiguity]` indicate a requirement that exists but needs clarification before implementation
- Items with `[Consistency]` flag potential conflicts between spec sections that should be aligned
- 44 items total across 6 categories
