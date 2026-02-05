# .NET Migration Plan — Full Migration to ASP.NET Core + Microsoft Agent Framework

**Target**: Full C#/.NET application replacing current Python API. No remaining Python API endpoints; rely on EF Core + SQL Server 2025 (vector type) for vector storage. Use Microsoft Agent Framework for agent orchestration.

---

## Summary

This document outlines a full migration plan to port the `api/` Python package into a single, self-contained .NET 10 application using:

- ASP.NET Core for the HTTP/WebSocket server and streaming endpoints
- Microsoft Agent Framework (MAF) for agent orchestration and prompt management
- EF Core for persistence, including SQL Server 2025's native vector type (`vector(1536)`) via `SqlVector<float>`
- Provider adapters for model providers (Azure OpenAI, OpenAI, Bedrock, OpenRouter, Ollama, Dashscope, etc.)

Goals:
- Feature parity with existing Python API (endpoints, streaming behavior, RAG flows)
- No Python microservices required — embedding, ingestion, indexing, retrieval all handled in .NET
- Pluggable vector-store interface so you can target Postgres (pgvector) later if desired

---

## Key assumptions

- SQL Server 2025 supports a vector column type and EF Core mapping: e.g.

```csharp
[Column(TypeName = "vector(1536)")]
public SqlVector<float> Embedding { get; set; }
```

- Embedding dimensionality is fixed (1536 in this example) and consistent across providers.
- Streaming behavior (text/event-stream & WebSocket) will be implemented using ASP.NET Core with efficient backpressure handling.

---

## High-level architecture

1. ASP.NET Core Web API + SignalR for WebSocket endpoints
2. Microsoft Agent Framework for building agent flows and handling memory, chains, and tools
3. EF Core persistence with DocumentEntity + `SqlServerVectorStore` implementation
4. Provider adapters: `IMLClient` implementations for each model provider
5. Embedding and Tokenization services implemented in .NET (or via a vendor SDK)


### Component responsibilities
- Controllers: translate HTTP requests to domain-level commands and return SSE / streaming responses
- RAG Service: builds prompts, uses `IVectorStore` to retrieve nearest documents, composes the system prompt, calls model adapters
- Vector Store: Upsert/query/delete documents with embeddings and metadata
- Embedding service: calls model provider to create embeddings
- Agent Layer: orchestrates generator flows, enforces output format rules, and handles memory

---

## EF Core data model

```csharp
public class DocumentEntity
{
    public Guid Id { get; set; }
    public string RepoUrl { get; set; }
    public string FilePath { get; set; }
    public string Title { get; set; }
    public string Text { get; set; }

    [Column(TypeName = "vector(1536)")]
    public SqlVector<float> Embedding { get; set; }

    public string MetadataJson { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

DbContext snippet:

```csharp
public class RAGDbContext : DbContext
{
    public DbSet<DocumentEntity> Documents { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DocumentEntity>().HasKey(d => d.Id);
        modelBuilder.Entity<DocumentEntity>().Property(d => d.Embedding).HasColumnType("vector(1536)");
        modelBuilder.Entity<DocumentEntity>().HasIndex(d => d.RepoUrl);
        // Add any provider-specific indexes (ANN index creation managed via migration or script)
    }
}
```


---

## IVectorStore interface (abstraction)

```csharp
public interface IVectorStore
{
    Task UpsertAsync(DocumentEntity doc);
    Task<IEnumerable<DocumentEntity>> QueryAsync(float[] embedding, int k = 10, IDictionary<string,string> filters = null);
    Task DeleteAsync(Guid id);
    Task RebuildIndexAsync();
}
```

Implementation: `SqlServerVectorStore` will use EF Core for inserts and `FromSqlRaw` for nearest-neighbor queries (provider-specific ANN operator or function). Keep implementation testable and pluggable.

Note: exact SQL for nearest neighbor depends on SQL Server 2025 API—use parameterized raw SQL with a placeholder for ANN distance or use a CLR-provided function if required.

Example pseudocode (query):

```sql
-- PSEUDO-SQL: replace ANN_FUNC and syntax with SQL Server 2025 specifics
SELECT TOP (@k) *
FROM Documents
WHERE RepoUrl = @repo -- optional filter
ORDER BY ANN_DISTANCE(Embedding, @query_vector) ASC;
```

Use EF Core `FromSqlInterpolated` to run this safely and map results to `DocumentEntity`.

---

## Embedding & tokenization

- Implement an `IEmbeddingService` that calls chosen provider(s). Wrap providers behind adapters so you can swap providers.
- Implement a tokenizer utility to approximate `tiktoken` for chunking and token counting. For exact parity, consider a thin compatibility service or port of the tokenizer. Add unit tests for chunking parity.
- For batch embedding upserts, implement batching and retry/backoff strategies.

---

## Agent orchestration

- Recreate the `Generator` behavior from `adalflow` using Microsoft Agent Framework by:
  - Implementing MAF tools or skills for the model adapters and the vector store
  - Translating `DataClassParser` formatting checks to an output validation component in .NET
  - Implementing a `RAGAgent` or `RAGSkill` that accepts a user query, retrieves contexts, and calls model adapters with templated prompt
- Implement memory using EF (conversations table) or in-memory caches with persistence.

---

## Endpoints & streaming

- Recreate all FastAPI endpoints as ASP.NET Core controllers. Keep the same route contracts where feasible for frontend parity.
- Streaming HTTP NDJSON: implement via IAsyncEnumerable-based Response streaming as the baseline (compatible with curl/fetch).  
- Optional: provide a SignalR hub for richer client experiences (convenience for TypeScript/.NET clients), not required for MVP.

---

## Testing and validation

- Unit tests for:
  - EF mappings and `SqlVector<float>` round-trip serialization
  - `IVectorStore` queries (using in-memory provider or test SQL Server instance)
  - Embedding and tokenization parity (compare to representative outputs)
  - Agent output validation (ensures answer schema and formatting)
- Integration tests for:
  - End-to-end RAG query → embedding → retrieval → generation
  - Streamed responses and WebSocket flows
- Perf tests for retrieval latency and scaling under realistic corpora

---

## Migration checklist and milestones

1. Draft plan & repo skeleton (this file) — **1–2 days**
2. EF Core model + migrations, DB setup scripts (SQL for vector index) — **2–4 days**
3. Implement `IVectorStore` (SQL Server) + unit tests — **4–7 days**
4. Embedding service & tokenizer + end-to-end ingestion pipeline — **3–5 days**
5. Agent orchestration mapping & basic flow with a single provider — **4–7 days**
6. Streaming endpoints + SignalR WebSocket implementation — **2–4 days**
7. Integration tests + performance tuning — **3–7 days**
8. Documentation, deployment manifests, and cut-over plan — **2–3 days**

Total rough estimate: 4–8 weeks depending on team size and parallelization.

---

## Risks & mitigations

- **Embedding dimension mismatch**: enforce schema check during ingestion; maintain a migration plan if embeddings change.
- **Nearest-neighbor SQL syntax changes**: abstract query logic behind `IVectorStore` and make SQL-specific code isolated and tested.
- **Tokenization mismatch**: produce unit tests comparing tokenization and chunking behaviors; consider porting tokenizer for exact parity.
- **Performance**: tune index parameters and batch sizes; plan capacity testing early.

---

## Next steps

- Implement prototype `DocumentEntity`, `RAGDbContext`, and a minimal `SqlServerVectorStore` with sample query and migration.
- Build integration tests verifying retrieval accuracy on a small sample dataset.
- Map `adalflow` flows into MAF components with a simple agent proof-of-concept.

---

## MCP Server & Private Copilot Extension — Speckit-spec (MVP)

**Summary:** Provide an MCP streaming RAG service and a private Copilot-like extension in two parallel features to reach a focused MVP quickly and safely.

> Specify: This document contains two Speckit-ready feature specs (server + client). Each spec includes: a short feature description, clarify questions for product decisions, technical points, acceptance criteria, and a short file-level task list for implementation.

---

### Feature A — MCP streaming RAG (Server)

**Specify (one-liner):** Provide a transport-agnostic streaming generation service that performs RAG (retrieval + generation) and streams token deltas to clients with cancellation and finalization semantics.

**Clarify questions (product / policy decisions):**
1. Auth model: For MVP this is an internal-only service and will run **without auth**; later we may add a simple API key for revocation/audit or stronger tokens if needed.
2. Multi-tenancy: require per-repo isolation or per-org isolation? How strict must repo-scoped access be?
3. Local hosting: is running Ollama locally acceptable for dev and for on-prem deployments? (affects connection and security design)
4. Providers: Ollama primary and OpenAI fallback — allow configuring provider priority per-org?
5. Tokenization parity: what is the tolerance threshold for tokenization mismatch vs Python baseline (exact parity vs close-enough)?
6. Rate-limiting & usage: MVP can use simple IP-based limits and IP-based usage stats; per-org rate-limiting can be added later.

**Technical points & constraints:**
- Define `IGenerationService` surfaced as `IAsyncEnumerable<GenerationDelta>` to enable streaming and cancellation.
- `GenerationDelta` schema must support: incremental token text, role, event-type (`token|done|error`), promptId, sequence number, and optional metadata (e.g., token logits if needed).
- Hub contract: `StartSession`, `SendPrompt`, `Cancel`. Use request ids and idempotency keys for retry-safe behavior.
- Streaming transport must present a consistent JSON delta event format across transports (e.g., HTTP streaming, WebSocket frames or other stream semantics).
- Backpressure/cancellation: Respect client cancel requests immediately and abort provider streams promptly; emit a `done` or `error` delta.
- Observability: emit metrics for time-to-first-token, tokens/sec, error rates, and per-org cost estimates.

**Acceptance criteria (measurable):**
- TTF (time-to-first-token) < 500ms in typical dev setup for small prompts (local Ollama) or within 1s for remote providers.
- Token deltas streamed and final `done` event emitted within end of generation.
- Cancellation results in no further deltas and an emitted `done`/`error` event within 200ms of request.
- Unit & contract tests exist and pass: streaming adapter tests, tokenization parity sample, streaming contract tests.

**File-level deliverables (example tasks):**
- `DeepWiki.Data.Abstractions/IGenerationService.cs` — define APIs and DTOs (`GenerationRequest`, `GenerationDelta`).
- `DeepWiki.Rag.Core/Providers/OllamaGenerationClient.cs` — implement streaming parse & delta normalization.
- Server session/streaming endpoint integration that maps transport-agnostic session events to `IGenerationService`.
- Transport-agnostic streaming controller implementation (HTTP streaming-friendly) for clients that do not use persistent socket transports.
- Tests: `DeepWiki.Rag.Core.Tests/OllamaGenerationStreamingTests.cs`, `ApiService.Tests/StreamingContractTests.cs`.

---

### Feature B — Private Copilot extension (Client)

**Specify (one-liner):** Provide a minimal private Copilot-like extension (VS Code) that runs internally **without auth** for MVP, opens a streaming session to the MCP server (NDJSON HTTP streaming baseline or optional SignalR convenience), streams token deltas into an assistant pane, and supports accept/cancel actions.

**Clarify questions (UX / policy):**
1. Where should suggestions appear? Inline (editor) or assistant pane (or both)?
2. Does the extension auto-send file context, or should it request explicit user action to include context?
3. Privacy/consent: for internal-only MVP, per-repo opt-in is not required; document opt-in as a potential enterprise requirement later.
4. What minimal telemetry is permitted for MVP (errors only vs usage metrics)?

**Technical points & constraints:**
- The client will implement a streaming client that listens for `GenerationDelta` events and renders them in a streaming UI.
- Implement an exponential backoff reconnect and resume strategy for transient network failures; include a staged retry with user-visible status.
- Provide a local stub HTTP+SignalR server for client E2E tests and local development.
- UX: show live partial text, streaming cursor, and actions: Accept, Edit, or Cancel; Accept should insert suggestion into the editor at current cursor position.

**Acceptance criteria (measurable):**
- Extension connects and opens a streaming session **without auth** for MVP (internal-only).
- Streaming text appears within 300ms of server-first-token; final suggestion can be accepted to the editor with one action.
- E2E tests against stub server cover connect→prompt→stream→accept flows.

**File-level deliverables (example tasks):**
- `extensions/copilot-private/src/extension.ts` — command palette hooks and SignalR/streaming client wiring (no auth for MVP).
- `extensions/copilot-private/src/ui/AssistantPane.tsx` — streaming rendering and actions.
- `extensions/copilot-private/tests/streaming.e2e.ts` — headless E2E with stub.

---

### Shared contract (Speckit-ready)

**Server contract (transport-agnostic)**
```csharp
Task<string> StartSession(SessionRequest request);   // returns sessionId
Task SendPrompt(string sessionId, PromptRequest prompt); // send a prompt (with promptId)
Task Cancel(string sessionId, string promptId); // cancel the in-flight prompt
```

**GenerationDelta event (JSON)**
```json
{
  "promptId": "<id>",
  "type": "token|done|error",
  "seq": 42,
  "text": "...",
  "role": "assistant|system|user",
  "metadata": { }
}
```

**Error & idempotency rules:**
- Include `requestId` and `idempotencyKey` where applicable for retry safety.  
- Server errors use a structured `error` delta with code + message; client may retry with same idempotency key.

---

### Test matrix & telemetry
- Unit: streaming parser, provider cancellation behavior, tokenization parity samples.
- Contract: streaming schema tests (NDJSON/SignalR parity).  
- E2E: client ↔ stub ↔ server streaming round-trip.  
- Telemetry: metrics for TTF, tokens/sec, per-session bytes, and optional IP-based usage stats.

---

**Next steps (specced PRs)**
1. PR A (server): Add `IGenerationService`, DTOs, `OllamaGenerationClient` prototype, and streaming unit tests — implement an **HTTP NDJSON streaming** endpoint (IAsyncEnumerable-based) as the baseline.
2. PR B (server infra): Add an **optional** SignalR convenience hub and a stub server for client testing; add contract tests that verify NDJSON/SignalR parity.
3. PR C (client): Private extension prototype that connects **without auth** (internal-only) and streams from the stub; E2E tests.

---

## Speckit Commands (business-facing)

Below are concise, non-technical command templates you can paste into Speckit or use in product conversations to create clear, testable specs and plan artifacts.

### Specify (business / outcomes)
- Command example:
  specify "Private Copilot - Streaming Suggestions" \
    --goal "Deliver streaming code suggestions to internal developers without external exposure" \
    --acceptance "Extension shows streaming tokens within 300ms; final suggestion can be accepted into editor with one action; curl/fetch demo works" \
    --notes "MVP internal-only, no auth; NDJSON HTTP streaming baseline; SignalR optional convenience"

- Business-friendly requirements (use these as acceptance bullets):
  - The system must provide live suggestion text to the developer as it is generated.
  - Developers can accept a final suggestion into their editor with a single action.
  - The feature must work locally (developer laptop) and be consumable by simple tools (curl, fetch).
  - The MVP will operate internally only and must not rely on external auth integrations.

### Clarify (stakeholder questions)
- Command example:
  clarify "Private Copilot MVP" \
    --q "Is internal-only no-auth acceptable for MVP?" \
    --q "Should suggestions be presented inline or in an assistant pane?" \
    --q "Do we require per-repo opt-in for enterprise users?" \
    --q "Is NDJSON HTTP streaming acceptable as the baseline transport?"

- Suggested clarify questions (business focus):
  - Is running the service internal-only (no external access) acceptable for rollout?
  - Should the default UI be an assistant pane, inline suggestions, or both?
  - What minimal telemetry is acceptable for MVP (errors only or basic usage counts)?
  - Are IP-based rate-limits and IP-based usage stats sufficient for initial governance?

### Plan (architecture & implementation notes for the plan stage)
- Command example:
  plan "Private Copilot MVP" \
    --arch "HTTP NDJSON streaming baseline; optional SignalR hub for convenience; Ollama primary adapter; IAsyncEnumerable-based server streaming; NDJSON parity tests" \
    --ops "Internal network-only deployment; optional IP-based rate-limiting; basic TTF/throughput metrics"

- Architecture points (to include in the plan):
  - Transport-agnostic contract: `GenerationDelta` JSON lines (promptId, seq, type, text, role, metadata).
  - Baseline streaming transport: HTTP NDJSON using IAsyncEnumerable for server-side streaming.
  - Optional convenience transport: SignalR hub for richer TypeScript/.NET clients — not required for MVP.
  - Provider adapters: Ollama streaming adapter first; make provider selection configurable for later rollout.
  - Cancellation & idempotency: session/prompt ids and idempotency keys for reliable cancel/resume behavior.
  - Observability & ops: time-to-first-token (TTF), tokens/sec, per-session bytes, and optional IP-based usage stats.
  - Deployment: internal-only network with the ability to run Ollama locally or on internal hosts.

---

*This specification is formatted for Speckit input: concise specify statements, clear clarify questions, measurable acceptance criteria, and concrete technical tasks for immediate PR work.*
