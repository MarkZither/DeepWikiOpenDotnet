# Tasks: Initial UI with Core Chat and Document Query

**Input**: Design documents from `/home/mark/docker/deepwiki-open-dotnet/specs/004-initial-ui-core-chat/`  
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/ ✅

**Tests**: This feature uses test-first approach per constitution. All test tasks are marked with explicit test instructions.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3, US4)
- Include exact file paths in descriptions

## Path Conventions

- **Source**: `src/deepwiki-open-dotnet.Web/`
- **Tests**: `tests/deepwiki-open-dotnet.Web.Tests/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and test infrastructure

- [X] T001 Create test project `tests/deepwiki-open-dotnet.Web.Tests/` with xUnit and bUnit dependencies
- [X] T002 [P] Add MudBlazor 7.x package to `src/deepwiki-open-dotnet.Web/Program.cs`
- [X] T003 [P] Create Models directory `src/deepwiki-open-dotnet.Web/Models/`
- [X] T004 [P] Create Services directory `src/deepwiki-open-dotnet.Web/Services/`
- [X] T005 [P] Create test fixtures directory `tests/deepwiki-open-dotnet.Web.Tests/Fixtures/`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before ANY user story can be implemented

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T006 Create `MessageRole` enum in `src/deepwiki-open-dotnet.Web/Models/MessageRole.cs`
- [X] T007 [P] Create `SourceCitation` model in `src/deepwiki-open-dotnet.Web/Models/SourceCitation.cs`
- [X] T008 [P] Create `ChatMessageModel` in `src/deepwiki-open-dotnet.Web/Models/ChatMessageModel.cs`
- [X] T009 [P] Create `GenerationDeltaDto` in `src/deepwiki-open-dotnet.Web/Models/GenerationDeltaDto.cs`
- [X] T010 [P] Create `SessionRequestDto` in `src/deepwiki-open-dotnet.Web/Models/SessionRequestDto.cs`
- [X] T011 [P] Create `SessionResponseDto` in `src/deepwiki-open-dotnet.Web/Models/SessionResponseDto.cs`
- [X] T012 [P] Create `GenerationRequestDto` in `src/deepwiki-open-dotnet.Web/Models/GenerationRequestDto.cs`
- [X] T013 [P] Create `ContextMessageDto` in `src/deepwiki-open-dotnet.Web/Models/ContextMessageDto.cs`
- [X] T014 Create `NdJsonStreamParser` service in `src/deepwiki-open-dotnet.Web/Services/NdJsonStreamParser.cs` with line-by-line parsing and error handling
- [X] T015 Create `ChatStateService` (scoped) in `src/deepwiki-open-dotnet.Web/Services/ChatStateService.cs` with Messages list, IsGenerating flag, SelectedCollectionIds, and StateChanged event
- [X] T016 Create `ChatApiClient` service in `src/deepwiki-open-dotnet.Web/Services/ChatApiClient.cs` with HttpClient for /api/generation/stream, /api/generation/session
- [X] T017 Register services in `src/deepwiki-open-dotnet.Web/Program.cs` (AddScoped<ChatStateService>, AddHttpClient<ChatApiClient>, AddSingleton<NdJsonStreamParser>)

**Checkpoint**: Foundation ready - user story implementation can now begin in parallel

---

## Phase 3: User Story 1 - Interactive Chat with AI (Priority: P1) 🎯 MVP

**Goal**: Enable users to have conversational interactions with the AI system to ask questions and receive streaming responses

**Independent Test**: Open UI, type a question, submit, and verify streaming AI response appears with loading indicator

### Tests for User Story 1 ⚠️ Write FIRST, ensure they FAIL

- [X] T018 [P] [US1] Create test for ChatStateService message management in `tests/deepwiki-open-dotnet.Web.Tests/Services/ChatStateServiceTests.cs` (add message, clear messages, state changed events)
- [X] T019 [P] [US1] Create test for NdJsonStreamParser in `tests/deepwiki-open-dotnet.Web.Tests/Services/NdJsonStreamParserTests.cs` (parse valid NDJSON, handle malformed JSON, handle empty lines)
- [X] T020 [P] [US1] Create test for ChatApiClient streaming in `tests/deepwiki-open-dotnet.Web.Tests/Services/ChatApiClientTests.cs` (successful stream, error handling, cancellation)
- [X] T021 [P] [US1] Create bUnit test for Chat.razor basic rendering in `tests/deepwiki-open-dotnet.Web.Tests/Components/ChatTests.cs` (renders input field, message list)
- [X] T022 [P] [US1] Create bUnit test for ChatInput component in `tests/deepwiki-open-dotnet.Web.Tests/Components/ChatInputTests.cs` (submit on click, disabled during generation, prevent empty submission)

### Implementation for User Story 1

- [X] T023 [P] [US1] Create ChatInput.razor component in `src/deepwiki-open-dotnet.Web/Components/Shared/ChatInput.razor` with MudTextField, send button, disabled state binding to ChatStateService.IsGenerating
- [X] T024 [P] [US1] Create ChatMessage.razor component in `src/deepwiki-open-dotnet.Web/Components/Shared/ChatMessage.razor` with MudPaper, role-based styling (user vs assistant), markdown rendering via Markdig
- [X] T025 [US1] Create Chat.razor page in `src/deepwiki-open-dotnet.Web/Components/Pages/Chat.razor` with ChatInput, message list, MudProgressLinear for streaming indicator
- [X] T026 [US1] Implement streaming logic in Chat.razor.cs: inject ChatApiClient and ChatStateService, handle message submission, consume NDJSON stream via NdJsonStreamParser, update ChatStateService on each token
- [X] T027 [US1] Add auto-scroll to latest message using JSInterop scrollIntoView in Chat.razor
- [X] T028 [US1] Add error handling display in Chat.razor for API failures (toast or inline error message)
- [X] T029 [US1] Add chat navigation link to `src/deepwiki-open-dotnet.Web/Components/Layout/NavMenu.razor` with icon and "Chat" label

**Checkpoint**: At this point, User Story 1 should be fully functional - users can chat with AI and see streaming responses

---

## Phase 4: User Story 2 - Document-Based Query (Priority: P2)

**Goal**: Enable users to query specific documents/collections and see source citations in AI responses

**Independent Test**: Select a document collection, ask a question, verify response includes source citations from those documents

### Tests for User Story 2 ⚠️ Write FIRST, ensure they FAIL

- [X] T030 [P] [US2] Create test for ChatApiClient with collection filters in `tests/deepwiki-open-dotnet.Web.Tests/Services/ChatApiClientTests.cs` (request includes collection_ids parameter)
- [X] T031 [P] [US2] Create bUnit test for ChatMessage with source citations in `tests/deepwiki-open-dotnet.Web.Tests/Components/ChatMessageTests.cs` (sources displayed, clickable links)
- [X] T032 [P] [US2] Create test for default scope indicator logic in `tests/deepwiki-open-dotnet.Web.Tests/Services/ChatStateServiceTests.cs` (when no collections selected, all documents indicator shown)

### Implementation for User Story 2

- [X] T033 [US2] Update ChatApiClient.cs to include collection_ids in GenerationRequestDto when ChatStateService.SelectedCollectionIds is not empty
- [X] T034 [US2] Update ChatMessage.razor to display SourceCitation list below message text with title, URL, and relevance score
- [X] T035 [US2] Update NdJsonStreamParser.cs to parse metadata.sources from NDJSON stream and map to SourceCitation models
- [X] T036 [US2] Update Chat.razor to show scope indicator chip (e.g., "Searching: All Documents" or "Searching: 3 collections") above input field, bound to ChatStateService.SelectedCollectionIds
- [X] T037 [US2] Add visual distinction for document-grounded responses (icon or badge) in ChatMessage.razor when Sources.Count > 0

**Checkpoint**: At this point, User Stories 1 AND 2 should both work independently - chat works with source citations

---

## Phase 5: User Story 3 - View and Select Document Collections (Priority: P3)

**Goal**: Enable users to browse available document collections and select specific ones to focus their queries

**Independent Test**: View list of collections, select one or more, verify subsequent queries use those collections and scope indicator updates

### Tests for User Story 3 ⚠️ Write FIRST, ensure they FAIL

- [ ] T038 [P] [US3] Create `DocumentCollectionModel` model in `src/deepwiki-open-dotnet.Web/Models/DocumentCollectionModel.cs`
- [ ] T039 [P] [US3] Create `DocumentListResponseDto` in `src/deepwiki-open-dotnet.Web/Models/DocumentListResponseDto.cs`
- [ ] T040 [P] [US3] Create test for ChatApiClient.GetCollectionsAsync in `tests/deepwiki-open-dotnet.Web.Tests/Services/ChatApiClientTests.cs` (fetches from /api/documents)
- [ ] T041 [P] [US3] Create bUnit test for DocumentScopeSelector component in `tests/deepwiki-open-dotnet.Web.Tests/Components/DocumentScopeSelectorTests.cs` (loads collections, selection updates state)
- [ ] T042 [P] [US3] Create test for ChatStateService collection selection in `tests/deepwiki-open-dotnet.Web.Tests/Services/ChatStateServiceTests.cs` (add/remove collection IDs, clear selections)

### Implementation for User Story 3

- [ ] T043 [US3] Add GetCollectionsAsync method to ChatApiClient.cs calling GET /api/documents and deserializing DocumentListResponseDto
- [ ] T044 [US3] Add SetSelectedCollections method to ChatStateService.cs updating SelectedCollectionIds and triggering StateChanged event
- [ ] T045 [P] [US3] Create DocumentScopeSelector.razor component in `src/deepwiki-open-dotnet.Web/Components/Shared/DocumentScopeSelector.razor` with MudAutocomplete multi-select
- [ ] T046 [US3] Load collections in DocumentScopeSelector.OnInitializedAsync via ChatApiClient.GetCollectionsAsync
- [ ] T047 [US3] Wire selection changes in DocumentScopeSelector to ChatStateService.SetSelectedCollections
- [ ] T048 [US3] Add DocumentScopeSelector component to Chat.razor above the chat input area
- [ ] T049 [US3] Update scope indicator in Chat.razor to show selected collection names when collections are selected (instead of "All Documents")

**Checkpoint**: All user stories 1-3 should now be independently functional - full chat + document scope selection

---

## Phase 6: User Story 4 - Clear Chat History (Priority: P4)

**Goal**: Enable users to start fresh conversations by clearing previous chat history

**Independent Test**: Have a conversation history, click clear/reset button, verify chat interface is empty and system remains functional

### Tests for User Story 4 ⚠️ Write FIRST, ensure they FAIL

- [ ] T050 [P] [US4] Create test for ChatStateService.ClearMessages in `tests/deepwiki-open-dotnet.Web.Tests/Services/ChatStateServiceTests.cs` (messages cleared, state event fired, collections preserved)
- [ ] T051 [P] [US4] Create bUnit test for clear button in Chat.razor in `tests/deepwiki-open-dotnet.Web.Tests/Components/ChatTests.cs` (button click clears messages, input remains enabled)

### Implementation for User Story 4

- [ ] T052 [US4] Add ClearMessages method to ChatStateService.cs clearing Messages list but preserving SelectedCollectionIds
- [ ] T053 [US4] Add clear button (MudIconButton with trash icon) to Chat.razor header calling ChatStateService.ClearMessages
- [ ] T054 [US4] Add confirmation dialog (MudDialog) before clearing chat history in Chat.razor
- [ ] T055 [US4] Ensure clear preserves document collection selections per FR-013

**Checkpoint**: All user stories should now be independently functional - complete feature set ready

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories

- [ ] T056 [P] Add session creation logic: call POST /api/generation/session on Chat.razor mount, store session_id in ChatStateService
- [ ] T057 [P] Update ChatApiClient to include session_id and context (previous messages) in GenerationRequestDto for multi-turn conversations per FR-017
- [ ] T058 [P] Add keyboard shortcuts: Enter to submit in ChatInput.razor, Shift+Enter for new line, Esc to clear input
- [ ] T059 [P] Add token batching (50ms windows) in Chat.razor to batch rapid NDJSON tokens before triggering StateHasChanged for performance
- [ ] T060 [P] Add input validation in ChatInput.razor: max length 2000 chars, prevent special character injection
- [ ] T061 [P] Add accessibility attributes to Chat.razor components: aria-labels, roles, live regions for streaming content
- [ ] T062 [P] Add logging for streaming failures, API errors, and session lifecycle in ChatApiClient.cs
- [ ] T063 Update project README.md with quickstart instructions referencing `specs/004-initial-ui-core-chat/quickstart.md`
- [ ] T064 Run through quickstart.md validation scenarios: basic chat, document query, collection selection, clear history
- [ ] T065 [P] Performance validation: test 50+ message conversation per SC-002, verify no degradation

---

## Phase 8: User Story 5 - Document Management (Priority: P5)

**Goal**: Enable users to browse the document library, ingest new documents into the vector store, and delete documents — backed by the existing `DocumentsController` API (`POST /api/documents/ingest`, `GET /api/documents`, `DELETE /api/documents/{id}`).

**Independent Test**: Navigate to Documents page, submit an ingest form with RepoUrl + content, verify document appears in the list with chunk count; click delete, verify document removed.

### Models for User Story 5 ⚠️ Create FIRST (no tests needed — pure DTOs)

- [ ] T066 [P] [US5] Create `IngestDocumentDto` in `src/deepwiki-open-dotnet.Web/Models/IngestDocumentDto.cs` (RepoUrl, FilePath, Title, Text, optional Metadata as `JsonElement?`)
- [ ] T067 [P] [US5] Create `IngestRequestDto` in `src/deepwiki-open-dotnet.Web/Models/IngestRequestDto.cs` (Documents list, ContinueOnError bool, BatchSize int)
- [ ] T068 [P] [US5] Create `IngestResponseDto` in `src/deepwiki-open-dotnet.Web/Models/IngestResponseDto.cs` (SuccessCount, FailureCount, TotalChunks, DurationMs, IngestedDocumentIds, Errors list of IngestErrorDto)
- [ ] T069 [P] [US5] Create `DocumentSummaryDto` in `src/deepwiki-open-dotnet.Web/Models/DocumentSummaryDto.cs` (Id Guid, RepoUrl, FilePath, Title, CreatedAt, UpdatedAt, TokenCount, FileType, IsCode bool)

### Tests for User Story 5 ⚠️ Write FIRST, ensure they FAIL

- [ ] T070 [P] [US5] Create `DocumentsApiClientTests.cs` in `tests/deepwiki-open-dotnet.Web.Tests/Services/DocumentsApiClientTests.cs` — test `IngestAsync` posts to `/api/documents/ingest` and deserializes `IngestResponseDto`
- [ ] T071 [P] [US5] Add test to `DocumentsApiClientTests.cs` — `ListAsync` calls `GET /api/documents` with `page`, `pageSize`, `repoUrl` query params and deserializes `DocumentListResponseDto`
- [ ] T072 [P] [US5] Add test to `DocumentsApiClientTests.cs` — `DeleteAsync` sends `DELETE /api/documents/{id}` and returns true on 204, false on 404
- [ ] T073 [P] [US5] Create bUnit test for `DocumentLibrary.razor` in `tests/deepwiki-open-dotnet.Web.Tests/Components/DocumentLibraryTests.cs` (renders document rows, delete button per row, empty state message)
- [ ] T074 [P] [US5] Create bUnit test for `IngestForm.razor` in `tests/deepwiki-open-dotnet.Web.Tests/Components/IngestFormTests.cs` (required field validation blocks submit, successful ingest shows MudAlert success, error response shows error alert)

### Implementation for User Story 5

- [ ] T075 [P] [US5] Create `DocumentsApiClient` service in `src/deepwiki-open-dotnet.Web/Services/DocumentsApiClient.cs` with `IngestAsync(IngestRequestDto)`, `ListAsync(page, pageSize, repoUrl?)`, `DeleteAsync(Guid)`, `GetAsync(Guid)`
- [ ] T076 [P] [US5] Register `DocumentsApiClient` in `src/deepwiki-open-dotnet.Web/Program.cs` via `AddHttpClient<DocumentsApiClient>`
- [ ] T077 [P] [US5] Create `IngestForm.razor` component in `src/deepwiki-open-dotnet.Web/Components/Shared/IngestForm.razor` with MudTextField fields for RepoUrl, FilePath, Title, and a MudTextField multiline for document text; MudProgressLinear while ingesting; MudAlert for success (shows SuccessCount + TotalChunks) and error feedback; `OnIngested` EventCallback to notify parent
- [ ] T078 [US5] Create `DocumentLibrary.razor` page in `src/deepwiki-open-dotnet.Web/Components/Pages/DocumentLibrary.razor` with `@page "/documents"`, MudTable showing DocumentSummaryDto rows (Title, RepoUrl, FilePath, TokenCount, FileType, CreatedAt), MudTextField filter bound to `repoUrl` query param, pagination via MudTablePager
- [ ] T079 [US5] Load documents in `DocumentLibrary.OnInitializedAsync` via `DocumentsApiClient.ListAsync`; reload on filter change and after ingest
- [ ] T080 [US5] Wire delete button in `DocumentLibrary.razor` to `DocumentsApiClient.DeleteAsync` with MudDialog confirmation; refresh table on success; show MudSnackbar on error
- [ ] T081 [US5] Embed `IngestForm` component in `DocumentLibrary.razor` inside a MudExpansionPanel labelled "Add Documents"; call reload on `OnIngested` event
- [ ] T082 [US5] Add Documents navigation link to `src/deepwiki-open-dotnet.Web/Components/Layout/NavMenu.razor` with `Icons.Material.Filled.LibraryBooks` and "Documents" label

**Checkpoint**: Users can browse all ingested documents, filter by repository, ingest new content, and delete documents — completing the document management lifecycle

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion - BLOCKS all user stories
- **User Stories (Phases 3-6)**: All depend on Foundational phase completion
  - User stories can proceed in parallel (if staffed)
  - Or sequentially in priority order: US1 → US2 → US3 → US4
- **Polish (Phase 7)**: Depends on desired user stories being complete (at minimum US1 for MVP)

### User Story Dependencies

- **User Story 1 (P1)** 🎯 MVP: Can start after Foundational (Phase 2) - No dependencies on other stories
- **User Story 2 (P2)**: Can start after Foundational (Phase 2) - Integrates with US1 (extends ChatMessage, ChatApiClient) but independently testable
- **User Story 3 (P3)**: Can start after Foundational (Phase 2) - Integrates with US1 (adds to Chat.razor) but independently testable
- **User Story 4 (P4)**: Can start after Foundational (Phase 2) - Integrates with US1 (adds clear to ChatStateService) but independently testable

### Within Each User Story

1. **Tests FIRST**: Write all tests for the story, ensure they FAIL
2. **Models before services**: Create DTOs and models
3. **Services before UI**: Implement ChatApiClient, ChatStateService logic
4. **Components**: Build UI components (ChatInput, ChatMessage, Chat.razor)
5. **Integration**: Wire components together
6. **Story complete**: Verify independent test scenario passes before moving to next priority

### Parallel Opportunities

**Within Setup (Phase 1)**:
- T002, T003, T004, T005 can all run in parallel

**Within Foundational (Phase 2)**:
- T007-T013 (all model files) can run in parallel
- T014, T015, T016 (services) can run in parallel after models complete

**Within User Story 1 Tests**:
- T018, T019, T020, T021, T022 can all run in parallel

**Within User Story 1 Implementation**:
- T023, T024 (components) can run in parallel

**Across User Stories (if team capacity allows)**:
- After Phase 2 completes, US1, US2, US3, US4 can all start in parallel on separate branches
- US2 developer can start T030-T032 tests while US1 developer finishes implementation
- US3 developer can work on collection models/tests while US1/US2 in progress

**Within Polish (Phase 7)**:
- T056, T057, T058, T059, T060, T061, T062, T065 can run in parallel (different files/concerns)

---

## Parallel Example: User Story 1

### All Tests Launch Together (Test-First)
```bash
# Run these tasks in parallel (different test files):
T018: ChatStateService tests
T019: NdJsonStreamParser tests
T020: ChatApiClient tests
T021: Chat.razor bUnit tests
T022: ChatInput.razor bUnit tests
```

### All Components Launch Together
```bash
# Run these tasks in parallel (different component files):
T023: ChatInput.razor implementation
T024: ChatMessage.razor implementation
```

---

## Implementation Strategy

### MVP First (User Story 1 Only) - Recommended

1. ✅ Complete Phase 1: Setup (T001-T005)
2. ✅ Complete Phase 2: Foundational (T006-T017) - **CRITICAL GATE**
3. ✅ Complete Phase 3: User Story 1 (T018-T029) - Test-first approach
4. **STOP and VALIDATE**: Test chat interface independently with streaming responses
5. Optionally add Phase 7 polish items: T056 (session), T057 (multi-turn context), T058 (keyboard shortcuts)
6. **Deploy/demo MVP**: Users can now chat with AI

### Incremental Delivery (Recommended)

1. Complete Setup + Foundational → Foundation ready
2. Add User Story 1 → Test independently → **Deploy/Demo (MVP!)**
3. Add User Story 2 → Test independently → Deploy/Demo (now with source citations)
4. Add User Story 3 → Test independently → Deploy/Demo (now with collection selection)
5. Add User Story 4 → Test independently → Deploy/Demo (complete feature set)
6. Add Phase 7 polish → Final deployment

Each story adds value without breaking previous stories.

### Parallel Team Strategy

With multiple developers after Foundational phase completes:

- **Developer A**: User Story 1 (T018-T029) - Core chat interface
- **Developer B**: User Story 2 (T030-T037) - Source citations (waits for T025 ChatMessage.razor, then extends it)
- **Developer C**: User Story 3 (T038-T049) - Collection selector (waits for T025 Chat.razor, then adds to it)
- **Developer D**: User Story 4 (T050-T055) - Clear history (waits for T015 ChatStateService, then extends it)

**Coordination Points**:
- All wait for Phase 2 (Foundational) to complete
- US2, US3, US4 should monitor US1 component creation (T023-T025) to avoid conflicts
- Merge US1 first, then others rebase and integrate

---

## Notes

- **[P] tasks** = different files, no dependencies, safe for parallel execution
- **[Story] label** maps task to specific user story for traceability and independent testing
- **Test-first mandatory** per constitution: write tests, watch them fail, implement, watch them pass
- **Streaming complexity**: T026 is the most complex task (NDJSON parsing + UI updates), allocate extra time
- **Reusable patterns**: T014 (NdJsonStreamParser) is reusable if other streaming features added later
- **Phase 2 is critical**: Foundation MUST be solid before user stories - avoid rushing this phase
- **Each user story is independently valuable**: Can stop after any story and still have a working feature
- **Avoid**: vague tasks, same file conflicts, skipping tests, cross-story dependencies that break independence

---

## Task Summary

- **Total Tasks**: 82
- **Setup Tasks**: 5
- **Foundational Tasks**: 12 (CRITICAL - blocks all stories)
- **User Story 1 Tasks**: 12 (5 tests + 7 implementation)
- **User Story 2 Tasks**: 8 (3 tests + 5 implementation)
- **User Story 3 Tasks**: 12 (5 tests + 7 implementation)
- **User Story 4 Tasks**: 6 (2 tests + 4 implementation)
- **User Story 5 Tasks**: 17 (4 models + 5 tests + 8 implementation)
- **Polish Tasks**: 10
- **Parallel Opportunities**: 35+ tasks marked [P] across all phases
- **Independent Test Criteria**: Each user story has explicit test scenario
- **Suggested MVP Scope**: Phases 1-3 only (User Story 1) = 29 tasks for working chat interface
