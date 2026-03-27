using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using DeepWiki.ApiService.Models;
using DeepWiki.ApiService.Tests.TestUtilities;
using DeepWiki.Data.Abstractions.Entities;
using DeepWiki.Rag.Core.Models;
using DeepWiki.Rag.Core.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DeepWiki.ApiService.Tests.Performance;

// ── In-memory wiki service with pre-seeded data ──────────────────────────────

file sealed class PerfWikiService : IWikiService
{
    public static readonly Guid WikiId100 = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    public static readonly Guid WikiId50  = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    private static WikiEntity BuildSeededWiki(Guid id, int pageCount) => new()
    {
        Id             = id,
        Name           = $"Perf Wiki {id}",
        CollectionId   = "perf-collection",
        Status         = WikiStatus.Complete,
        CreatedAt      = DateTime.UtcNow,
        UpdatedAt      = DateTime.UtcNow,
        Pages          = Enumerable.Range(1, pageCount).Select(i => new WikiPageEntity
        {
            Id          = Guid.NewGuid(),
            WikiId      = id,
            Title       = $"Page {i}",
            Content     = new string('x', 500),
            SectionPath = $"Section/{(i - 1) / 10 + 1}",
            SortOrder   = i,
            Status      = PageStatus.OK,
            CreatedAt   = DateTime.UtcNow,
            UpdatedAt   = DateTime.UtcNow
        }).ToList()
    };

    private readonly WikiEntity _wiki100 = BuildSeededWiki(WikiId100, 100);
    private readonly WikiEntity _wiki50  = BuildSeededWiki(WikiId50,  50);

    private readonly List<WikiEntity> _projects = Enumerable.Range(1, 100).Select(i => new WikiEntity
    {
        Id           = Guid.NewGuid(),
        Name         = $"Project {i}",
        CollectionId = $"col-{i}",
        Status       = WikiStatus.Complete,
        CreatedAt    = DateTime.UtcNow,
        UpdatedAt    = DateTime.UtcNow,
        Pages        = []
    }).ToList();

    public Task<WikiEntity> CreateWikiAsync(string name, string collectionId, string? description,
        IEnumerable<WikiPageEntity>? pages = null, CancellationToken cancellationToken = default)
    {
        var wiki = new WikiEntity
        {
            Id           = Guid.NewGuid(),
            Name         = name,
            CollectionId = collectionId,
            Description  = description,
            Status       = WikiStatus.Complete,
            CreatedAt    = DateTime.UtcNow,
            UpdatedAt    = DateTime.UtcNow,
            Pages        = pages?.ToList() ?? []
        };
        return Task.FromResult(wiki);
    }

    public Task<WikiEntity?> GetWikiByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == WikiId100) return Task.FromResult<WikiEntity?>(_wiki100);
        if (id == WikiId50)  return Task.FromResult<WikiEntity?>(_wiki50);
        return Task.FromResult<WikiEntity?>(null);
    }

    public Task<(IReadOnlyList<WikiEntity> Items, int TotalCount)> GetProjectsAsync(int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<WikiEntity> items = _projects
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();
        return Task.FromResult((items, _projects.Count));
    }

    public Task<bool> DeleteWikiAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<WikiPageEntity?> GetPageByIdAsync(Guid wikiId, Guid pageId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<WikiPageEntity?>(null);

    public Task<IReadOnlyList<WikiPageEntity>> GetRelatedPagesAsync(Guid pageId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<WikiPageEntity> empty = [];
        return Task.FromResult(empty);
    }

    public Task<WikiEntity?> UpdateWikiDescriptionAsync(Guid id, string? description,
        CancellationToken cancellationToken = default)
        => Task.FromResult<WikiEntity?>(null);

    public Task<WikiPageEntity> AddPageAsync(Guid wikiId, string title, string content, string sectionPath,
        int sortOrder, Guid? parentPageId, IEnumerable<Guid>? relatedPageIds = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new WikiPageEntity
        {
            Id          = Guid.NewGuid(),
            WikiId      = wikiId,
            Title       = title,
            Content     = content,
            SectionPath = sectionPath,
            SortOrder   = sortOrder,
            Status      = PageStatus.OK,
            CreatedAt   = DateTime.UtcNow,
            UpdatedAt   = DateTime.UtcNow
        });

    public Task<WikiPageEntity?> UpdatePageAsync(Guid wikiId, Guid pageId, string? title, string? content,
        string? sectionPath, int? sortOrder, IEnumerable<Guid>? relatedPageIds,
        CancellationToken cancellationToken = default)
        => Task.FromResult<WikiPageEntity?>(null);

    public Task<bool> DeletePageAsync(Guid wikiId, Guid pageId, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task UpdateRelatedPagesAsync(Guid wikiId, Guid pageId, IEnumerable<Guid> relatedPageIds,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

// ── No-op wiki generation service stub ───────────────────────────────────────

file sealed class NullWikiGenerationService : IWikiGenerationService
{
    public async IAsyncEnumerable<WikiGenerationProgress> GenerateAsync(
        WikiGenerationRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }
}

// ── WebApplicationFactory fixture ─────────────────────────────────────────────

public sealed class WikiPerfFixture : IntegrationTestFixture
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Apply base setup: Testing environment, mocked vector/embedding/doc-repo
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            Replace<IWikiService>(services, ServiceLifetime.Singleton, new PerfWikiService());
            Replace<IWikiGenerationService>(services, ServiceLifetime.Scoped, new NullWikiGenerationService());
        });
    }

    private static void Replace<T>(IServiceCollection services, ServiceLifetime lifetime, T instance)
        where T : class
    {
        var toRemove = services.Where(d => d.ServiceType == typeof(T)).ToList();
        foreach (var d in toRemove) services.Remove(d);
        services.Add(new ServiceDescriptor(typeof(T), _ => instance, lifetime));
    }
}

// ── Performance smoke tests ───────────────────────────────────────────────────

/// <summary>
/// Smoke tests validating that wiki API endpoints meet the latency requirements
/// defined in spec SC-001 through SC-004. These tests use an in-memory IWikiService
/// to isolate and measure the application processing layer (routing, serialization,
/// middleware) without database I/O.
/// </summary>
[Trait("Category", "Performance")]
public sealed class WikiPerformanceSmokeTests : IClassFixture<WikiPerfFixture>
{
    private readonly HttpClient _client;

    public WikiPerformanceSmokeTests(WikiPerfFixture fixture)
    {
        _client = fixture.CreateClient();
    }

    /// <summary>
    /// SC-002: GET /api/wiki/{id} for a 100-page wiki must complete in under 200 ms (median).
    /// </summary>
    [Fact]
    public async Task GetWikiById_100Pages_MedianLatency_Under200ms()
    {
        var ct  = TestContext.Current.CancellationToken;
        var url = $"/api/wiki/{PerfWikiService.WikiId100}";

        // warm-up
        for (var i = 0; i < 3; i++)
            await _client.GetAsync(url, ct);

        var timings = await MeasureAsync(() => _client.GetAsync(url, ct), iterations: 20);
        var median  = Median(timings);

        median.Should().BeLessThan(TimeSpan.FromMilliseconds(200),
            because: "SC-002 requires GET /api/wiki/{id} median latency < 200 ms");
    }

    /// <summary>
    /// SC-004: GET /api/wiki/projects (100 projects, page 1) must complete in under 500 ms (median).
    /// </summary>
    [Fact]
    public async Task GetProjects_100Items_MedianLatency_Under500ms()
    {
        var ct  = TestContext.Current.CancellationToken;
        const string url = "/api/wiki/projects?page=1&pageSize=20";

        // warm-up
        for (var i = 0; i < 3; i++)
            await _client.GetAsync(url, ct);

        var timings = await MeasureAsync(() => _client.GetAsync(url, ct), iterations: 20);
        var median  = Median(timings);

        median.Should().BeLessThan(TimeSpan.FromMilliseconds(500),
            because: "SC-004 requires GET /api/wiki/projects median latency < 500 ms");
    }

    /// <summary>
    /// SC-001: POST /api/wiki with 50 pages must complete in under 2 s.
    /// </summary>
    [Fact]
    public async Task CreateWiki_50Pages_CompletesUnder2s()
    {
        var ct    = TestContext.Current.CancellationToken;
        var pages = Enumerable.Range(1, 50).Select(i => new CreateWikiPageRequest
        {
            Title       = $"Page {i}",
            Content     = new string('x', 500),
            SectionPath = $"Section/{(i - 1) / 10 + 1}",
            SortOrder   = i
        }).ToList();

        var request = new CreateWikiRequest
        {
            Name         = "SC-001 Perf Wiki",
            CollectionId = "perf-col",
            Pages        = pages
        };

        // warm-up
        for (var i = 0; i < 2; i++)
            await _client.PostAsJsonAsync("/api/wiki", request, ct);

        var timings = await MeasureAsync(() => _client.PostAsJsonAsync("/api/wiki", request, ct), iterations: 10);
        var max = timings.Max();

        max.Should().BeLessThan(TimeSpan.FromSeconds(2),
            because: "SC-001 requires POST /api/wiki with 50 pages to complete < 2 s");
    }

    /// <summary>
    /// SC-003: POST /api/wiki/export for a 50-page wiki must complete in under 3 s.
    /// </summary>
    [Fact]
    public async Task ExportWiki_50Pages_CompletesUnder3s()
    {
        var ct      = TestContext.Current.CancellationToken;
        var request = new WikiExportRequest
        {
            WikiId = PerfWikiService.WikiId50,
            Format = "markdown"
        };

        // warm-up
        for (var i = 0; i < 2; i++)
            await _client.PostAsJsonAsync("/api/wiki/export", request, ct);

        var timings = await MeasureAsync(() => _client.PostAsJsonAsync("/api/wiki/export", request, ct), iterations: 10);
        var max = timings.Max();

        max.Should().BeLessThan(TimeSpan.FromSeconds(3),
            because: "SC-003 requires POST /api/wiki/export for a 50-page wiki to complete < 3 s");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static async Task<List<TimeSpan>> MeasureAsync(Func<Task<HttpResponseMessage>> action, int iterations)
    {
        var timings = new List<TimeSpan>(iterations);
        for (var i = 0; i < iterations; i++)
        {
            var sw       = Stopwatch.StartNew();
            var response = await action();
            sw.Stop();
            response.EnsureSuccessStatusCode();
            timings.Add(sw.Elapsed);
        }
        return timings;
    }

    private static TimeSpan Median(List<TimeSpan> timings)
    {
        var sorted = timings.OrderBy(t => t).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1
            ? sorted[mid]
            : TimeSpan.FromMilliseconds((sorted[mid - 1].TotalMilliseconds + sorted[mid].TotalMilliseconds) / 2.0);
    }
}
