# Implementation Plan: MCP Streaming RAG Service

**Branch**: `001-streaming-rag-service` | **Date**: 2026-02-06 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/001-streaming-rag-service/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/commands/plan.md` for the execution workflow.

## Summary

Provide a transport-agnostic streaming generation service that performs RAG (retrieval + generation) and streams token deltas to clients with cancellation and finalization semantics. The service exposes `IGenerationService` with `IAsyncEnumerable<GenerationDelta>` for server-side streaming, supports HTTP NDJSON baseline transport and optional SignalR hub, implements provider adapters (Ollama local-first, OpenAI fallback), and includes IP-based rate-limiting, observability metrics (TTF, tokens/sec), and comprehensive test coverage.

## Technical Context

**Language/Version**: C# 13 / .NET 10  
**Primary Dependencies**: ASP.NET Core, Microsoft Agent Framework, Entity Framework Core 10, Ollama SDK, OpenAI SDK  
**Storage**: SQL Server 2025 (vector type) primary, PostgreSQL (pgvector) secondary  
**Testing**: xUnit (unit), bUnit (component), TestServer (integration), Playwright (E2E)  
**Target Platform**: Linux server (containerized), Windows (dev), cross-platform
**Project Type**: Web backend service with RAG orchestration  
**Performance Goals**: TTF <500ms (local Ollama), <1s (remote OpenAI); token throughput >50 tokens/sec; cancellation <200ms  
**Constraints**: Internal-only (no auth for MVP), single-tenant, IP-based rate-limiting, streaming with backpressure support  
**Scale/Scope**: MVP targets 10-50 concurrent sessions, <10K documents indexed, development/internal deployment

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Gate | Status | Notes |
|------|--------|-------|
| **Test-First** | ✅ PASS | Plan includes unit tests (streaming adapters, rate-limiting), contract tests (transport parity), integration tests (E2E RAG flow), E2E tests (stub server) |
| **EF Core & DB-Agnostic** | ✅ PASS | Uses existing `DeepWiki.Data` abstractions; IVectorStore already implemented; no new migrations needed for MVP |
| **Agent Framework Compatible** | ✅ PASS | `IGenerationService` designed for agent tool binding; JSON-serializable DTOs; error handling returns structured deltas |
| **Local-First ML** | ✅ PASS | Ollama as primary provider with configurable fallback to OpenAI; pluggable provider abstraction |
| **Observability & Cost** | ✅ PASS | Metrics for TTF, tokens/sec, error rates, per-session token counts; IP-based usage tracking |
| **Security & Privacy** | ⚠️ DEFERRED | No auth for MVP (internal-only); PII redaction and secret management deferred to post-MVP |
| **Snapshot & Determinism** | ⚠️ PARTIAL | Streaming parser tests included; full LLM snapshot recording deferred to Phase 2 |

**Re-check after Phase 1**: Verify `GenerationDelta` schema is JSON-serializable, IGenerationService is DI-compatible, and contract tests validate transport parity.

## Project Structure

### Documentation (this feature)

```text
specs/001-streaming-rag-service/
├── plan.md              # This file (Phase 0)
├── research.md          # Phase 0 output (provider APIs, streaming patterns)
├── data-model.md        # Phase 1 output (GenerationDelta, Session, Prompt)
├── quickstart.md        # Phase 1 output (curl demo, test scenarios)
├── contracts/           # Phase 1 output (OpenAPI/JSON schemas)
│   ├── generation-service.yaml
│   ├── generation-delta.schema.json
│   └── session-api.yaml
└── tasks.md             # Phase 2 output (/speckit.tasks - NOT created by this command)
```

### Source Code (repository root)

```text
src/
├── DeepWiki.Data.Abstractions/
│   ├── IGenerationService.cs         # NEW: Core streaming service contract
│   ├── DTOs/
│   │   ├── GenerationRequest.cs      # NEW: Request with prompt, sessionId, context
│   │   ├── GenerationDelta.cs        # NEW: Token delta event (type, seq, text, role)
│   │   ├── SessionRequest.cs         # NEW: Session creation request
│   │   └── PromptRequest.cs          # NEW: Prompt submission with idempotencyKey
│   └── IVectorStore.cs               # EXISTING: Reuse for RAG retrieval
│
├── DeepWiki.Rag.Core/
│   ├── Services/
│   │   ├── GenerationService.cs      # NEW: Orchestrates retrieval + generation
│   │   └── SessionManager.cs         # NEW: Manages session state
│   ├── Providers/
│   │   ├── IModelProvider.cs         # NEW: Provider abstraction
│   │   ├── OllamaProvider.cs         # NEW: Ollama streaming adapter
│   │   └── OpenAIProvider.cs         # NEW: OpenAI streaming adapter
│   └── Streaming/
│       ├── StreamNormalizer.cs       # NEW: Dedup, sequence, UTF-8 validation
│       └── TokenBuffer.cs            # NEW: Backpressure buffer
│
├── deepwiki-open-dotnet.ApiService/
│   ├── Controllers/
│   │   └── GenerationController.cs   # NEW: HTTP NDJSON streaming endpoint
│   ├── Hubs/
│   │   └── GenerationHub.cs          # NEW: SignalR hub (optional)
│   ├── Middleware/
│   │   └── RateLimitingMiddleware.cs # NEW: IP-based token-bucket limiter
│   └── Program.cs                    # MODIFY: Register services, middleware
│
tests/
├── DeepWiki.Rag.Core.Tests/
│   ├── OllamaProviderTests.cs        # NEW: Streaming parse, cancellation
│   ├── StreamNormalizerTests.cs      # NEW: Dedup, sequence validation
│   └── GenerationServiceTests.cs     # NEW: RAG orchestration unit tests
│
├── deepwiki-open-dotnet.Tests/
│   ├── StreamingContractTests.cs     # NEW: HTTP/SignalR parity validation
│   ├── RateLimitingTests.cs          # NEW: Rate-limit enforcement tests
│   └── GenerationE2ETests.cs         # NEW: End-to-end RAG flow (TestServer)
│
extensions/
└── copilot-private/
    ├── src/
    │   ├── extension.ts              # NEW: VS Code extension entry
    │   ├── services/
    │   │   └── StreamingClient.ts    # NEW: NDJSON/SignalR client
    │   └── ui/
    │       └── AssistantPane.tsx     # NEW: Streaming UI
    └── tests/
        └── streaming.e2e.ts          # NEW: E2E with stub server
```

**Structure Decision**: Backend service structure (Option 2 variant) chosen. Core abstractions live in `DeepWiki.Data.Abstractions`, RAG orchestration in `DeepWiki.Rag.Core`, API endpoints in `deepwiki-open-dotnet.ApiService`. Extension scaffolded in `extensions/copilot-private` for future client work (Phase 2 or later feature).

## Complexity Tracking

No Constitution violations requiring justification.

## Phase Breakdown

### Phase 0: Research & Technology Decisions ✅ COMPLETE

**Objective**: Resolve all NEEDS CLARIFICATION items and establish technology baseline

**Deliverable**: [research.md](research.md)

**Key Decisions Made**:
- Server-side streaming: `IAsyncEnumerable<T>` with ASP.NET Core native support
- HTTP baseline: Line-delimited JSON (NDJSON) for curl/fetch compatibility
- Optional SignalR: Convenience hub for rich clients (contract parity enforced)
- Provider abstraction: `IModelProvider` with Ollama and OpenAI adapters
- Ollama integration: HTTP streaming via `/api/generate` endpoint
- OpenAI integration: SDK streaming via `CompleteChatStreamingAsync`
- Stream normalization: Sequence assignment, deduplication, UTF-8 validation
- Rate limiting: Token-bucket algorithm with AspNetCoreRateLimit library
- Observability: .NET Metrics API with OpenTelemetry export (TTF, tokens/sec, errors)
- Cancellation: `CancellationToken` propagation with 30s provider stall timeout

**Risks Mitigated**: Ollama availability (Testcontainers for CI), provider stalls (timeout + error delta), UTF-8 encoding (explicit validation)

### Phase 1: Design & Contracts ✅ COMPLETE

**Objective**: Define data model, API contracts, and quickstart documentation

**Deliverables**:
- [data-model.md](data-model.md) - Entities: Session, Prompt, GenerationDelta, Document
- [contracts/generation-service.yaml](contracts/generation-service.yaml) - OpenAPI 3.1 specification
- [contracts/generation-delta.schema.json](contracts/generation-delta.schema.json) - JSON Schema for streaming events
- [quickstart.md](quickstart.md) - curl demos, test scenarios, troubleshooting

**Data Model Summary**:
- **Session**: SessionId (GUID), Owner (optional), Status, timestamps
- **Prompt**: PromptId (GUID), SessionId, Text, IdempotencyKey, Status, token count
- **GenerationDelta**: PromptId, Type (token/done/error), Seq, Text, Role, Metadata
- **Storage**: In-memory for MVP (ConcurrentDictionary), EF Core persistence deferred

**API Contracts**:
- `POST /api/generation/session` → Create session
- `POST /api/generation/stream` → Stream NDJSON deltas (HTTP baseline)
- `POST /api/generation/cancel` → Cancel in-flight prompt
- Rate limiting: 429 with X-RateLimit-* headers and Retry-After

**Acceptance**: GenerationDelta schema validated, JSON-serializable, Agent Framework-compatible

### Phase 2: Implementation & Testing (NEXT - use `/speckit.tasks`)

**Objective**: Implement service, providers, controllers, and comprehensive tests

**Tasks** (high-level, detailed breakdown in tasks.md):
1. **Abstractions Layer**
   - Define `IGenerationService`, `IModelProvider` interfaces
   - Implement DTOs (GenerationRequest, GenerationDelta, SessionRequest, PromptRequest)
   - Add validation attributes and JSON serialization config

2. **Provider Adapters**
   - Implement `OllamaProvider` with HTTP streaming client
   - Implement `OpenAIProvider` with SDK streaming integration
   - Implement `StreamNormalizer` for sequence/dedup/UTF-8 handling
   - Add provider health checks and availability detection

3. **RAG Orchestration**
   - Implement `GenerationService` orchestrating IVectorStore + IModelProvider
   - Implement `SessionManager` with in-memory session/prompt tracking
   - Add cancellation token wiring and timeout enforcement (30s)
   - Implement idempotency key checking for retry safety

4. **API Endpoints**
   - Implement `GenerationController` with NDJSON streaming endpoint
   - Implement rate-limiting middleware (AspNetCoreRateLimit)
   - Add OpenTelemetry metrics instrumentation (TTF, tokens/sec, errors)
   - Wire up DI registration in Program.cs

5. **Optional SignalR Hub**
   - Implement `GenerationHub` with streaming method
   - Add contract parity tests (HTTP vs SignalR delta sequences)

6. **Testing**
   - Unit tests: Provider adapters, stream normalizer, session manager
   - Integration tests: TestServer E2E flow, rate limiting enforcement
   - Contract tests: NDJSON/SignalR parity, schema validation
   - E2E tests: Stub server for client testing

**Estimate**: 2-3 weeks (1 developer, parallel unit/integration test development)

**Acceptance Criteria** (from spec.md):
- SC-001: TTF <500ms (local), <1s (remote)
- SC-002: Cancellation <200ms
- SC-003: Token deltas with increasing seq, done event emitted
- SC-004: Contract tests pass (HTTP/SignalR parity)
- SC-005: All unit/integration/E2E tests pass

---

## Next Steps

**Current Phase Complete**: Phase 1 (Design & Contracts)  
**Next Command**: `/speckit.tasks` to generate detailed task breakdown (tasks.md)  
**Ready for**: Implementation kickoff with clear contracts, data model, and acceptance criteria

**Implementation Order**:
1. Abstractions → Providers → Orchestration → API → Tests (dependency order)
2. Parallel: Unit tests alongside implementation (Test-First principle)
3. Integration tests after controller/middleware complete
4. E2E tests for validation before merge

**Constitution Re-Check** (post-design):
- ✅ GenerationDelta schema JSON-serializable
- ✅ IGenerationService compatible with Agent Framework DI
- ✅ Contract tests validate transport parity (HTTP/SignalR)
