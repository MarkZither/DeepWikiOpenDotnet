# Feature Specification: MCP Streaming RAG (Streaming RAG Service)

**Feature Branch**: `001-streaming-rag-service`  
**Created**: 2026-02-05  
**Status**: Draft  
**Input**: User description: "Provide a transport-agnostic streaming generation service that performs RAG (retrieval + generation) and streams token deltas to clients with cancellation and finalization semantics, as a basis for a private github copilot extension and other future clients"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Developer assistant (Priority: P1)
A developer uses the private Copilot extension to ask for a code suggestion or explanation. The extension opens a streaming session with the MCP Streaming RAG service, the service retrieves relevant context, and generates a streaming suggestion that the extension displays token-by-token. The developer can Accept or Cancel the suggestion.

**Why this priority**: This is the primary user value — immediate, interactive coding assistance that flows into developer workflows.

**Independent Test**: Start the stub server and extension against the HTTP streaming endpoint (line-delimited JSON); send a prompt and verify token-deltas are streamed to the client, accept inserts text into the editor.

**Acceptance Scenarios**:
1. **Given** a running MCP server and the extension, **When** the developer sends a prompt, **Then** the client receives token deltas (type=token) and a final done event (type=done).
2. **Given** a running prompt, **When** the developer presses Cancel, **Then** the server stops sending token deltas and emits a final done/error event within 200ms.

---

### User Story 2 - CLI / curl client (Priority: P2)
A developer runs a curl/fetch-based demo against the HTTP streaming endpoint (line-delimited JSON) and observes token deltas streamed in real time. This demonstrates the transport-agnostic contract for non-editor clients.

**Why this priority**: Ensures the contract works with simple clients and provides a low-friction integration path for scripts and tools.

**Independent Test**: Use curl to POST a prompt to the HTTP streaming endpoint (line-delimited JSON) and validate that incremental JSON lines conform to `GenerationDelta` schema and a `done` event is received.

**Acceptance Scenarios**:
1. **Given** the HTTP streaming endpoint (line-delimited JSON), **When** a prompt is sent via curl, **Then** JSON lines contain token deltas with increasing sequence numbers and a final done event.

---

### User Story 3 - Server admin / ops (Priority: P3)
An operator validates server behavior under cancellation, provider failures, and provider switchover. They inspect metrics for time-to-first-token (TTF), token/sec, and error rates.

**Why this priority**: Verifies observability and reliability expectations for production readiness.

**Independent Test**: Run provider failure scenarios (simulate provider disconnect) and validate the server emits an `error` delta and cleans up the session.

**Acceptance Scenarios**:
1. **Given** a provider failure mid-generation, **When** the provider disconnects, **Then** the server emits an `error` delta with a structured code and message and terminates the prompt.
2. **Given** a Cancel request, **When** Cancel is received, **Then** no further token deltas are emitted and a final `done`/`error` delta is emitted.

---

### Edge Cases
- What happens when a provider stalls (no tokens for X seconds)? Server should timeout and emit an `error` delta.
- How are duplicate prompts handled? Use idempotency keys on prompts to ensure retry-safe behavior.
- Partial provider dups (same token re-emitted) must be normalized by sequence numbers on the server.

## Requirements *(mandatory)*

### Functional Requirements
- **FR-001**: System MUST expose a transport-agnostic session API: `StartSession`, `SendPrompt`, `Cancel`, and must return stable `sessionId` and `promptId` values. **Acceptance**: StartSession returns a valid `sessionId` and contract tests verify mapping of deltas to `promptId`/`sessionId` for the lifecycle.
- **FR-002**: System MUST implement an `IGenerationService` (or equivalent) that exposes server-side streaming of `GenerationDelta` with support for incremental delivery and cancellation. **Acceptance**: tests verify ordered token deltas and cancel behaviour.
- **FR-003**: System MUST stream incremental `GenerationDelta` JSON events that include: `promptId`, `type` (`token|done|error`), `seq` (monotonic sequence), `text` (token delta), `role` (`assistant|system|user`), and optional `metadata`.
- **FR-004**: System MUST support prompt cancellation semantics: on `Cancel`, provider streams are aborted promptly and a final `done` or `error` delta is emitted within **200ms** of Cancel acknowledgment in typical dev environments.
- **FR-005**: System MUST normalize and validate provider token streams (sequence numbers, deduping, trimming incomplete UTF-8, etc.) before emitting to clients. **Acceptance**: contract tests verify deduping, correct sequencing, and UTF-8 safety on emitted deltas.
- **FR-006**: System MUST provide observable metrics: time-to-first-token (TTF), tokens-per-second, per-session token counts, and error rates. **Acceptance**: metrics emitted under synthetic test load include TTF, tokens/sec, and per-session counts; tests assert metric emission and reasonable value ranges.
- **FR-007**: System MUST support provider adapters and runtime provider selection (e.g., local provider runtime, OpenAI remote) and a configurable provider priority per deployment or per-org. **Decision**: default selection for MVP is **local-first** with optional managed/remote provider fallback. **Acceptance**: tests demonstrate provider selection, local-first behavior, and fallback to managed providers in a controlled environment.
- **FR-008**: System MUST include contract tests for parity across transports (HTTP streaming - line-delimited JSON vs persistent socket frames) and unit tests for tokenization parity. **Decision**: Tokenization parity is **required for critical flows** (e.g., schema outputs, code blocks, formatted answers) and **allowed to be approximate** for other flows with measurable tolerances. **Acceptance**: critical-flow parity unit tests must match the Python baseline exactly for representative critical prompts; non-critical parity tests must assert tolerance bounds (example: <=5% token-count difference or chunk boundary drift <=1 token). Contract tests must still verify `GenerationDelta` schema parity across transports.
- **FR-009**: System MUST provide clear error deltas with structured `code` and `message` and include `idempotencyKey` for retry safety. **Acceptance**: contract tests assert error delta schema (code + message) and idempotencyKey must be preserved across retries and reflected in server logs or response metadata.
- **FR-010**: System MUST provide a simple HTTP streaming (line-delimited JSON) baseline and an optional persistent socket-based hub for richer client experiences; tests must verify parity of event schema across transports. **Acceptance**: a curl demo against the HTTP streaming baseline streams `GenerationDelta` events and the parity tests validate the schema across transports.

*Unclear decisions requiring stakeholder input (limit 3):*
- **FR-CLAR-001**: Auth model: MVP will run internal-only with **no auth**. **Decision**: internal-only (no auth) chosen for MVP; future work may add API-key or per-org tokens for revocation, audit, and billing. **Acceptance**: server runs without auth in internal deployments and tests demonstrate expected behavior.
- **FR-CLAR-002**: Multi-tenancy scope: MVP will not enforce per-repo or per-org isolation (single-tenant internal deployment). **Decision**: no enforced multi-tenancy for MVP. **Implication**: per-org or per-repo isolation can be added later if required.
- **FR-CLAR-003**: Transport baseline: HTTP streaming (line-delimited JSON) is chosen as the required baseline for MVP. **Decision**: HTTP streaming baseline. **Acceptance**: curl/fetch demo works and contract tests verify parity with optional persistent socket hub.

### Key Entities *(include if feature involves data)*
- **Session**: { sessionId, owner (optional org/repo), createdAt, lastActiveAt }
- **Prompt**: { promptId, sessionId, text, idempotencyKey, status (in-flight|done|cancelled|error) }
- **GenerationDelta**: { promptId, type, seq, text, role, metadata }
- **Document**: { id, source, repoUrl, filePath, text, embedding (opaque), metadata }

## Success Criteria *(mandatory)*

### Measurable Outcomes
- **SC-001**: Time-to-first-token (TTF) is < 500ms for a local provider runtime (developer-hosted model) in typical dev setup and < 1s for remote providers in representative network conditions.
- **SC-002**: Cancellation completes (no further deltas) and a final `done`/`error` delta is emitted within 200ms of Cancel acknowledgement in typical dev setups.
- **SC-003**: Token deltas are emitted with increasing `seq` values, and a `done` event is always emitted at the end of generation.
- **SC-004**: Contract tests for HTTP streaming and persistent socket parity pass (HTTP streaming baseline must pass for MVP).
- **SC-005**: Unit and E2E tests exist and pass: streaming adapter tests, provider cancellation tests, CLI curl demo tests, and tokenization parity tests.

## Assumptions
- MVP is internal-only with **no authentication** and **no enforced multi-tenancy** (single-tenant internal deployment). Future work may add API-keys and per-org/repo isolation as needed.
- Embedding dimensionality and tokenization are managed by provider adapters. **Decision**: exact tokenization parity is required for *critical flows* (schema outputs, code blocks, formatted answers) and must be enforced via compatibility tests; for other flows, allow measurable tolerance and implement parity-sample tests (e.g., <=5% token-count difference, chunk boundary drift <=1 token).
- A local provider runtime (e.g., Ollama) is available in dev environments to meet the TTF targets.

## Clarifications
### Session 2026-02-06
- Q: Local hosting — is running Ollama locally acceptable for dev and on‑prem deployments? → A: Local-first with extension possibilities for managed/remote.
- Q: Tokenization parity tolerance — what level of parity is required vs the Python baseline? → A: **C — require parity only for critical flows (schema, code blocks, output formatting); allow close-enough behavior elsewhere with measurable tolerances and tests.**

**Acceptance**: Add unit tests that assert exact parity for a small set of critical prompts (schema outputs, code blocks, formatting) and additional parity-sample tests asserting tolerance bounds (e.g., <=5% token-count difference, chunk boundaries within 1 token) for non-critical flows.
## Deliverables & File-level Tasks
- Add `DeepWiki.Data.Abstractions/IGenerationService.cs` and DTOs (`GenerationRequest`, `GenerationDelta`).
- Implement provider adapter prototype (local provider runtime) in `DeepWiki.Rag.Core/Providers/` (stream parser + normalization).
- Implement provider selection configuration and tests for local-first behavior and managed-provider fallback.
- Add transport NDJSON controller endpoint (server-side streaming) and an optional persistent socket-based hub with parity tests.
- Tests: `DeepWiki.Rag.Core.Tests/OllamaGenerationStreamingTests.cs`, `ApiService.Tests/StreamingContractTests.cs`, `extensions/copilot-private/tests/streaming.e2e.ts` stub.

---

**Spec Status**: Ready for planning — stakeholder clarifications applied (no remaining [NEEDS CLARIFICATION] markers).

### User Story 1 - [Brief Title] (Priority: P1)

[Describe this user journey in plain language]

**Why this priority**: [Explain the value and why it has this priority level]

**Independent Test**: [Describe how this can be tested independently - e.g., "Can be fully tested by [specific action] and delivers [specific value]"]

**Acceptance Scenarios**:

1. **Given** [initial state], **When** [action], **Then** [expected outcome]
2. **Given** [initial state], **When** [action], **Then** [expected outcome]

---

### User Story 2 - [Brief Title] (Priority: P2)

[Describe this user journey in plain language]

**Why this priority**: [Explain the value and why it has this priority level]

**Independent Test**: [Describe how this can be tested independently]

**Acceptance Scenarios**:

1. **Given** [initial state], **When** [action], **Then** [expected outcome]

---

### User Story 3 - [Brief Title] (Priority: P3)

[Describe this user journey in plain language]

**Why this priority**: [Explain the value and why it has this priority level]

**Independent Test**: [Describe how this can be tested independently]

**Acceptance Scenarios**:

1. **Given** [initial state], **When** [action], **Then** [expected outcome]

---

[Add more user stories as needed, each with an assigned priority]

### Edge Cases

<!--
  ACTION REQUIRED: The content in this section represents placeholders.
  Fill them out with the right edge cases.
-->

- What happens when [boundary condition]?
- How does system handle [error scenario]?

## Requirements *(mandatory)*

<!--
  ACTION REQUIRED: The content in this section represents placeholders.
  Fill them out with the right functional requirements.
-->

### Functional Requirements

- **FR-001**: System MUST [specific capability, e.g., "allow users to create accounts"]
- **FR-002**: System MUST [specific capability, e.g., "validate email addresses"]  
- **FR-003**: Users MUST be able to [key interaction, e.g., "reset their password"]
- **FR-004**: System MUST [data requirement, e.g., "persist user preferences"]
- **FR-005**: System MUST [behavior, e.g., "log all security events"]

*No additional unspecified requirements remain.*

### Key Entities *(include if feature involves data)*

- **[Entity 1]**: [What it represents, key attributes without implementation]
- **[Entity 2]**: [What it represents, relationships to other entities]

## Success Criteria *(mandatory)*

<!--
  ACTION REQUIRED: Define measurable success criteria.
  These must be technology-agnostic and measurable.
-->

### Measurable Outcomes

- **SC-001**: [Measurable metric, e.g., "Users can complete account creation in under 2 minutes"]
- **SC-002**: [Measurable metric, e.g., "System handles 1000 concurrent users without degradation"]
- **SC-003**: [User satisfaction metric, e.g., "90% of users successfully complete primary task on first attempt"]
- **SC-004**: [Business metric, e.g., "Reduce support tickets related to [X] by 50%"]
