# Tasks: MCP Streaming RAG Service

**Input**: Design documents from `/specs/001-streaming-rag-service/`
**Prerequisites**: plan.md, spec.md (user stories), research.md, data-model.md, contracts/

**Tests**: Tests are REQUIRED per Constitution Section I (Test-First). Test tasks are integrated throughout phases and MUST pass before corresponding implementation tasks can be considered complete. This follows TDD principles: write test → watch it fail → implement → watch it pass.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story. Test tasks marked with 🧪 are blockers for implementation tasks.

## Format: `- [ ] [ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and basic structure

- [ ] T001 Review project structure in [plan.md](plan.md) and verify existing directories match
- [ ] T002 Create directory structure for new components: src/DeepWiki.Data.Abstractions/DTOs/, src/DeepWiki.Rag.Core/Services/, src/DeepWiki.Rag.Core/Providers/, src/DeepWiki.Rag.Core/Streaming/
- [ ] T003 [P] Add NuGet package references: AspNetCoreRateLimit (5.0+), OpenAI SDK (2.0.0+), System.Text.Json (if not already present)
- [ ] T004 [P] Configure OpenTelemetry metrics instrumentation in src/deepwiki-open-dotnet.ServiceDefaults/Extensions.cs
- [ ] T004a [P] 🧪 Create test project structure: tests/DeepWiki.Rag.Core.Tests/, tests/deepwiki-open-dotnet.Tests/Integration/
- [ ] T004b [P] 🧪 Add test NuGet packages: xUnit (2.6+), Moq (4.20+), FluentAssertions (6.12+), Microsoft.AspNetCore.Mvc.Testing (10.0)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core abstractions and infrastructure that MUST be complete before ANY user story can be implemented

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

### Foundational Implementation Tasks

- [ ] T005 Define IGenerationService interface in src/DeepWiki.Data.Abstractions/IGenerationService.cs with IAsyncEnumerable<GenerationDelta> GenerateAsync method
- [ ] T006 [P] Create GenerationRequest DTO in src/DeepWiki.Data.Abstractions/DTOs/GenerationRequest.cs
- [ ] T007 [P] Create GenerationDelta DTO in src/DeepWiki.Data.Abstractions/DTOs/GenerationDelta.cs with JSON serialization attributes
- [ ] T008 [P] Create SessionRequest DTO in src/DeepWiki.Data.Abstractions/DTOs/SessionRequest.cs
- [ ] T009 [P] Create SessionResponse DTO in src/DeepWiki.Data.Abstractions/DTOs/SessionResponse.cs
- [ ] T010 [P] Create PromptRequest DTO in src/DeepWiki.Data.Abstractions/DTOs/PromptRequest.cs with validation attributes
- [ ] T011 [P] Create CancelRequest DTO in src/DeepWiki.Data.Abstractions/DTOs/CancelRequest.cs
- [ ] T012 Define IModelProvider interface in src/DeepWiki.Rag.Core/Providers/IModelProvider.cs with Name, IsAvailableAsync, StreamAsync methods
- [ ] T013 Create Session entity class in src/DeepWiki.Rag.Core/Models/Session.cs per data-model.md schema (add ExpiresAt field for session expiration)
- [ ] T014 [P] Create Prompt entity class in src/DeepWiki.Rag.Core/Models/Prompt.cs per data-model.md schema
- [ ] T015 [P] Create SessionStatus and PromptStatus enums in src/DeepWiki.Rag.Core/Models/Enums.cs
- [ ] T016 Implement SessionManager class in src/DeepWiki.Rag.Core/Services/SessionManager.cs with in-memory storage (ConcurrentDictionary) for sessions and prompts
- [ ] T017 Implement GenerationMetrics class in src/DeepWiki.Rag.Core/Observability/GenerationMetrics.cs with TTF histogram, token counter, error counter
- [ ] T018 Configure rate limiting middleware registration in src/deepwiki-open-dotnet.ApiService/Program.cs with IP-based token-bucket settings

### Foundational Test Tasks (TDD - Write First)

- [ ] T005a 🧪 [P] Write contract test for IGenerationService interface in tests/DeepWiki.Data.Abstractions.Tests/IGenerationServiceContractTests.cs (verify async enumerable signature, cancellation support)
- [ ] T006a 🧪 [P] Write DTO serialization tests in tests/DeepWiki.Data.Abstractions.Tests/DTOSerializationTests.cs (GenerationRequest, GenerationDelta, SessionRequest, SessionResponse, PromptRequest, CancelRequest) - verify JSON round-trip, required fields, validation attributes
- [ ] T012a 🧪 [P] Write contract test for IModelProvider interface in tests/DeepWiki.Rag.Core.Tests/IModelProviderContractTests.cs (verify streaming signature, health check, cancellation)
- [ ] T013a 🧪 [P] Write entity validation tests in tests/DeepWiki.Rag.Core.Tests/Models/SessionTests.cs (Session state transitions, validation rules, expiration logic)
- [ ] T014a 🧪 [P] Write entity validation tests in tests/DeepWiki.Rag.Core.Tests/Models/PromptTests.cs (Prompt state transitions, idempotency key uniqueness, validation rules)
- [ ] T016a 🧪 Write SessionManager unit tests in tests/DeepWiki.Rag.Core.Tests/Services/SessionManagerTests.cs (session creation, prompt creation, idempotency key checking, session expiration cleanup, concurrent access safety)
- [ ] T017a 🧪 Write GenerationMetrics unit tests in tests/DeepWiki.Rag.Core.Tests/Observability/GenerationMetricsTests.cs (TTF recording, token counting, error rate tracking, metric emission validation)

**Checkpoint**: Foundation ready - user story implementation can now begin in parallel

---

## Phase 3: User Story 1 - Developer Assistant (Priority: P1) 🎯 MVP

**Goal**: Enable streaming code suggestions with token-by-token delivery, accept/cancel actions

**Independent Test**: Start stub server and extension against HTTP NDJSON endpoint; send prompt and verify token deltas are streamed, accept inserts text

### Test Tasks for User Story 1 (TDD - Write First) 🧪

- [ ] T019a 🧪 [P] [US1] Write StreamNormalizer unit tests in tests/DeepWiki.Rag.Core.Tests/Streaming/StreamNormalizerTests.cs (sequence assignment, deduplication, UTF-8 incomplete byte handling, edge cases)
- [ ] T020a 🧪 [P] [US1] Write OllamaProvider unit tests in tests/DeepWiki.Rag.Core.Tests/Providers/OllamaProviderTests.cs (NDJSON parsing, delta mapping, health check, 30s timeout, connection failure, provider stall scenario)
- [ ] T023a 🧪 [US1] Write GenerationService unit tests in tests/DeepWiki.Rag.Core.Tests/Services/GenerationServiceTests.cs (RAG orchestration, context formatting, cancellation propagation, idempotency key caching, timeout enforcement, error delta emission)
- [ ] T028a 🧪 [P] [US1] Write controller integration tests in tests/deepwiki-open-dotnet.Tests/Integration/GenerationControllerTests.cs using TestServer (session creation, streaming endpoint NDJSON format validation, cancel endpoint, error responses 400/404)
- [ ] T034a 🧪 [US1] Write rate limiting integration tests in tests/deepwiki-open-dotnet.Tests/Integration/RateLimitingTests.cs (429 after limit exceeded, X-RateLimit-* headers, Retry-After header, legitimate traffic not blocked)
- [ ] T036a 🧪 [US1] Write metrics validation tests in tests/DeepWiki.Rag.Core.Tests/Observability/MetricsIntegrationTests.cs (TTF recorded, tokens/sec emitted, token counts tracked, OpenTelemetry export validation)
- [ ] T039a 🧪 [US1] Write cancellation latency test in tests/deepwiki-open-dotnet.Tests/Integration/CancellationTests.cs (verify SC-002: cancellation completes <200ms, no further deltas emitted, final done/error delta within 200ms)
- [ ] T039b 🧪 [US1] Write edge case test suite in tests/deepwiki-open-dotnet.Tests/Integration/EdgeCaseTests.cs (provider stalls, duplicate tokens, partial UTF-8 sequences, concurrent cancel requests, idempotency key retries)
- [ ] T039c 🧪 [US1] Write tokenization parity tests in tests/DeepWiki.Rag.Core.Tests/TokenizationParityTests.cs per FR-008 (critical flows: exact parity for schema outputs/code blocks/formatting; non-critical flows: <=5% tolerance, chunk boundary drift <=1 token)

### Implementation Tasks for User Story 1

- [ ] T019 [P] [US1] Implement StreamNormalizer class in src/DeepWiki.Rag.Core/Streaming/StreamNormalizer.cs with sequence assignment, deduplication, UTF-8 validation (tests T019a must pass)
- [ ] T020 [P] [US1] Implement OllamaProvider class in src/DeepWiki.Rag.Core/Providers/OllamaProvider.cs with HTTP streaming client for /api/generate endpoint (tests T020a must pass)
- [ ] T021 [US1] Implement OllamaProvider.StreamAsync method with NDJSON parsing, delta mapping, and 30s stall timeout (tests T020a must pass)
- [ ] T022 [US1] Implement OllamaProvider.IsAvailableAsync health check using /api/tags endpoint (tests T020a must pass)
- [ ] T023 [US1] Implement GenerationService class in src/DeepWiki.Rag.Core/Services/GenerationService.cs orchestrating IVectorStore retrieval + IModelProvider generation (tests T023a must pass)
- [ ] T024 [US1] Add RAG context building logic in GenerationService to format retrieved documents into system prompt (tests T023a must pass)
- [ ] T025 [US1] Implement cancellation token propagation and timeout enforcement (30s) in GenerationService (tests T023a, T039a must pass)
- [ ] T026 [US1] Add idempotency key checking in GenerationService to return cached responses for duplicate keys (tests T023a, T039b must pass)
- [ ] T027 [US1] Wire StreamNormalizer into GenerationService pipeline to ensure sequence integrity (tests T019a, T023a must pass)
- [ ] T028 [US1] Implement GenerationController.CreateSession endpoint (POST /api/generation/session) in src/deepwiki-open-dotnet.ApiService/Controllers/GenerationController.cs (tests T028a must pass)
- [ ] T029 [US1] Implement GenerationController.StreamGeneration endpoint (POST /api/generation/stream) with IAsyncEnumerable<GenerationDelta> return type and application/x-ndjson content type (tests T028a must pass)
- [ ] T030 [US1] Implement GenerationController.CancelGeneration endpoint (POST /api/generation/cancel) in src/deepwiki-open-dotnet.ApiService/Controllers/GenerationController.cs (tests T028a, T039a must pass)
- [ ] T031 [US1] Add dependency injection registration for IGenerationService, SessionManager, OllamaProvider in src/deepwiki-open-dotnet.ApiService/Program.cs
- [ ] T032 [US1] Configure Ollama base URL from configuration (appsettings.json) with default http://localhost:11434
- [ ] T033 [US1] Add error handling in GenerationController to convert exceptions to structured error deltas (tests T028a must pass)
- [ ] T034 [US1] Implement rate limiting middleware using AspNetCoreRateLimit in src/deepwiki-open-dotnet.ApiService/Middleware/RateLimitingMiddleware.cs with 100 req/min per IP (tests T034a must pass)
- [ ] T035 [US1] Add rate limit configuration section to src/deepwiki-open-dotnet.ApiService/appsettings.json
- [ ] T036 [US1] Wire up GenerationMetrics instrumentation in GenerationService to record TTF, tokens/sec, token counts (tests T036a must pass)
- [ ] T037 [US1] Add request validation in GenerationController for empty prompts, invalid sessionIds, topK range checks (tests T028a must pass)
- [ ] T038 [US1] Implement TTF measurement in GenerationService using Stopwatch from first request to first delta emission (tests T036a must pass, define "typical dev setup" baseline: 16GB RAM, Ollama vicuna-13b, local SSD)
- [ ] T039 [US1] Add logging for session lifecycle (create, prompt submit, cancel, complete) using ILogger

**Checkpoint**: User Story 1 complete - HTTP NDJSON streaming with Ollama provider, rate limiting, observability, cancellation (all tests passing)

---

## Phase 4: User Story 2 - CLI/Curl Client (Priority: P2)

**Goal**: Ensure contract works with simple clients (curl/fetch) for scripts and tools

**Independent Test**: Use curl to POST prompt to HTTP NDJSON endpoint and validate JSON lines conform to GenerationDelta schema with done event

### Implementation for User Story 2

- [ ] T040 [US2] Create curl demo script in specs/001-streaming-rag-service/examples/curl-demo.sh based on quickstart.md scenarios
- [ ] T041 [US2] Add validation in GenerationController to return 400 for missing sessionId or empty prompt with structured ErrorResponse
- [ ] T042 [US2] Implement proper content-type handling in GenerationController to ensure application/x-ndjson is set for streaming responses
- [ ] T043 [US2] Add example NDJSON responses in OpenAPI spec (contracts/generation-service.yaml) for documentation
- [ ] T044 [US2] Create bash script example in quickstart.md for parsing NDJSON stream with jq (already documented, validate implementation)
- [ ] T045 [US2] Add validation for topK range (1-20) in GenerationController with 400 error for out-of-range values
- [ ] T046 [US2] Implement filter parsing in GenerationService to pass retrieval filters to IVectorStore.QueryAsync
- [ ] T047 [US2] Add HTTP response header validation to ensure X-RateLimit-* headers are included in responses
- [ ] T048 [US2] Document error response codes (400, 404, 429) in contracts/generation-service.yaml with examples

**Checkpoint**: User Story 2 complete - curl/fetch compatibility validated, proper error handling, filter support

---

## Phase 5: User Story 3 - Server Admin/Ops (Priority: P3)

**Goal**: Validate server behavior under cancellation, provider failures, provider switchover; inspect metrics

**Independent Test**: Run provider failure scenarios (simulate disconnect) and validate server emits error delta and cleans up session

### Test Tasks for User Story 3 (TDD - Write First) 🧪

- [ ] T049a 🧪 [P] [US3] Write OpenAIProvider unit tests in tests/DeepWiki.Rag.Core.Tests/Providers/OpenAIProviderTests.cs (SDK streaming, delta mapping, sequence tracking, health check, connection failure)
- [ ] T053a 🧪 [US3] Write provider selection tests in tests/DeepWiki.Rag.Core.Tests/Services/ProviderSelectionTests.cs (ordered fallback, circuit breaker logic, availability tracking, provider stall handling)
- [ ] T054a 🧪 [US3] Write circuit breaker tests in tests/DeepWiki.Rag.Core.Tests/Services/CircuitBreakerTests.cs (repeated failures trigger circuit open, timeout before retry, successful request closes circuit)
- [ ] T059a 🧪 [US3] Write metrics export validation test in tests/deepwiki-open-dotnet.Tests/Integration/PrometheusExportTests.cs (verify OpenTelemetry exports to Prometheus format, TTF/tokens/errors visible)
- [ ] T060a 🧪 [P] [US3] Write health check endpoint tests in tests/deepwiki-open-dotnet.Tests/Integration/HealthCheckTests.cs (provider availability reported, degraded status when provider down)

### Implementation Tasks for User Story 3

- [ ] T049 [P] [US3] Implement OpenAIProvider class in src/DeepWiki.Rag.Core/Providers/OpenAIProvider.cs with SDK streaming integration (tests T049a must pass)
- [ ] T050 [US3] Implement OpenAIProvider.StreamAsync using ChatClient.CompleteChatStreamingAsync with delta mapping and sequence tracking (tests T049a must pass)
- [ ] T051 [US3] Implement OpenAIProvider.IsAvailableAsync health check with API connectivity test (tests T049a must pass)
- [ ] T052 [US3] Add provider selection configuration in appsettings.json with ordered provider list (e.g., ["Ollama", "OpenAI"])
- [ ] T053 [US3] Implement provider selection logic in GenerationService to try providers in order and fallback on failure (tests T053a must pass)
- [ ] T054 [US3] Add circuit breaker logic in GenerationService to skip unavailable providers temporarily after repeated failures (tests T054a must pass)
- [ ] T055 [US3] Implement error delta emission in GenerationService for provider timeout (30s stall) with structured metadata (tests T023a, T053a must pass)
- [ ] T056 [US3] Implement error delta emission in GenerationService for provider connection failures with structured metadata (tests T023a, T053a must pass)
- [ ] T057 [US3] Add cancellation cleanup logic in SessionManager to mark prompts as Cancelled and emit final done delta (tests T016a must pass)
- [ ] T058 [US3] Implement Retry-After header calculation in rate limiting middleware for 429 responses (tests T034a must pass)
- [ ] T059 [US3] Add Prometheus metrics export configuration in src/deepwiki-open-dotnet.ServiceDefaults/Extensions.cs for GenerationMetrics (tests T059a must pass)
- [ ] T060 [US3] Create health check endpoint in GenerationController (GET /api/generation/health) reporting provider availability (tests T060a must pass)
- [ ] T061 [US3] Add per-session token count tracking in SessionManager and Prompt entity (tests T016a must pass)
- [ ] T062 [US3] Implement error rate counter in GenerationMetrics categorized by error type (timeout, unavailable, cancelled) (tests T017a must pass)
- [ ] T063 [US3] Add logging for provider selection, fallback, and availability changes using ILogger
- [ ] T064 [US3] Implement graceful shutdown handling in GenerationService to emit done deltas for in-flight prompts on app shutdown (tests T023a must pass)

**Checkpoint**: User Story 3 complete - provider switchover, error handling, comprehensive observability, ops readiness (all tests passing)

---

## Phase 6: Optional SignalR Hub (Enhancement)

**Purpose**: Provide richer bidirectional communication for TypeScript/.NET clients (not MVP-blocking)

### Test Tasks for SignalR 🧪

- [ ] T065a 🧪 [P] Write SignalR contract parity tests in tests/deepwiki-open-dotnet.Tests/Integration/SignalRParityTests.cs (HTTP NDJSON vs SignalR delta sequences match for same input, schema validation)

### Implementation Tasks for SignalR

- [ ] T065 [P] Create GenerationHub SignalR hub class in src/deepwiki-open-dotnet.ApiService/Hubs/GenerationHub.cs
- [ ] T066 [P] Implement GenerationHub.StartSession method (tests T065a must pass)
- [ ] T067 [P] Implement GenerationHub.SendPrompt streaming method returning IAsyncEnumerable<GenerationDelta> (tests T065a must pass)
- [ ] T068 [P] Implement GenerationHub.Cancel method (tests T065a must pass)
- [ ] T069 Add SignalR endpoint registration in src/deepwiki-open-dotnet.ApiService/Program.cs with hub route /hubs/generation
- [ ] T070 Add SignalR CORS policy configuration for local development and internal origins
- [ ] T071 Document SignalR usage in quickstart.md with TypeScript client example and add health check curl example

**Checkpoint**: SignalR hub available as convenience transport with contract parity (tests passing)

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Improvements affecting multiple user stories

- [ ] T072 [P] Update API documentation in README.md with links to quickstart.md and contracts/
- [ ] T072a 🧪 [P] Validate OpenTelemetry metrics export to Grafana using docker-compose test environment (run all tests, verify TTF/tokens/errors visible in Grafana dashboard)
- [ ] T073 [P] Add architecture diagram to specs/001-streaming-rag-service/ showing component relationships
- [ ] T073a 🧪 [P] Validate OpenAPI spec with Spectral linter in CI (ensure contracts/generation-service.yaml has no warnings)
- [ ] T074 Add validation for session expiration (1 hour inactivity) in SessionManager with automatic cleanup (tests T016a must pass)
- [ ] T075 [P] Add XML documentation comments to all public APIs (IGenerationService, IModelProvider, DTOs)
- [ ] T076 Optimize memory usage in StreamNormalizer to avoid buffering entire response (benchmark with 10K token response)
- [ ] T077 Add backpressure handling in GenerationService if client consumes deltas slowly (verify with slow client simulation test)
- [ ] T078 [P] Create Grafana dashboard JSON for TTF, tokens/sec, error rates in docs/observability/
- [ ] T079 [P] Add deployment guide in docs/deployment-checklist.md for Ollama + API service setup and document Owner field as "reserved for future use" in data-model.md
- [ ] T080 Run through all quickstart.md scenarios manually and validate against acceptance criteria in spec.md (create checklist: session creation, streaming, cancellation, curl demo, rate limiting)
- [ ] T081 Add integration with existing IVectorStore implementation to ensure RAG retrieval works end-to-end (integration test with sample documents)
- [ ] T081a 🧪 Create Agent Framework tool binding example in docs/examples/AgentToolExample.cs demonstrating queryKnowledge tool integration per Constitution Section VIII
- [ ] T082 Configure logging levels for production (Information) vs development (Debug) in appsettings.json
- [ ] T083 Add security headers middleware (X-Content-Type-Options, X-Frame-Options) for production deployment
- [ ] T084 🧪 Run full test suite validation: unit tests >=90% coverage, integration tests pass, contract tests pass, E2E scenarios validated (per Constitution Test-First gate)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion - BLOCKS all user stories
- **User Stories (Phase 3-5)**: All depend on Foundational phase completion
  - User Story 1 (P1) can start after Foundational - highest priority
  - User Story 2 (P2) can start after Foundational - builds on US1 endpoints
  - User Story 3 (P3) can start after Foundational - adds provider redundancy
- **SignalR Hub (Phase 6)**: Optional enhancement, depends on Foundational
- **Polish (Phase 7)**: Depends on desired user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: Can start after Foundational (Phase 2) - No dependencies on other stories
  - Creates core HTTP NDJSON baseline transport
  - Implements Ollama provider
  - Adds rate limiting and observability
  - **This is the MVP** - sufficient for initial deployment

- **User Story 2 (P2)**: Can start after Foundational (Phase 2) - Minimal dependency on US1
  - Validates US1 implementation works with simple clients
  - Adds filter support and better error handling
  - Can be implemented in parallel with US1 endpoints complete

- **User Story 3 (P3)**: Can start after Foundational (Phase 2) - No blocking dependency on US1/US2
  - Adds OpenAI provider as fallback
  - Implements provider selection and circuit breaker
  - Enhances observability and ops tooling
  - Can be implemented in parallel with dedicated team member

### Within Each User Story

**User Story 1 (P1) - Sequential Tasks**:
1. T019-T022: Provider implementation (Ollama) - can work in parallel
2. T023-T027: Service layer (depends on T019-T022)
3. T028-T030: Controller endpoints (depends on T023-T027)
4. T031-T039: Infrastructure wiring and instrumentation (depends on T028-T030)

**User Story 2 (P2) - Mostly Independent**:
- T040-T048 can proceed once US1 endpoints exist
- Most tasks are validation/documentation enhancements

**User Story 3 (P3) - Sequential Provider Tasks**:
1. T049-T051: OpenAI provider (parallel with US1 if staffed)
2. T052-T054: Provider selection logic (depends on T049-T051)
3. T055-T064: Error handling and observability (depends on T052-T054)

### Parallel Opportunities

**Phase 1 (Setup)**: All tasks can run in parallel

**Phase 2 (Foundational)**:
- T006-T011: All DTO classes (parallel)
- T013-T015: All entity/enum classes (parallel)
- T005, T012: Interfaces can be defined in parallel
- T016-T018: Infrastructure classes can proceed after interfaces complete

**Phase 3 (US1)**:
- T019 (StreamNormalizer) + T020-T022 (OllamaProvider) can run in parallel (different files)
- T034 (rate limiting middleware) can run in parallel with T028-T030 (controller endpoints)
- T036 (metrics wiring) + T037 (validation) + T038 (TTF) + T039 (logging) can run in parallel after core service complete

**Phase 5 (US3)**:
- T049-T051 (OpenAIProvider) can start in parallel with Phase 3 if staffed
- T059 (metrics export) + T060 (health endpoint) + T063 (logging) can run in parallel

**Phase 6 (SignalR)**: All tasks T065-T068 can run in parallel (different methods)

**Phase 7 (Polish)**: Most documentation tasks (T072, T073, T078, T079) can run in parallel

---

## Parallel Example: User Story 1

```bash
# After Phase 2 complete, launch in parallel:
# Developer A:
Task T019: "Implement StreamNormalizer in src/DeepWiki.Rag.Core/Streaming/StreamNormalizer.cs"

# Developer B:
Task T020-T022: "Implement OllamaProvider in src/DeepWiki.Rag.Core/Providers/OllamaProvider.cs"

# Wait for both, then Developer A:
Task T023-T027: "Implement GenerationService orchestration"

# Then Developer B (parallel with A's controller work):
Task T034-T035: "Implement rate limiting middleware and configuration"

# Developer A (parallel with B):
Task T028-T030: "Implement GenerationController endpoints"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. ✅ Complete Phase 1: Setup
2. ✅ Complete Phase 2: Foundational (CRITICAL - blocks all stories)
3. ✅ Complete Phase 3: User Story 1 (HTTP NDJSON + Ollama + rate limiting + metrics)
4. **STOP and VALIDATE**: 
   - Test with curl using quickstart.md examples
   - Verify SC-001 (TTF <500ms local), SC-002 (cancellation <200ms), SC-003 (seq integrity + done event)
   - Check metrics in OpenTelemetry exporter
5. **Deploy/Demo MVP** - Production-ready baseline

### Incremental Delivery

1. **Week 1-2**: Setup + Foundational → Foundation ready
2. **Week 3-4**: User Story 1 → Test independently → **Deploy/Demo MVP!**
3. **Week 5**: User Story 2 → Validate curl/fetch compatibility → Deploy update
4. **Week 6**: User Story 3 → Add OpenAI fallback + ops tooling → Deploy update
5. **Optional Week 7**: SignalR hub → Rich client support
6. **Week 8**: Polish → Performance tuning, documentation, final validation

Each story adds value without breaking previous functionality.

### Parallel Team Strategy (3+ Developers)

**Week 1**: All complete Setup + Foundational together

**Week 2-3** (after Foundational done):
- **Developer A**: User Story 1 core (T019-T027)
- **Developer B**: User Story 3 OpenAI provider (T049-T051) - can start early
- **Developer C**: User Story 1 API + middleware (T028-T039)

**Week 4**:
- **Developer A**: User Story 2 validation (T040-T048)
- **Developer B**: User Story 3 provider selection (T052-T064)
- **Developer C**: SignalR hub (T065-T071)

**Week 5**: All collaborate on Polish (T072-T083)

---

## Notes

- **[P] tasks** = different files, no dependencies, safe for parallel execution
- **[Story] label** maps task to specific user story for traceability and independent delivery
- Each user story should be independently completable and deployable
- Stop at any checkpoint to validate story independently against acceptance criteria in spec.md
- **MVP = Phase 1 + Phase 2 + Phase 3 (User Story 1)** - sufficient for production deployment with HTTP baseline
- User Stories 2 and 3 are enhancements adding validation and redundancy
- SignalR (Phase 6) is optional convenience for rich clients
- Commit frequently: after each task or logical group of related tasks
- Refer to [quickstart.md](quickstart.md) for curl demo examples and [contracts/](contracts/) for OpenAPI schema

---

## Summary

- **Total Tasks**: 108 (83 implementation + 25 test tasks)
- **MVP Tasks (Phase 1-3)**: 53 tasks (6 setup + 21 foundational + 26 US1 implementation+tests)
- **Enhancement Tasks (Phase 4-7)**: 55 tasks
- **Test Tasks** (marked 🧪): 25 tasks enforcing Test-First principle per Constitution
- **Parallel Opportunities**: 35+ tasks marked [P] across all phases
- **User Stories**: 3 independent stories (P1: Developer assistant, P2: CLI/curl, P3: Ops/admin)
- **MVP Scope**: User Story 1 provides complete baseline functionality (HTTP NDJSON, Ollama, rate limiting, observability) with comprehensive test coverage
- **Constitution Compliance**: ✅ Test-First enforced with TDD workflow, ✅ Agent Framework compatibility (T081a), ✅ EF Core reuse, ✅ Local-First ML, ✅ Observability validation
- **Estimated Timeline**: 5-7 weeks (1 developer with TDD), 3-4 weeks (3 developers with parallel execution)
- **Test Coverage Goals**: >=90% code coverage per Constitution, all integration tests passing, contract parity validated
