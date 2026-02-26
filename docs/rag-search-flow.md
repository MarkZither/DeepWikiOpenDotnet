# RAG: Document Search to Ollama Flow

When a user submits a query the system performs **Retrieval-Augmented Generation (RAG)** — it searches for relevant document chunks first, then passes them as context to the LLM. There are two calls to Ollama: one for embeddings and one for text generation.

---

## Flow Diagram

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  Browser (Chat.razor)                                                       │
│  POST /api/generation/stream  { sessionId, prompt, topK, filters }          │
└───────────────────────────────────────┬─────────────────────────────────────┘
                                        │  NDJSON token stream (back)
                                        ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│  GenerationController  (ApiService)                                         │
│  → validates session via SessionManager                                     │
│  → calls GenerationService.GenerateAsync(sessionId, prompt, topK, filters)  │
└───────────────────────────────────────┬─────────────────────────────────────┘
                                        │
                                        ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│  GenerationService  (DeepWiki.Rag.Core)                                     │
│                                                                             │
│  STEP 1 — EMBED THE QUERY                                                   │
│  ┌──────────────────────────────────────────────────────────────────────┐   │
│  │  IEmbeddingService.EmbedAsync(promptText)                            │   │
│  │    → OllamaEmbeddingClient                                           │   │
│  │    → POST http://ollama:11434/api/embed                              │   │
│  │       { model: "nomic-embed-text", input: [promptText] }             │   │
│  │    ← float[1536]  (unit-normalised embedding vector)                 │   │
│  └──────────────────────────────────────────────────────────────────────┘   │
│                                        │                                    │
│  STEP 2 — VECTOR SEARCH                ▼                                    │
│  ┌──────────────────────────────────────────────────────────────────────┐   │
│  │  IVectorStore.QueryAsync(embedding, topK×3, filters)                 │   │
│  │    → PostgresVectorStoreAdapter                                      │   │
│  │    → PostgresVectorStore.QueryNearestAsync(...)                      │   │
│  │    → SQL (pgvector HNSW index, cosine distance):                     │   │
│  │        SELECT ... FROM "Documents"                                   │   │
│  │        WHERE "Embedding" IS NOT NULL                                 │   │
│  │          [AND "RepoUrl" = ... / LIKE ...]                            │   │
│  │        ORDER BY "Embedding" <=> '[0.1,0.2,...]'::vector              │   │
│  │        LIMIT topK×3                                                  │   │
│  │    ← List<VectorQueryResult> (ranked by similarity score)            │   │
│  └──────────────────────────────────────────────────────────────────────┘   │
│                                        │                                    │
│  STEP 3 — DEDUPLICATE CHUNKS           ▼                                    │
│  ┌──────────────────────────────────────────────────────────────────────┐   │
│  │  GroupBy (RepoUrl, FilePath)                                         │   │
│  │  Keep highest-scoring chunk per file                                 │   │
│  │  Take top 5 (MaxContextDocuments)                                    │   │
│  └──────────────────────────────────────────────────────────────────────┘   │
│                                        │                                    │
│  STEP 4 — BUILD SYSTEM PROMPT          ▼                                    │
│  ┌──────────────────────────────────────────────────────────────────────┐   │
│  │  systemPrompt = "Context documents:\n"                               │   │
│  │    + "- Title: {doc.Title}\n"                                        │   │
│  │    + "  Excerpt: {doc.Text[..500]}\n"   (per result)                 │   │
│  └──────────────────────────────────────────────────────────────────────┘   │
│                                        │                                    │
│  STEP 5 — LLM GENERATION               ▼                                    │
│  ┌──────────────────────────────────────────────────────────────────────┐   │
│  │  OllamaProvider.StreamAsync(promptText, systemPrompt)                │   │
│  │    → POST http://ollama:11434/api/generate (streaming)               │   │
│  │       {                                                              │   │
│  │         model: "<configured LLM>",                                   │   │
│  │         prompt: "<user's question>",                                 │   │
│  │         system: "Context documents:\n- Title: ...\n  Excerpt: ...",  │   │
│  │         stream: true                                                 │   │
│  │       }                                                              │   │
│  │    ← NDJSON token stream  { "response": "token", "done": false }    │   │
│  └──────────────────────────────────────────────────────────────────────┘   │
│                                        │                                    │
└────────────────────────────────────────┼────────────────────────────────────┘
                                         │ GenerationDelta stream
                                         ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│  GenerationController  — writes each delta as NDJSON line to Response       │
└───────────────────────────────────────┬─────────────────────────────────────┘
                                        │
                                        ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│  Browser (Chat.razor + NdJsonStreamParser)                                  │
│  — parses tokens → appends to ChatStateService.Messages → re-renders        │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Key Steps

| Step | What happens | Source file |
|---|---|---|
| **1. Embed query** | User's prompt is vectorised into 1536 floats by `nomic-embed-text` via Ollama's `/api/embed` | `src/DeepWiki.Rag.Core/Embedding/Providers/OllamaEmbeddingClient.cs` |
| **2. Vector search** | pgvector does an HNSW cosine-distance scan (`<=>`) to find the closest stored chunks | `src/DeepWiki.Data.Postgres/Repositories/PostgresVectorStore.cs` |
| **3. Deduplicate** | Results grouped by file; only the best-scoring chunk per file is kept; capped at 5 files | `src/DeepWiki.Rag.Core/Services/GenerationService.cs` |
| **4. System prompt** | Retrieved excerpts (≤500 chars each) are injected as the `system` field of the Ollama request | `src/DeepWiki.Rag.Core/Services/GenerationService.cs` |
| **5. LLM stream** | Ollama's `/api/generate` streams tokens back as NDJSON; user's actual question goes in `prompt` | `src/DeepWiki.Rag.Core/Providers/OllamaProvider.cs` |

---

## Collection Filtering

The `filters` passed by the UI (collection IDs / `repoUrl`) flow all the way from the `POST /api/generation/stream` request body through `GenerateAsync` into `QueryAsync`, where they become `WHERE "RepoUrl" = ...` clauses in the SQL — so selecting a document collection genuinely restricts which chunks are retrieved **before** the LLM ever sees them.

---

## Chunk Deduplication Detail

Because large files are split into multiple overlapping chunks (512-token windows, 128-token overlap — see `ChunkOptions`), a single file can produce many matching rows. Without deduplication, one large file could consume all `topK` context slots. The dedup step (Step 3) ensures the LLM context contains at most one representative excerpt per file, ranked by the highest cosine similarity score among that file's chunks.
