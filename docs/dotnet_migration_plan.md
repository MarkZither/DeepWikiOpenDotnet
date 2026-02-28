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

1. ~~Draft plan & repo skeleton (this file) — **1–2 days**~~ ✅ Complete
2. ~~EF Core model + migrations, DB setup scripts (SQL for vector index) — **2–4 days**~~ ✅ Complete (Postgres + SQL Server dual-provider)
3. ~~Implement `IVectorStore` (SQL Server) + unit tests — **4–7 days**~~ ✅ Complete (+ Postgres pgvector)
4. ~~Embedding service & tokenizer + end-to-end ingestion pipeline — **3–5 days**~~ ✅ Complete (3 providers, tiktoken, full pipeline)
5. ~~Agent orchestration mapping & basic flow with a single provider — **4–7 days**~~ ✅ Complete (RagService + Ollama + OpenAI-compat)
6. ~~Streaming endpoints + SignalR WebSocket implementation — **2–4 days**~~ ✅ Complete (NDJSON + SignalR hub)
7. ~~Integration tests + performance tuning — **3–7 days**~~ ✅ Substantially complete (91 test files)
8. Documentation, deployment manifests, and cut-over plan — **2–3 days** ⬜ Pending

**Original milestones 1–7 are complete.** See the **Revised Roadmap** in the "Next steps" section below for the forward-looking plan covering wiki, deep research, collection management, GraphRAG, model routing, podcast generation, and SCORM output.

---

## Risks & mitigations

- **Embedding dimension mismatch**: enforce schema check during ingestion; maintain a migration plan if embeddings change.
- **Nearest-neighbor SQL syntax changes**: abstract query logic behind `IVectorStore` and make SQL-specific code isolated and tested.
- **Tokenization mismatch**: produce unit tests comparing tokenization and chunking behaviors; consider porting tokenizer for exact parity.
- **Performance**: tune index parameters and batch sizes; plan capacity testing early.

---

## Completed milestones (as of 2026-03-01)

The following items from the original migration checklist are **done**:

- ✅ **Milestone 1** — Plan & repo skeleton
- ✅ **Milestone 2** — EF Core model + migrations (Postgres pgvector + SQL Server 2025 dual-provider)
- ✅ **Milestone 3** — `IVectorStore` implementations (SQL Server `VECTOR_DISTANCE`, Postgres `<=>` cosine, HNSW index)
- ✅ **Milestone 4** — Embedding service (OpenAI, Azure/Foundry, Ollama), tiktoken tokenization, full ingestion pipeline with chunking, retry, caching
- ✅ **Milestone 5** — `RagService` with multi-provider streaming, circuit breaker, stall timeout, idempotency
- ✅ **Milestone 6** — NDJSON HTTP streaming endpoint + SignalR `GenerationHub` + session management
- ✅ **Milestone 7 (partial)** — 91 test files across 7 projects (unit, integration, bunit, Testcontainers, performance)
- ✅ **Blazor UI** — RAG chat with streaming + multi-turn + citations + Markdown/KaTeX, document library with pagination/filter/delete, folder-based ingestion wizard
- ✅ **Infra** — Rate limiting, OTel metrics (TTF, tokens/sec), Prometheus endpoint, Polly resilience throughout

---

## Next steps — Revised Roadmap

The following phases are ordered by business priority. Git integration and additional LLM provider parity are deferred — the current Ollama + OpenAI-compatible provider coverage is sufficient for near-term use.

### Phase 1 — Wiki System (High Priority)

Build the wiki cache, generation, and export features that form the core product experience.

1. **`IWikiCacheService`** — CRUD for wiki structures (pages, sections, rootSections) per repo/collection. Use EF Core persistence with a `WikiEntity` + `WikiPageEntity` model (prefer DB over file-based cache for consistency with the existing data layer).
2. **Wiki API endpoints**:
   - `GET /api/wiki/{repoOrCollection}` — retrieve cached wiki data
   - `POST /api/wiki` — store generated wiki structure + pages
   - `DELETE /api/wiki/{repoOrCollection}` — delete a wiki entry
   - `GET /api/wiki/projects` — list all cached wiki projects with metadata
3. **Wiki export** — `POST /api/wiki/export` returning Markdown or JSON file download with TOC, page content, related-page links, and `Content-Disposition` header.
4. **Blazor Wiki page** — browse cached wiki projects, view generated pages with Markdown rendering, trigger wiki generation from a collection, navigate between pages/sections.
5. **Tests**: wiki service unit tests, export format tests, Blazor component tests (bunit).

### Phase 2 — Deep Research & Prompt Templates (High Priority)

Enable the multi-turn iterative research workflow and externalise prompt management.

6. **`IPromptTemplateService`** — load and render prompt templates with variable substitution (language, conversation history, context documents, user query). Port the 5 Python templates: RAG system prompt, RAG user prompt, Simple Chat, Deep Research Plan, Deep Research Update, Deep Research Conclusion.
7. **Deep Research orchestration** — detect `[DEEP RESEARCH]` prefix in user prompts, execute a multi-turn pipeline (iteration 1: Research Plan → iterations 2–4: Research Update with progressive deepening → iteration 5+: Final Conclusion/synthesis), stream deltas from each iteration to the client.
8. **File-scoped query support** — when a `filePath` is provided in the generation request, fetch the file content from the collection and inject it into the RAG context for focused answers.
9. **Blazor Deep Research UX** — visual indicator for research phase (plan → researching → conclusion), progress through iterations, collapsible iteration history.
10. **Tests**: prompt template rendering tests, Deep Research orchestration tests (mock provider, verify iteration sequencing and delta streaming), file-scoped query integration tests.

### Phase 3 — Arbitrary File & Content Ingestion (High Priority)

Allow users to add product documentation, Azure DevOps work items, and other arbitrary content to collections for enhanced RAG context.

11. **Collection model** — formalise `Collection` as a first-class entity (name, description, owner, source type: `repo | upload | azuredevops | url`). Add `CollectionEntity` + EF migration.
12. **Arbitrary file upload endpoint** — `POST /api/collections/{id}/documents/upload` accepting multipart file uploads (PDF, Markdown, Word, plain text, HTML). Parse and chunk each file type with appropriate extractors.
13. **URL content ingestion** — `POST /api/collections/{id}/documents/url` accepting a URL; fetch, extract readable content (HTML → text), chunk, embed, and upsert.
14. **Azure DevOps integration** — `POST /api/collections/{id}/documents/azuredevops` accepting an ADO org/project/query; fetch work items (user stories, bugs, tasks, epics) via the Azure DevOps REST API, convert to text, chunk, embed, upsert. Support WIQL queries for flexible item selection.
15. **Blazor Collection Manager** — UI for creating/managing collections, uploading files, pasting URLs, connecting ADO projects. Show ingestion progress and document counts per source type.
16. **Tests**: file-type parser tests (PDF, DOCX, HTML extraction), collection CRUD tests, ADO client mock tests.

### Phase 4 — GraphRAG & Code Graph RAG (Medium Priority)

Improve retrieval relevance by adding graph-based knowledge representations alongside vector search.

17. **Knowledge graph extraction** — build an `IGraphExtractor` that processes ingested documents to extract entities (concepts, classes, functions, APIs) and relationships. Store as a lightweight graph model in the DB (nodes + edges tables) or use a graph-aware index.
18. **Code graph construction** — for code files, parse AST/symbol information to build a code graph (call graphs, inheritance, module dependencies). Use Roslyn for C#, tree-sitter or similar for other languages. Store relationships as graph edges linked to document chunks.
19. **Hybrid retrieval** — extend `RagService` to combine vector similarity search with graph traversal (e.g., retrieve neighbours of top-K hits, follow call-graph edges for code questions). Implement a `IRetrievalStrategy` abstraction with `VectorOnly`, `GraphOnly`, and `Hybrid` modes.
20. **Graph-aware prompt enrichment** — include graph context (related entities, dependency paths) in the generation prompt to improve answer grounding.
21. **Tests**: graph extraction unit tests, hybrid retrieval accuracy benchmarks, code graph parser tests.

### Phase 5 — Dynamic Model Routing (Medium Priority)

Intelligently select the best model for each query based on complexity, domain, and cost.

22. **`IModelRouter`** — classify incoming prompts (complexity, domain, code vs prose, language) and route to the optimal model/provider. Implement rules-based routing first (e.g., simple factual → small/fast model, deep research → large model, code → code-specialised model).
23. **Cost & latency tracking** — extend `GenerationMetrics` to record per-model cost estimates and latency percentiles. Surface in observability dashboard.
24. **Routing configuration** — admin-configurable routing rules in `appsettings.json` or a `routing.json` config file (model preferences per query type, cost budgets, fallback chains).
25. **Tests**: routing classification unit tests, cost tracking integration tests.

### Phase 6 — Podcast Generation (Lower Priority)

Generate NotebookLM-style audio podcasts from wiki topics and user search history.

26. **`IPodcastService`** — given a set of wiki pages or search results, generate a conversational podcast script (two-speaker dialogue format) using the LLM. Apply a podcast-specific prompt template with speaker roles, topic transitions, and summary/recap structure.
27. **Text-to-speech integration** — integrate with Azure Cognitive Services Speech SDK (or similar TTS provider) to convert the podcast script to audio. Support multiple voice profiles for the two-speaker format.
28. **Podcast API** — `POST /api/podcasts/generate` accepting topic/collection/search-context, returning a podcast job ID. `GET /api/podcasts/{id}` to retrieve status and download the audio file when ready.
29. **Blazor Podcast UI** — trigger podcast generation from wiki pages or search results, show generation progress, inline audio player for playback.
30. **Tests**: podcast script generation tests (verify dialogue structure, speaker alternation), TTS integration tests.

### Phase 7 — SCORM Package Generation for Workday Learning (Lower Priority)

Produce SCORM-compliant learning packages from wiki topics for import into Workday Learning.

31. **`IScormPackageService`** — given wiki pages, generate a SCORM 1.2 or SCORM 2004 package. Structure content as a multi-page course with: learning objectives (derived from wiki section headings), content pages (wiki content rendered as HTML), knowledge-check questions (LLM-generated from the content), and a completion quiz.
32. **SCORM manifest generation** — produce `imsmanifest.xml` with proper organization, resource, and sequencing elements. Package all HTML content, CSS, JavaScript (SCORM API wrapper), and media assets into a conformant ZIP.
33. **Quiz generation** — use the LLM to generate multiple-choice and true/false questions from wiki content. Include distractors, correct-answer explanations, and map to learning objectives.
34. **SCORM API wrapper** — include a lightweight JavaScript SCORM runtime communication layer (`SCORMAdapter.js`) that handles `LMSInitialize`, `LMSSetValue` (score, completion status, suspend data), and `LMSFinish` for Workday Learning compatibility.
35. **SCORM API endpoints** — `POST /api/scorm/generate` accepting wiki topic/collection ID and course metadata (title, description, passing score). `GET /api/scorm/{id}` to download the generated `.zip` package.
36. **Blazor SCORM UI** — configure course parameters (title, passing score, question count), select wiki topics to include, preview course structure, download the SCORM package.
37. **Tests**: SCORM manifest XML validation tests, quiz generation quality tests, SCORM package structure conformance tests (validate against SCORM spec), Workday Learning import smoke test documentation.

### Quick Wins (Do Anytime)

38. **Wire `SecurityHeadersMiddleware`** — add `app.UseSecurityHeaders()` to the API pipeline.
39. **Register `SessionCleanupService`** — add `builder.Services.AddHostedService<SessionCleanupService>()`.
40. **Replace template pages** — remove Counter/Weather scaffolding, add a proper home page with project overview and navigation to Wiki, Chat, and Documents.
41. **Add root health check** — `GET /health` returning service info, timestamp, and component health.

---

## MCP Server & Private Copilot Extension — Speckit-spec (MVP)

**Summary:** Provide an MCP streaming RAG service and a private github Copilot extension in two parallel features to reach a focused MVP quickly and safely.

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
