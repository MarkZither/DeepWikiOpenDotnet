# Vector Store Service Layer — Planning Summary

**Status**: ✅ **READY FOR TASK GENERATION**  
**Branch**: `002-vector-store-service`  
**Created**: 2026-01-18  
**Session**: Specify → Clarify → Plan (Complete)

---

## Key Accomplishments

### Phase: Specify ✅
- **5 prioritized user stories** covering RAG query, ingestion, tokenization, provider support, and metadata filtering
- **14 functional requirements** with clear acceptance criteria
- **10 measurable success criteria** (performance, accuracy, coverage, compatibility)
- **Quality checklist** validated — all mandatory sections complete

### Phase: Clarify ✅
- **4 critical clarifications** resolved via structured Q&A:
  1. Embedding service resilience: **Retry + fallback caching** (FR-014)
  2. Document deduplication: **Store separately by repo+path** (FR-009)
  3. Provider scope: **3 providers for MVP** (OpenAI, Foundry, Ollama) — reduces scope from 5
  4. Metadata filtering: **SQL LIKE pattern matching** (FR-004, intuitive for file paths)
- **Constitution check passed** — design aligns with test-first, local-first ML, simplicity principles

### Phase: Plan ✅
- **Implementation plan** with technical context, architecture, and phases
- **Project structure** defined: 2 new class libraries (Data.Abstractions, Rag.Core) + ApiService updates
- **5 independent slices** for task breakdown (vector store, tokenization, embedding, ingestion, integration)
- **Effort estimate**: 16-21 days (4-5 weeks, 1-2 devs)
- **Database schema** with SQL Server vector type
- **API contracts** sketched for IVectorStore, ITokenizationService, IEmbeddingService
- **Constitution re-check passed** — all principles maintained post-design

---

## Quick Reference

### Architecture
```
DeepWiki.Data.Abstractions
  ├── IVectorStore
  ├── ITokenizationService
  ├── IEmbeddingService
  └── Models (DocumentEntity, VectorQueryResult, etc.)

DeepWiki.Rag.Core
  ├── VectorStore/ (SqlServerVectorStore with k-NN)
  ├── Tokenization/ (3 models, Chunker with 8192 token limit)
  ├── Embedding/ (OpenAI, Foundry, Ollama with retry+fallback)
  └── Ingestion/ (DocumentIngestionService)

deepwiki-open-dotnet.ApiService
  └── DI registration + future RAG endpoints
```

### Key Technical Decisions
- **Storage**: SQL Server 2025 vector(1536), Postgres pgvector as alternative
- **Providers**: OpenAI (API baseline), Foundry Local (production), Ollama (dev/test)
- **Resilience**: 3 retries, exponential backoff, cached/secondary embedding fallback
- **Filtering**: SQL LIKE patterns (e.g., `file_path LIKE '%.md'`)
- **Testing**: xUnit + in-memory SQLite for unit tests, test SQL Server for integration

### Deliverables (5 Slices)
| # | Slice | Days | Tests | Dependencies |
|---|-------|------|-------|--------------|
| 1 | Vector Store (k-NN queries) | 4-5 | Unit + integration | — |
| 2 | Tokenization (3 models, chunking) | 3-4 | Unit + parity tests | — |
| 3 | Embedding Factory (3 providers, retry) | 4-5 | Unit + provider tests | 1, 2 |
| 4 | Document Ingestion (upsert, chunking) | 3-4 | Unit + E2E flow | 1, 2, 3 |
| 5 | Integration & Docs (e2e, quickstart) | 2-3 | E2E + CI | 1, 2, 3, 4 |

---

## Files Generated

### Specification Artifacts
- ✅ `spec.md` — Feature specification with 5 user stories, 14 FRs, 10 success criteria
- ✅ `plan.md` — Implementation plan (technical context, architecture, phases)
- ✅ `checklists/requirements.md` — Specification quality checklist (PASSED)

### Specification Status
- **Scope**: Fully defined (5 P1 + P2 user stories, MVP provider set)
- **Clarifications**: All resolved (4/4 critical Q&As)
- **Constitution**: APPROVED (test-first, local-first, simple design)
- **Ready for**: Task generation (`/speckit.tasks`)

---

## Next: Task Generation

Run the following to break down into granular tasks:

```bash
.specify/scripts/bash/plan-to-tasks.sh  # (when available)
# OR manually run: /speckit.tasks
```

This will generate:
- `tasks.md` — 30-50 specific tasks across 5 slices
- Task dependencies, effort estimates, and test requirements
- CI/CD integration steps

---

## Success Metrics (from Specification)

| Metric | Target | Status |
|--------|--------|--------|
| Retrieval latency (p95) | <500ms for 10k docs | Planned |
| Token parity | ≥95% match to Python tiktoken | Planned |
| Embedding throughput | ≥50 docs/sec | Planned |
| Unit test coverage | ≥90% | Planned |
| Provider coverage | 3 (OpenAI, Foundry, Ollama) | Planned |
| Metadata filtering | SQL LIKE patterns | Designed |
| Architecture extensibility | Postgres support without code changes | Designed |

---

## Decision Log

| Date | Category | Decision | Rationale |
|------|----------|----------|-----------|
| 2026-01-18 | Resilience | Retry + fallback caching | Balance determinism with robustness |
| 2026-01-18 | Deduplication | Separate documents by repo+path | Simple, preserves context |
| 2026-01-18 | Providers | 3 for MVP (OpenAI, Foundry, Ollama) | Reduce scope, focus on Microsoft stack + local |
| 2026-01-18 | Filtering | SQL LIKE patterns | Intuitive for file paths, performant |
| 2026-01-18 | Architecture | 2 class libraries + interfaces | Clean separation, future extensibility |

---

## Risk Assessment

| Risk | Mitigation | Status |
|------|-----------|--------|
| SQL Server vector ops unfamiliar | Research spike on vector syntax, parameterization | Planned for Phase 0 |
| Token counting parity hard | Build comprehensive test suite vs. Python tiktoken | Planned for Slice 2 |
| Ollama API changes | Keep provider interface stable, abstract API calls | Mitigated by factory pattern |
| Concurrent write conflicts | Test atomicity, document conflict handling | Planned for Slice 4 |

---

**Ready to proceed with `/speckit.tasks` to generate detailed implementation tasks.**
