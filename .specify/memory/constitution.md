<!--
SYNC IMPACT REPORT
- Version change: 0.0.1 → 0.1.0 (MINOR: added ML snapshot, local-first model policy, observability, and security guidance)
- Modified Principles: Test‑First → strengthened testing requirements; Added Reproducibility & Determinism; Added Local‑First ML; Expanded Observability & Security
- Added Sections: LLM Policy & Snapshotting, Deterministic Replay & Streaming Parser Tests, Cost & Model Limits, Local Model Operation, Data Export & Audit Logs, Accessibility & i18n
- Removed Sections: none
- Templates requiring review/update: 
  - `.specify/templates/plan-template.md` (⚠ pending manual update: Constitution Check rules)
  - `.specify/templates/spec-template.md` (⚠ pending manual review)
  - `.specify/templates/tasks-template.md` (⚠ pending manual review)
  - `.github/agents/speckit.constitution.agent.md` (⚠ review: validation & version bump logic)
- Follow-up TODOs: create issues to (1) add `.specify/fixtures/llm-snapshots/` with examples & playback tests, (2) update templates to reflect snapshot and LLM rules, (3) add LLM cost dashboards and runbooks.
-->

# DeepWiki .NET Constitution

## Purpose & Scope
This Constitution governs the design, development, testing, and operation of the DeepWiki .NET implementation: a C#/.NET server using the Microsoft Agent Framework, Entity Framework with vector storage (SQL Server prioritized, Postgres supported), a Blazor frontend, and a local‑first ML strategy (Ollama first, OpenAI-compatible APIs next, cloud providers pluggable).
This document defines non-negotiable principles, governance rules, and operational policies that all contributors MUST follow.

## Core Principles

### I. Test‑First (NON‑NEGOTIABLE)
- Tests MUST be written before implementation for every new feature or bugfix. Unit tests (xUnit), component tests (bUnit), and E2E tests (Playwright) are required where applicable. PRs MUST include passing tests and meet CI thresholds before merging.
- Rationale: Ensures correctness and reduces regressions; streaming parser and prompt changes MUST include snapshot-based tests.

### II. Reproducibility & Determinism
- All LLM interactions that influence content or parsing MUST be recorded as deterministic snapshots. Playbacks of snapshots MUST reproduce parser behavior and be used in tests and debugging.
- Rationale: Streaming and nondeterministic outputs require reproducible inputs to reliably validate parsers and generation logic.

### III. Local‑First ML
- The project MUST prioritize local runtimes (Ollama and compatible) for development and testing. OpenAI-compatible APIs are a supported secondary option. Cloud provider adapters (Azure/AWS/GCP) MUST be pluggable and conform to a Provider abstraction to avoid vendor lock-in.
- Rationale: Local-first enables fast iteration, offline development, and lower cost for CI and dev workflows.

### IV. Observability & Cost Visibility
- Services MUST emit structured logs, metrics, and traces. LLM usage (calls, token counts, latency, errors) MUST be tracked and surfaced in dashboards to make cost and performance visible. Alerting MUST exist for abnormal usage or cost spikes.
- Rationale: LLM calls can incur significant cost and reliability risk; observability is required for incident response and cost control.

### V. Security & Privacy
- Secrets MUST be stored in secure stores and rotated regularly. Snapshots, logs, and caches that contain user data MUST be redacted of PII before long-term storage. Sensitive artifacts at rest MUST be encrypted.
- Rationale: Protect user privacy and comply with regulatory requirements.

### VI. Simplicity & Incremental Design
- Prefer simple, well-documented APIs and incremental changes. Breaking changes MUST be justified, documented, and follow the Governance procedure.
- Rationale: Easier maintenance and safer evolution of system contracts.

---

## Storage & Data Policies
- Primary vector storage: **SQL Server** vector type (preferred). **Postgres (pgvector)** MUST be supported as an alternative.
- Indexing & retrieval: Vector fields MUST be indexed; query performance expectations and index maintenance MUST be documented in EF migrations and PR descriptions.
- Backups & retention: Use platform-recommended backups for vector stores. Snapshot retention policies (short/medium/long) apply to LLM artifacts.
- EF Migrations: EF migrations MUST include a migration checklist (impact on indexes, data transformation, downtime expectations) and rollback steps. Migrations that touch vector fields MUST include performance benchmarks.

---

## LLM Policy & Snapshotting
- Provider precedence: Local-first (Ollama) MUST be default; OpenAI-compatible APIs are next; cloud providers are pluggable. Implementations MUST satisfy a Provider interface and be configurable.
- Prompt versioning: Prompt templates and prompt changes MUST be versioned in the repo with changelogs and rationale. Changes that affect generation or parsing MUST include snapshot comparisons.
- Snapshot recording: All LLM requests that impact wiki content or parsing MUST create a snapshot containing request metadata, streaming chunks with timestamps, response hash, and redaction info. Snapshots MUST be stored in `.specify/fixtures/llm-snapshots/` and referenced in tests.
- PII & retention: Snapshots MUST be scanned for PII and redacted before medium/long-term storage. Short-term raw captures MAY be allowed in secured environments but MUST be deleted per retention policy.

### Snapshot JSON Schema (reference)
Include this as a canonical example in the repo and use for fixtures.
```json
{
  "id": "snapshot-YYYYMMDD-0001",
  "created_at": "2026-01-12T15:04:05Z",
  "model": { "provider": "ollama", "id": "vicuna-13b", "version": "v2026-01" },
  "request": { "prompt": "...", "temperature": 0.0, "streaming": true, "metadata": { "feature": "wiki-page-generation", "commit": "abc123" } },
  "stream": [ { "time": "2026-01-12T15:04:06.100Z", "chunk": "This is", "role": "assistant" } ],
  "response_hash": "sha256:...",
  "redacted": { "fields": ["user_email", "phone"] },
  "retention_policy": { "tier": "short-term", "keep_days": 30 }
}
```

Retention tiers: short-term (30 days raw), medium-term (365 days redacted), long-term (archival, redacted and reviewed). Teams MUST document environmental tiers.

---

## Caching & Snapshot Playbacks
- Cache policy: Local wiki cache MUST be versioned and include references to snapshot IDs used to generate the entry. Invalidation triggers: content edit, prompt version change, manual invalidate, or explicit cache expiry.
- Snapshot playback: Tests and debugging tools MUST support playback of snapshots to reproduce parsing and rendering without live LLM calls. Snapshot-based tests MUST be part of CI for parsing logic.

---

## Frontend, Accessibility & i18n
- Blazor components MUST meet WCAG 2.1 AA conformance for major user flows. bUnit tests MUST cover key components and accessibility assertions.
- i18n: All UI strings MUST be localizable. Translation workflow and default English strings MUST be documented.

---

## Testing & CI Requirements
- Test suites: Unit (xUnit), component (bUnit), integration/contract (TestServer/in-memory), and E2E (Playwright).
- Gating: PRs MUST pass unit, component, and integration tests in CI. E2E tests SHOULD run on merge; critical features MUST include E2E coverage before release.
- Snapshot tests: Streaming parsers and prompt-dependent logic MUST include snapshot playbacks in tests.

---

## Observability & Cost Monitoring
- Logs: Use structured JSON logs with standard fields (timestamp, level, correlation_id, feature, snapshot_id).
- Metrics: Track LLM call counts, token usage, latency, error rates, and cache hit rates. Provide dashboards for cost and usage trends.
- Alerts & Runbooks: Set thresholds for abnormal LLM costs or error spikes and document runbook actions for alerts.

---

## Security & Compliance
- Secrets: Use platform secret stores; do NOT commit secrets to the repo. Enforce periodic rotation and least-privilege access.
- Encryption: Sensitive snapshots and caches MUST be encrypted at rest.
- Access controls: Limit raw snapshot access to authorized roles; audit access to sensitive artifacts.

---

## Governance & Versioning
- Versioning rules: MAJOR for backward-incompatible governance/principle removals or redefinitions; MINOR for added principles/sections; PATCH for clarifications and non-semantic fixes.
- This change: 0.0.1 → 0.1.0 (MINOR).
- Amendments: Amendments MUST be proposed via PR that updates this file, includes a Sync Impact Report, and details migration steps if required. Ratification requires approval from at least two maintainers.
- Dates MUST be ISO format (YYYY-MM-DD).

**Version**: 0.1.0 | **Ratified**: 2026-01-12 | **Last Amended**: 2026-01-12

---

## Contribution & PR Rules
- PR checklist: tests added/updated, snapshot updates (if generation changed) included, performance/migration impact documented (if applicable), diagrams or examples updated.
- Major changes (storage schema, EF migrations, vector index changes) MUST include a migration plan, performance validation, and a rollback plan.

---

## Templates & Sync Checklist
- Review and update `.specify/templates/plan-template.md` to include Constitution Check gates for snapshot and prompt changes.
- Audit `.specify/templates/spec-template.md` and `.specify/templates/tasks-template.md` to ensure required sections (observability, snapshots, security) are present.
- Add `.specify/fixtures/llm-snapshots/README.md` describing snapshot format, redaction, and playback instructions.

---

## Follow-up Tasks
- Create issues to: (1) add sample snapshot fixtures and playback tests, (2) update templates to align with new rules, (3) add LLM cost dashboards and runbooks.

---

*If a field is intentionally deferred, use `TODO(<FIELD_NAME>): explanation` and create a follow-up issue.*
[PRINCIPLE_1_DESCRIPTION]
<!-- Example: Every feature starts as a standalone library; Libraries must be self-contained, independently testable, documented; Clear purpose required - no organizational-only libraries -->

### [PRINCIPLE_2_NAME]












## Governance
- The Constitution supersedes other internal practices where stated. Amendments MUST include documentation, a migration plan (if applicable), and pass the amendment approval process described below.

- Amendment process: Propose changes via PR that update this file and include a Sync Impact Report that details version bump reasoning, affected templates, and follow-up migration tasks. Approval requires at least two maintainers.

- Compliance: Key PRs must verify compliance with this Constitution where applicable; reviewers MUST verify tests, snapshot updates, and migration plans as required.



<!-- Example: Version: 2.1.1 | Ratified: 2025-06-13 | Last Amended: 2025-07-16 -->
