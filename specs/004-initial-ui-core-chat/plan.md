# Implementation Plan: Initial UI with Core Chat and Document Query

**Branch**: `004-initial-ui-core-chat` | **Date**: February 16, 2026 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/004-initial-ui-core-chat/spec.md`

## Summary

Build a Blazor-based chat interface for interactive AI conversations with document-grounded responses. Implement using Blazor InteractiveServer render mode with MudBlazor components. Initial implementation uses HTTP NDJSON streaming via `/api/generation/stream` endpoint (following WeatherApiClient pattern), with SignalR Hub upgrade planned as enhancement. Chat interface will be Chat.razor page with streaming response handling via HttpClient.ReadAsStreamAsync() and StreamReader line-by-line parsing. No backend changes required - integrates with existing RAG streaming endpoints.

## Technical Context

**Language/Version**: C# / .NET 10 (Blazor InteractiveServer)  
**Primary Dependencies**: 
- MudBlazor 7.x (UI component library)
- Microsoft.AspNetCore.Components.Web 10.x
- System.Text.Json (NDJSON parsing)
- Existing: `/api/generation/stream`, `/api/documents` endpoints

**Storage**: Client-side in-memory only (session scoped, no persistence across browser sessions)  
**Testing**: 
- bUnit for Blazor component tests
- xUnit for service layer tests
- Manual E2E validation (Playwright deferred to Phase 2)

**Target Platform**: Web browsers (modern evergreen browsers)  
**Project Type**: Blazor Web UI (adding pages/components to existing deepwiki-open-dotnet.Web project)  

**Performance Goals**: 
- First token from API <500ms (API responsibility, UI displays immediately)
- UI remains responsive during streaming
- Stream rendering <16ms per token (60fps target)
- Document collection loading <5s

**Constraints**: 
- No backend modifications allowed (use existing endpoints as-is)
- Session-only state (no persistence required)
- Input blocked during active generation (per FR-018)
- Must support 50+ message conversation history without degradation (SC-002)

**Scale/Scope**: 
- Single page (Chat.razor) + supporting services
- 2-4 MudBlazor components
- 1 API client service
- Session-scoped state management

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Test-First (NON-NEGOTIABLE)
- ✅ **PASS**: Component tests required for Chat.razor using bUnit
- ✅ **PASS**: Service tests required for ChatApiClient
- ✅ **PASS**: Tests for streaming NDJSON parser logic

### Reproducibility & Determinism
- ✅ **PASS**: UI layer only - does not influence LLM content generation
- ℹ️ **NOTE**: Backend already handles snapshot requirements

### Local-First ML
- ✅ **PASS**: UI consumes existing API endpoints - no ML provider changes
- ℹ️ **NOTE**: Backend already implements Ollama-first strategy

### Observability & Cost Visibility
- ✅ **PASS**: UI will log API call failures and streaming errors
- ✅ **PASS**: Backend already tracks LLM metrics

### Security & Privacy
- ✅ **PASS**: Session-only storage (no PII persistence)
- ✅ **PASS**: Uses existing authenticated endpoints
- ⚠️ **REVIEW**: Conversation history in memory - confirm no sensitive data leakage in logs

### Entity Framework Core
- ✅ **PASS**: No database access in UI layer

### Frontend Accessibility & i18n
- ⚠️ **DEFERRED**: WCAG 2.1 AA conformance - documented for Phase 2
- ⚠️ **DEFERRED**: i18n/localization - English-only for Phase 1, localization in Phase 2

**Overall Assessment**: ✅ PASS with 2 items deferred to Phase 2 (accessibility polish, i18n)

## Project Structure

### Documentation (this feature)

```text
specs/004-initial-ui-core-chat/
├── plan.md              # This file
├── research.md          # Phase 0: NDJSON streaming patterns, MudBlazor chat UX
├── data-model.md        # Phase 1: ChatMessage, SessionState, API contracts
├── quickstart.md        # Phase 1: How to use the chat interface
├── contracts/           # Phase 1: TypeScript models for GenerationDelta, API schemas
└── tasks.md             # Phase 2: (/speckit.tasks - NOT created by this command)
```

### Source Code (existing project)

```text
src/deepwiki-open-dotnet.Web/
├── Components/
│   ├── Pages/
│   │   ├── Chat.razor                      # NEW: Main chat interface page
│   │   └── Chat.razor.cs                   # NEW: Code-behind for chat logic
│   ├── Shared/
│   │   ├── ChatMessage.razor               # NEW: Individual message display component
│   │   ├── ChatInput.razor                 # NEW: Message input with submit control
│   │   └── DocumentScopeSelector.razor     # NEW: Collection selection component
│   └── Layout/
│       └── NavMenu.razor                   # MODIFY: Add chat navigation link
├── Services/
│   ├── ChatApiClient.cs                    # NEW: HTTP client for /api/generation/*
│   ├── ChatStateService.cs                 # NEW: Session-scoped conversation state
│   └── NdJsonStreamParser.cs               # NEW: NDJSON line parsing utility
├── Models/
│   ├── ChatMessageModel.cs                 # NEW: UI message model
│   ├── GenerationDeltaDto.cs               # NEW: API response DTO
│   └── SessionRequestDto.cs                # NEW: API request DTO
└── Program.cs                              # MODIFY: Register services, configure MudBlazor

tests/deepwiki-open-dotnet.Web.Tests/       # NEW: Test project
├── Components/
│   ├── ChatTests.cs                        # bUnit tests for Chat.razor
│   ├── ChatMessageTests.cs                 # bUnit tests for message component
│   └── ChatInputTests.cs                   # bUnit tests for input component
└── Services/
    ├── ChatApiClientTests.cs               # Unit tests for API client
    ├── ChatStateServiceTests.cs            # Unit tests for state management
    └── NdJsonStreamParserTests.cs          # Unit tests for stream parsing
```

**Structure Decision**: Adding new components and services to existing `deepwiki-open-dotnet.Web` project. No new projects required. Follows established Blazor project layout with Pages/Components/Services pattern already present in codebase.

## Complexity Tracking

> No constitution violations. This section intentionally left blank.

---

## Phase Completion Report

### ✅ Phase 0: Research & Discovery

**Deliverable**: [research.md](research.md)

**Key Decisions**:
- HTTP NDJSON streaming over SignalR (SignalR deferred to Phase 2)
- NDJSON parsed via StreamReader.ReadLineAsync() + JsonSerializer.Deserialize
- MudBlazor components: MudTextField, MudPaper, MudProgressLinear, MudAutocomplete
- Scoped ChatStateService for session-only conversation history
- Auto-scroll via JSInterop (scrollIntoView)
- Markdig for markdown rendering
- Token batching (50ms windows) for smooth rendering
- Comprehensive error handling with retry and user feedback

**Research Topics Covered**:
1. NDJSON Streaming Patterns
2. MudBlazor Chat UX Best Practices
3. Blazor State Management Strategies
4. Auto-Scroll Implementation
5. Markdown Rendering
6. Source Citation Display
7. Error Handling & Resilience
8. Performance Optimizations

### ✅ Phase 1: Design & Contracts

**Deliverables**: 
- [data-model.md](data-model.md) - UI models, state structures, API DTOs
- [quickstart.md](quickstart.md) - User guide for chat interface
- [contracts/](contracts/) - JSON schemas for API contracts

**Models Defined**:
- `ChatMessageModel` - UI representation with Id, Role, Text, IsStreaming, Sources
- `SourceCitation` - Document metadata with relevance scoring
- `DocumentCollectionModel` - Collection metadata for scope selector
- `ChatSessionState` - Session-scoped service with Messages list, SelectedCollectionIds, IsGenerating flag
- `GenerationDeltaDto` - NDJSON stream DTO
- `SessionRequestDto/ResponseDto` - Session creation API
- `GenerationRequestDto` - Query API with multi-turn context
- `DocumentListResponseDto` - Collections API

**Contracts Created**:
- `GenerationDelta.schema.json` - NDJSON stream format
- `GenerationRequest.schema.json` - Query API payload
- `SessionRequest.schema.json` - Session creation payload
- `SessionResponse.schema.json` - Session creation response
- `DocumentListResponse.schema.json` - Collections list response

**State Flow**: Documented session creation → query submission → streaming → message completion → multi-turn context cycle

### 📋 Next Steps

1. **Ready for `/speckit.tasks`**: All planning documentation complete
2. **Phase 2 Implementation**: Tasks will be generated from this plan
3. **Test-First**: Start with component/service tests before implementation
4. **Review Points**: After services layer, after UI components, before PR

---

## Summary

**Branch**: `004-initial-ui-core-chat`  
**Spec Location**: `/home/mark/docker/deepwiki-open-dotnet/specs/004-initial-ui-core-chat/spec.md`  
**Plan Location**: `/home/mark/docker/deepwiki-open-dotnet/specs/004-initial-ui-core-chat/plan.md`

**Generated Artifacts**:
- ✅ spec.md (feature specification with 19 requirements, 10 success criteria)
- ✅ research.md (NDJSON streaming, MudBlazor UX, state management decisions)
- ✅ data-model.md (8 models/DTOs, state flows, relationships)
- ✅ quickstart.md (user guide with prerequisites, steps, troubleshooting)
- ✅ contracts/ (5 JSON schemas for API contracts)

**Constitution Status**: ✅ PASS (2 items deferred to Phase 2: WCAG 2.1 AA, i18n)

**Technical Approach**: Blazor InteractiveServer + MudBlazor + HTTP NDJSON streaming (WeatherApiClient pattern) → SignalR upgrade later

**Readiness**: ✅ Ready for task breakdown with `/speckit.tasks`
