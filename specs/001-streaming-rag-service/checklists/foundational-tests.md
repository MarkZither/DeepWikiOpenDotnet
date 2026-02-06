# Foundational Test Tasks (TDD - Write First)

**Purpose**: Verify presence and quality of foundational/unit tests required by Phase 2 (Foundational).
**Created**: 2026-02-06
**Feature**: ../spec.md

---

## Requirement Completeness ✅

- [ ] CHK001 - Is there a contract test for the `IGenerationService` interface verifying signature, return type `IAsyncEnumerable<GenerationDelta>`, and cancellation support? [Completeness, tests/DeepWiki.Data.Abstractions.Tests/IGenerationServiceContractTests.cs]
- [ ] CHK002 - Are DTO serialization & validation tests present for `GenerationDelta`, `SessionRequest`, `SessionResponse`, `PromptRequest`, `CancelRequest` verifying JSON round-trip and data annotations? [Completeness, tests/deepwiki-open-dotnet.Tests/Models/GenerationDTOTests.cs]
- [ ] CHK003 - Is there a contract test for `IModelProvider` verifying `StreamAsync` signature, `IsAvailableAsync` return type and cancellation support? [Completeness, tests/DeepWiki.Rag.Core.Tests/IModelProviderContractTests.cs]
- [ ] CHK004 - Are `Session` entity validation tests present verifying timestamps, default status and expiration invariants? [Completeness, tests/DeepWiki.Rag.Core.Tests/Models/SessionTests.cs]
- [ ] CHK005 - Are `Prompt` entity validation tests present verifying default token count, status, and idempotencyKey storage? [Completeness, tests/DeepWiki.Rag.Core.Tests/Models/PromptTests.cs]
- [ ] CHK006 - Are `SessionManager` unit tests present covering session creation, prompt creation, idempotency key caching, expiration cleanup, and concurrent access safety? [Completeness, tests/DeepWiki.Rag.Core.Tests/Services/SessionManagerTests.cs]
- [ ] CHK007 - Are `GenerationMetrics` tests present verifying TTF method signature, record methods, and basic emission contract (method signatures present)? [Completeness, tests/DeepWiki.Rag.Core.Tests/Observability/GenerationMetricsTests.cs]

---

## Notes
- Tests were added to the repository under the paths above and tasks T005a–T017a in `tasks.md` were marked completed.
- Some deeper metric emission verification (capturing Meter output) is deferred to integration-level tests where a metrics pipeline (MeterListener / OpenTelemetry) is available.

---

**Checklist Status**: Draft — Run tests locally (prefer `dotnet test`) and ensure all tests pass in CI. Each run of `/speckit.checklist` must create a new checklist file to track progress.
