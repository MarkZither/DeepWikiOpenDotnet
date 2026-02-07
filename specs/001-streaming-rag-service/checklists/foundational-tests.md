# Foundational Test Tasks (TDD - Write First)

**Purpose**: Verify presence and quality of foundational/unit tests required by Phase 2 (Foundational).
**Created**: 2026-02-06
**Feature**: ../spec.md

---

## Requirement Completeness ✅

- [X] CHK001 - Is there a contract test for the `IGenerationService` interface verifying signature, return type `IAsyncEnumerable<GenerationDelta>`, and cancellation support? [Completeness, tests/DeepWiki.Data.Abstractions.Tests/IGenerationServiceContractTests.cs]
- [X] CHK002 - Are DTO serialization & validation tests present for `GenerationDelta`, `SessionRequest`, `SessionResponse`, `PromptRequest`, `CancelRequest` verifying JSON round-trip and data annotations? [Completeness, tests/deepwiki-open-dotnet.Tests/Models/GenerationDTOTests.cs]
- [X] CHK003 - Is there a contract test for `IModelProvider` verifying `StreamAsync` signature, `IsAvailableAsync` return type and cancellation support? [Completeness, tests/DeepWiki.Rag.Core.Tests/IModelProviderContractTests.cs]
- [X] CHK004 - Are `Session` entity validation tests present verifying timestamps, default status and expiration invariants? [Completeness, tests/DeepWiki.Rag.Core.Tests/Models/SessionTests.cs]
- [X] CHK005 - Are `Prompt` entity validation tests present verifying default token count, status, and idempotencyKey storage? [Completeness, tests/DeepWiki.Rag.Core.Tests/Models/PromptTests.cs]
- [X] CHK006 - Are `SessionManager` unit tests present covering session creation, prompt creation, idempotency key caching, expiration cleanup, and concurrent access safety? [Completeness, tests/DeepWiki.Rag.Core.Tests/Services/SessionManagerTests.cs]
- [X] CHK007 - Are `GenerationMetrics` tests present verifying TTF method signature, record methods, and basic emission contract (method signatures present)? [Completeness, tests/DeepWiki.Rag.Core.Tests/Observability/GenerationMetricsTests.cs]

---

## Notes
- Tests for each checklist item have been added and are present at the referenced paths. Integration-level metric emission validation remains in the integration tests (MeterListener / OpenTelemetry) where appropriate.

---

**Checklist Status**: PASS — All foundational checklist items have corresponding unit/contract tests in the repo. Run `dotnet test` locally or in CI to confirm there are no regressions before merging.
