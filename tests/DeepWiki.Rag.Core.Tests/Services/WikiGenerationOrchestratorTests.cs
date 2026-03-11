using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DeepWiki.Data.Abstractions;
using DeepWiki.Data.Abstractions.Entities;
using DeepWiki.Data.Abstractions.Interfaces;
using DeepWiki.Data.Abstractions.Models;
using DeepWiki.Rag.Core.Models;
using DeepWiki.Rag.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace DeepWiki.Rag.Core.Tests.Services;

/// <summary>
/// Unit tests for WikiGenerationOrchestrator using mocked dependencies.
/// Tests are written TDD-first (will fail until T054 is implemented).
/// </summary>
public class WikiGenerationOrchestratorTests
{
    // ── Test doubles ─────────────────────────────────────────────────────────

    private readonly Mock<IWikiRepository> _repoMock = new();
    private readonly Mock<IGenerationService> _generationMock = new();
    private readonly Mock<IVectorStore> _vectorStoreMock = new();
    private readonly Mock<IEmbeddingService> _embeddingMock = new();
    private readonly SessionManager _sessionManager = new();
    private readonly IOptions<WikiGenerationOptions> _options =
        Options.Create(new WikiGenerationOptions { Mode = "sequential", MaxTocRetries = 2 });

    private WikiGenerationOrchestrator CreateOrchestrator() =>
        new WikiGenerationOrchestrator(
            _repoMock.Object,
            _generationMock.Object,
            _sessionManager,
            _vectorStoreMock.Object,
            _embeddingMock.Object,
            _options);

    // ── Helper: build a streaming LLM response ───────────────────────────────

    private static IAsyncEnumerable<GenerationDelta> MakeTocStream(string tocJson)
    {
        return BuildStream("session-test", tocJson);
    }

    private static IAsyncEnumerable<GenerationDelta> MakePageStream(string content)
    {
        return BuildStream("session-test", content);
    }

    private static async IAsyncEnumerable<GenerationDelta> BuildStream(
        string promptId,
        string text,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new GenerationDelta { PromptId = promptId, Type = "token", Role = "assistant", Text = text };
        yield return new GenerationDelta { PromptId = promptId, Type = "done", Role = "assistant" };
    }

    private const string ValidTocJson = """
        {
          "sections": [
            {
              "sectionPath": "Overview",
              "pages": [
                { "title": "Introduction", "keywords": ["intro"] }
              ]
            }
          ]
        }
        """;

    private const string ValidPageContent =
        "## Introduction\n\nThis is the intro page.\nRELATED_PAGES: []";

    private void SetupHappyPath(string tocJson = ValidTocJson, string pageContent = ValidPageContent)
    {
        // Embedding returns zero vector
        _embeddingMock.Setup(e => e.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[1536]);

        // Vector store returns empty results
        _vectorStoreMock.Setup(v => v.QueryAsync(It.IsAny<float[]>(), It.IsAny<int>(),
                It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<VectorQueryResult>)new List<VectorQueryResult>());

        // First generation call returns TOC; subsequent calls return page content
        var callCount = 0;
        _generationMock.Setup(g => g.GenerateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<Dictionary<string, string>?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns((string _, string _, int _, Dictionary<string, string>? _, string? _, CancellationToken ct) =>
            {
                var idx = Interlocked.Increment(ref callCount);
                return idx == 1 ? MakeTocStream(tocJson) : MakePageStream(pageContent);
            });

        // Repo: wiki creation returns the wiki
        _repoMock.Setup(r => r.CreateWikiAsync(It.IsAny<WikiEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WikiEntity w, CancellationToken _) => { w.Id = Guid.NewGuid(); return w; });

        // Repo: upsert returns the page
        _repoMock.Setup(r => r.UpsertPageAsync(It.IsAny<WikiPageEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WikiPageEntity p, CancellationToken _) => { p.Id = p.Id == Guid.Empty ? Guid.NewGuid() : p.Id; return p; });

        // Repo: no concurrent generation
        _repoMock.Setup(r => r.ExistsGeneratingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _repoMock.Setup(r => r.UpdateWikiStatusAsync(It.IsAny<Guid>(), It.IsAny<WikiStatus>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repoMock.Setup(r => r.SetRelatedPagesAsync(It.IsAny<Guid>(), It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    // ── Phase 1: TOC generation ───────────────────────────────────────────────

    [Fact]
    public async Task GenerateAsync_ValidRequest_EmitsWikiCreatedEvent()
    {
        SetupHappyPath();
        var orchestrator = CreateOrchestrator();
        var request = new WikiGenerationRequest { CollectionId = "col1", Name = "Test Wiki" };

        var events = await CollectEventsAsync(orchestrator, request);

        events.Should().Contain(e => e.EventType == WikiGenerationProgress.EventWikiCreated);
    }

    [Fact]
    public async Task GenerateAsync_ValidRequest_EmitsTocCompleteEvent()
    {
        SetupHappyPath();
        var orchestrator = CreateOrchestrator();
        var request = new WikiGenerationRequest { CollectionId = "col1", Name = "Test Wiki" };

        var events = await CollectEventsAsync(orchestrator, request);

        events.Should().Contain(e => e.EventType == WikiGenerationProgress.EventTocComplete);
    }

    [Fact]
    public async Task GenerateAsync_ValidRequest_Phase1PersistsWikiWithGeneratingStatus()
    {
        SetupHappyPath();
        var orchestrator = CreateOrchestrator();
        var request = new WikiGenerationRequest { CollectionId = "col1", Name = "Test Wiki" };

        await CollectEventsAsync(orchestrator, request);

        _repoMock.Verify(r => r.CreateWikiAsync(
            It.Is<WikiEntity>(w => w.Status == WikiStatus.Generating),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenerateAsync_ValidRequest_Phase2EmitsPageStartAndComplete()
    {
        SetupHappyPath();
        var orchestrator = CreateOrchestrator();
        var request = new WikiGenerationRequest { CollectionId = "col1", Name = "Test Wiki" };

        var events = await CollectEventsAsync(orchestrator, request);

        events.Should().Contain(e => e.EventType == WikiGenerationProgress.EventPageStart);
        events.Should().Contain(e => e.EventType == WikiGenerationProgress.EventPageComplete);
    }

    [Fact]
    public async Task GenerateAsync_ValidRequest_EmitsGenerationCompleteAtEnd()
    {
        SetupHappyPath();
        var orchestrator = CreateOrchestrator();
        var request = new WikiGenerationRequest { CollectionId = "col1", Name = "Test Wiki" };

        var events = await CollectEventsAsync(orchestrator, request);

        events.Last().EventType.Should().Be(WikiGenerationProgress.EventGenerationComplete);
    }

    [Fact]
    public async Task GenerateAsync_ValidRequest_EventOrderIsCorrect()
    {
        SetupHappyPath();
        var orchestrator = CreateOrchestrator();
        var request = new WikiGenerationRequest { CollectionId = "col1", Name = "Test Wiki" };

        var events = await CollectEventsAsync(orchestrator, request);
        var types = events.Select(e => e.EventType).ToList();

        var wikiCreatedIdx = types.IndexOf(WikiGenerationProgress.EventWikiCreated);
        var tocCompleteIdx = types.IndexOf(WikiGenerationProgress.EventTocComplete);
        var pageStartIdx = types.IndexOf(WikiGenerationProgress.EventPageStart);
        var generationCompleteIdx = types.LastIndexOf(WikiGenerationProgress.EventGenerationComplete);

        wikiCreatedIdx.Should().BeLessThan(tocCompleteIdx);
        tocCompleteIdx.Should().BeLessThan(pageStartIdx);
        pageStartIdx.Should().BeLessThan(generationCompleteIdx);
    }

    [Fact]
    public async Task GenerateAsync_ValidRequest_UpsertPageCalledWithOkStatus()
    {
        SetupHappyPath();
        var orchestrator = CreateOrchestrator();
        var request = new WikiGenerationRequest { CollectionId = "col1", Name = "Test Wiki" };

        await CollectEventsAsync(orchestrator, request);

        _repoMock.Verify(r => r.UpsertPageAsync(
            It.Is<WikiPageEntity>(p => p.Status == PageStatus.OK),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    // ── TOC parse failure with retry ──────────────────────────────────────────

    [Fact]
    public async Task GenerateAsync_TocParseFailsFirstAttempt_RetriesAndSucceeds()
    {
        SetupHappyPath();

        var callCount = 0;
        _generationMock.Setup(g => g.GenerateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<Dictionary<string, string>?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns((string _, string _, int _, Dictionary<string, string>? _, string? _, CancellationToken _) =>
            {
                var idx = Interlocked.Increment(ref callCount);
                // First call returns invalid JSON (will fail to parse)
                if (idx == 1) return MakeTocStream("not valid json");
                // Second call returns valid TOC
                if (idx == 2) return MakeTocStream(ValidTocJson);
                // Page generation calls
                return MakePageStream(ValidPageContent);
            });

        var orchestrator = CreateOrchestrator();
        var request = new WikiGenerationRequest { CollectionId = "col1", Name = "Test Wiki" };

        var events = await CollectEventsAsync(orchestrator, request);

        // Should succeed after retry
        events.Should().Contain(e => e.EventType == WikiGenerationProgress.EventTocComplete);
        events.Last().EventType.Should().Be(WikiGenerationProgress.EventGenerationComplete);
        // Generation service called at least twice for TOC (1 fail + 1 succeed) + 1 for page
        _generationMock.Verify(g => g.GenerateAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<Dictionary<string, string>?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.AtLeast(3));
    }

    // ── Page generation failure handling ─────────────────────────────────────

    [Fact]
    public async Task GenerateAsync_PageGenerationFails_EmitsPageErrorAndContinues()
    {
        var twoPageToc = """
            {
              "sections": [
                {
                  "sectionPath": "S1",
                  "pages": [
                    { "title": "Page A", "keywords": [] },
                    { "title": "Page B", "keywords": [] }
                  ]
                }
              ]
            }
            """;

        SetupHappyPath(tocJson: twoPageToc);

        var callCount = 0;
        _generationMock.Setup(g => g.GenerateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<Dictionary<string, string>?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns((string _, string _, int _, Dictionary<string, string>? _, string? _, CancellationToken _) =>
            {
                var idx = Interlocked.Increment(ref callCount);
                if (idx == 1) return MakeTocStream(twoPageToc); // TOC
                if (idx == 2) return ThrowingStream<GenerationDelta>(new Exception("LLM error for Page A"));
                return MakePageStream(ValidPageContent); // Page B succeeds
            });

        var orchestrator = CreateOrchestrator();
        var request = new WikiGenerationRequest { CollectionId = "col1", Name = "Test Wiki" };

        var events = await CollectEventsAsync(orchestrator, request);

        events.Should().Contain(e => e.EventType == WikiGenerationProgress.EventPageError);
        events.Should().Contain(e => e.EventType == WikiGenerationProgress.EventPageComplete);
        // Should complete with Partial status
        var completionEvent = events.Last();
        completionEvent.EventType.Should().Be(WikiGenerationProgress.EventGenerationComplete);
        completionEvent.Status.Should().Be(WikiStatus.Partial.ToString());
    }

    private static async IAsyncEnumerable<T> ThrowingStream<T>(
        Exception ex,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        throw ex;
#pragma warning disable CS0162 // unreachable code — needed to make this method an async iterator
        yield break;
#pragma warning restore CS0162
    }

    // ── Cancellation ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateAsync_CancellationRequested_EmitsCancelledEvent()
    {
        var twoPageToc = """
            {
              "sections": [
                {
                  "sectionPath": "S1",
                  "pages": [
                    { "title": "Page A", "keywords": [] },
                    { "title": "Page B", "keywords": [] }
                  ]
                }
              ]
            }
            """;

        var cts = new CancellationTokenSource();

        // First page generation cancels before finishing
        var callCount = 0;
        SetupHappyPath(tocJson: twoPageToc);
        _generationMock.Setup(g => g.GenerateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<Dictionary<string, string>?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns((string _, string _, int _, Dictionary<string, string>? _, string? _, CancellationToken ct) =>
            {
                var idx = Interlocked.Increment(ref callCount);
                if (idx == 1) return MakeTocStream(twoPageToc);
                // Cancel on first page generation
                cts.Cancel();
                return ThrowingStream<GenerationDelta>(new OperationCanceledException());
            });

        var orchestrator = CreateOrchestrator();
        var request = new WikiGenerationRequest { CollectionId = "col1", Name = "Test Wiki" };

        var events = new List<WikiGenerationProgress>();
        try
        {
            await foreach (var e in orchestrator.GenerateAsync(request, cts.Token))
                events.Add(e);
        }
        catch (OperationCanceledException) { /* Expected */ }

        events.Should().Contain(e =>
            e.EventType == WikiGenerationProgress.EventGenerationCancelled ||
            e.EventType == WikiGenerationProgress.EventGenerationComplete &&
            e.Status == WikiStatus.Partial.ToString());
    }

    // ── Concurrent generation guard ───────────────────────────────────────────

    [Fact]
    public void GenerateAsync_ConcurrentGenerationInProgress_ThrowsInvalidOperationException()
    {
        SetupHappyPath();

        // Simulate an in-progress generation for the same collection+name
        _repoMock.Setup(r => r.ExistsGeneratingAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var orchestrator = CreateOrchestrator();
        var request = new WikiGenerationRequest { CollectionId = "col1", Name = "Test Wiki" };

        // The exception should be thrown synchronously before the first yield
        var act = () => orchestrator.GenerateAsync(request, CancellationToken.None);

        act.Should().Throw<InvalidOperationException>("because a concurrent generation is in progress");
    }

    // ── Progress event content validation ────────────────────────────────────

    [Fact]
    public async Task GenerateAsync_TocComplete_TotalPagesIsCorrect()
    {
        SetupHappyPath(); // 1 page in ValidTocJson
        var orchestrator = CreateOrchestrator();
        var request = new WikiGenerationRequest { CollectionId = "col1", Name = "Test Wiki" };

        var events = await CollectEventsAsync(orchestrator, request);

        var tocEvent = events.Single(e => e.EventType == WikiGenerationProgress.EventTocComplete);
        tocEvent.TotalPages.Should().Be(1);
    }

    [Fact]
    public async Task GenerateAsync_PageStart_PageTitleIsPopulated()
    {
        SetupHappyPath();
        var orchestrator = CreateOrchestrator();
        var request = new WikiGenerationRequest { CollectionId = "col1", Name = "Test Wiki" };

        var events = await CollectEventsAsync(orchestrator, request);

        var pageStartEvent = events.First(e => e.EventType == WikiGenerationProgress.EventPageStart);
        pageStartEvent.PageTitle.Should().Be("Introduction");
    }

    [Fact]
    public async Task GenerateAsync_WallToWall_WikiIdIsConsistentAcrossAllEvents()
    {
        SetupHappyPath();
        var orchestrator = CreateOrchestrator();
        var request = new WikiGenerationRequest { CollectionId = "col1", Name = "Test Wiki" };

        var events = await CollectEventsAsync(orchestrator, request);

        var wikiIds = events.Select(e => e.WikiId).Distinct().ToList();
        wikiIds.Should().HaveCount(1, "all events should share the same WikiId");
        wikiIds[0].Should().NotBe(Guid.Empty);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<List<WikiGenerationProgress>> CollectEventsAsync(
        IWikiGenerationService orchestrator,
        WikiGenerationRequest request,
        CancellationToken ct = default)
    {
        var events = new List<WikiGenerationProgress>();
        await foreach (var e in orchestrator.GenerateAsync(request, ct))
            events.Add(e);
        return events;
    }
}
