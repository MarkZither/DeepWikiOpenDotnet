using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DeepWiki.Data.Abstractions.Entities;
using DeepWiki.Data.Abstractions.Interfaces;
using DeepWiki.Rag.Core.Services;
using FluentAssertions;
using Xunit;

namespace DeepWiki.Rag.Core.Tests.Services;

public class WikiServiceTests
{
    // ── Inline test double ──────────────────────────────────────────────────

    private sealed class FakeWikiRepository : IWikiRepository
    {
        private readonly List<WikiEntity> _wikis = [];
        private readonly List<WikiPageEntity> _pages = [];
        private readonly List<WikiPageRelation> _relations = [];

        public Task<WikiEntity> CreateWikiAsync(WikiEntity wiki, CancellationToken ct = default)
        {
            wiki.Id = wiki.Id == Guid.Empty ? Guid.NewGuid() : wiki.Id;
            _wikis.Add(wiki);
            return Task.FromResult(wiki);
        }

        public Task<WikiEntity?> GetWikiByIdAsync(Guid wikiId, CancellationToken ct = default)
        {
            var wiki = _wikis.FirstOrDefault(w => w.Id == wikiId);
            if (wiki != null)
                wiki.Pages = _pages.Where(p => p.WikiId == wikiId).ToList();
            return Task.FromResult(wiki);
        }

        public Task<IReadOnlyList<WikiEntity>> GetProjectsAsync(int page, int pageSize, CancellationToken ct = default)
        {
            IReadOnlyList<WikiEntity> result = _wikis
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(w => { w.Pages = _pages.Where(p => p.WikiId == w.Id).ToList(); return w; })
                .ToList();
            return Task.FromResult(result);
        }

        public Task DeleteWikiAsync(Guid wikiId, CancellationToken ct = default)
        {
            _wikis.RemoveAll(w => w.Id == wikiId);
            _pages.RemoveAll(p => p.WikiId == wikiId);
            return Task.CompletedTask;
        }

        public Task UpdateWikiStatusAsync(Guid wikiId, WikiStatus status, CancellationToken ct = default)
        {
            var wiki = _wikis.FirstOrDefault(w => w.Id == wikiId);
            if (wiki != null) wiki.Status = status;
            return Task.CompletedTask;
        }

        public Task UpdateWikiDescriptionAsync(Guid wikiId, string? description, CancellationToken ct = default)
        {
            var wiki = _wikis.FirstOrDefault(w => w.Id == wikiId);
            if (wiki != null)
            {
                wiki.Description = description;
                wiki.UpdatedAt = DateTime.UtcNow;
            }
            return Task.CompletedTask;
        }

        public Task<WikiPageEntity?> GetPageByIdAsync(Guid pageId, CancellationToken ct = default)
        {
            return Task.FromResult(_pages.FirstOrDefault(p => p.Id == pageId));
        }

        public Task<WikiPageEntity> AddPageAsync(WikiPageEntity page, CancellationToken ct = default)
        {
            page.Id = page.Id == Guid.Empty ? Guid.NewGuid() : page.Id;
            _pages.Add(page);
            return Task.FromResult(page);
        }

        public Task<WikiPageEntity> UpdatePageAsync(WikiPageEntity page, CancellationToken ct = default)
        {
            var i = _pages.FindIndex(p => p.Id == page.Id);
            if (i >= 0) _pages[i] = page;
            return Task.FromResult(page);
        }

        public Task DeletePageAsync(Guid pageId, CancellationToken ct = default)
        {
            _pages.RemoveAll(p => p.Id == pageId);
            _relations.RemoveAll(r => r.SourcePageId == pageId || r.TargetPageId == pageId);
            return Task.CompletedTask;
        }

        public Task<int> GetPageCountAsync(Guid wikiId, CancellationToken ct = default)
        {
            return Task.FromResult(_pages.Count(p => p.WikiId == wikiId));
        }

        public Task<IReadOnlyList<WikiPageEntity>> GetRelatedPagesAsync(Guid pageId, CancellationToken ct = default)
        {
            var targetIds = _relations.Where(r => r.SourcePageId == pageId).Select(r => r.TargetPageId);
            IReadOnlyList<WikiPageEntity> result = _pages.Where(p => targetIds.Contains(p.Id)).ToList();
            return Task.FromResult(result);
        }

        public Task SetRelatedPagesAsync(Guid sourcePageId, IEnumerable<Guid> targetPageIds, CancellationToken ct = default)
        {
            _relations.RemoveAll(r => r.SourcePageId == sourcePageId);
            foreach (var tid in targetPageIds)
                _relations.Add(new WikiPageRelation { SourcePageId = sourcePageId, TargetPageId = tid });
            return Task.CompletedTask;
        }

        public Task<bool> ExistsGeneratingAsync(string collectionId, string name, CancellationToken ct = default)
        {
            return Task.FromResult(_wikis.Any(w =>
                w.CollectionId == collectionId &&
                w.Name == name &&
                w.Status == WikiStatus.Generating));
        }

        public Task<WikiPageEntity> UpsertPageAsync(WikiPageEntity page, CancellationToken ct = default)
        {
            var existing = _pages.FirstOrDefault(p => p.Id == page.Id);
            if (existing == null)
                return AddPageAsync(page, ct);
            var i = _pages.FindIndex(p => p.Id == page.Id);
            _pages[i] = page;
            return Task.FromResult(page);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static WikiService CreateService(IWikiRepository? repo = null)
        => new WikiService(repo ?? new FakeWikiRepository());

    // ── CreateWikiAsync tests ────────────────────────────────────────────────

    [Fact]
    public async Task CreateWikiAsync_ValidInput_ReturnsPersistedWiki()
    {
        var svc = CreateService();

        var result = await svc.CreateWikiAsync("My Wiki", "col-001", "A description");

        result.Should().NotBeNull();
        result.Id.Should().NotBe(Guid.Empty);
        result.Name.Should().Be("My Wiki");
        result.CollectionId.Should().Be("col-001");
        result.Description.Should().Be("A description");
        result.Status.Should().Be(WikiStatus.Complete);
        result.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        result.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CreateWikiAsync_WithPages_CreatesWikiAndPages()
    {
        var svc = CreateService();
        var pages = new List<WikiPageEntity>
        {
            new() { Title = "Page 1", Content = "Content 1", SectionPath = "Section/Overview", SortOrder = 0 }
        };

        var result = await svc.CreateWikiAsync("Wiki", "col-001", null, pages);

        result.Id.Should().NotBe(Guid.Empty);
        result.Pages.Should().HaveCount(1);
        result.Pages.First().WikiId.Should().Be(result.Id);
    }

    [Theory]
    [InlineData("", "col-001")]      // name empty
    [InlineData("   ", "col-001")]   // name whitespace
    [InlineData("Valid Name", "")]   // collectionId empty
    [InlineData("Valid Name", "  ")] // collectionId whitespace
    public async Task CreateWikiAsync_InvalidInput_ThrowsArgumentException(string name, string collectionId)
    {
        var svc = CreateService();

        var act = async () => await svc.CreateWikiAsync(name, collectionId, null);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CreateWikiAsync_NameExceeds200Chars_ThrowsArgumentException()
    {
        var svc = CreateService();
        var longName = new string('a', 201);

        var act = async () => await svc.CreateWikiAsync(longName, "col-001", null);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*200*");
    }

    // ── GetWikiByIdAsync tests ───────────────────────────────────────────────

    [Fact]
    public async Task GetWikiByIdAsync_ExistingId_ReturnsWiki()
    {
        var svc = CreateService();
        var wiki = await svc.CreateWikiAsync("Test", "col-001", null);

        var result = await svc.GetWikiByIdAsync(wiki.Id);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Test");
    }

    [Fact]
    public async Task GetWikiByIdAsync_NonExistentId_ReturnsNull()
    {
        var svc = CreateService();

        var result = await svc.GetWikiByIdAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    // ── GetProjectsAsync tests ───────────────────────────────────────────────

    [Fact]
    public async Task GetProjectsAsync_WithMultipleWikis_ReturnsPaginatedResults()
    {
        var svc = CreateService();
        await svc.CreateWikiAsync("Wiki One", "col-001", null);
        await svc.CreateWikiAsync("Wiki Two", "col-002", null);
        await svc.CreateWikiAsync("Wiki Three", "col-003", null);

        var (items, total) = await svc.GetProjectsAsync(page: 1, pageSize: 2);

        items.Should().HaveCount(2);
        total.Should().Be(3);
    }

    [Fact]
    public async Task GetProjectsAsync_EmptyRepository_ReturnsEmptyList()
    {
        var svc = CreateService();

        var (items, total) = await svc.GetProjectsAsync(1, 20);

        items.Should().BeEmpty();
        total.Should().Be(0);
    }

    // ── DeleteWikiAsync tests ────────────────────────────────────────────────

    [Fact]
    public async Task DeleteWikiAsync_ExistingId_ReturnsTrueAndRemovesWiki()
    {
        var svc = CreateService();
        var wiki = await svc.CreateWikiAsync("Delete Me", "col-001", null);

        var deleted = await svc.DeleteWikiAsync(wiki.Id);
        var fetched = await svc.GetWikiByIdAsync(wiki.Id);

        deleted.Should().BeTrue();
        fetched.Should().BeNull();
    }

    [Fact]
    public async Task DeleteWikiAsync_NonExistentId_ReturnsFalse()
    {
        var svc = CreateService();

        var deleted = await svc.DeleteWikiAsync(Guid.NewGuid());

        deleted.Should().BeFalse();
    }

    // ── UpdateWikiDescriptionAsync tests ─────────────────────────────────────

    [Fact]
    public async Task UpdateWikiDescriptionAsync_ExistingId_UpdatesDescription()
    {
        var svc = CreateService();
        var wiki = await svc.CreateWikiAsync("Test", "col-001", "Old Description");

        var result = await svc.UpdateWikiDescriptionAsync(wiki.Id, "New Description");

        result.Should().NotBeNull();
        result!.Description.Should().Be("New Description");
        result.UpdatedAt.Should().BeOnOrAfter(wiki.UpdatedAt);
    }

    [Fact]
    public async Task UpdateWikiDescriptionAsync_NonExistentId_ReturnsNull()
    {
        var svc = CreateService();

        var result = await svc.UpdateWikiDescriptionAsync(Guid.NewGuid(), "desc");

        result.Should().BeNull();
    }

    // ── AddPageAsync tests ───────────────────────────────────────────────────

    [Fact]
    public async Task AddPageAsync_ValidInput_ReturnsPersistedPage()
    {
        var svc = CreateService();
        var wiki = await svc.CreateWikiAsync("Wiki", "col-001", null);

        var page = await svc.AddPageAsync(wiki.Id, "Page Title", "Page content", "Architecture/Overview", 0, null);

        page.Should().NotBeNull();
        page.Id.Should().NotBe(Guid.Empty);
        page.WikiId.Should().Be(wiki.Id);
        page.Title.Should().Be("Page Title");
        page.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData("", "Content")]    // title empty
    [InlineData("Title", "")]      // content empty
    public async Task AddPageAsync_InvalidInput_ThrowsArgumentException(string title, string content)
    {
        var svc = CreateService();
        var wiki = await svc.CreateWikiAsync("Wiki", "col-001", null);

        var act = async () => await svc.AddPageAsync(wiki.Id, title, content, "Section", 0, null);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── UpdatePageAsync tests ────────────────────────────────────────────────

    [Fact]
    public async Task UpdatePageAsync_ExistingPage_UpdatesMutableFields()
    {
        var svc = CreateService();
        var wiki = await svc.CreateWikiAsync("Wiki", "col-001", null);
        var page = await svc.AddPageAsync(wiki.Id, "Old Title", "Old content", "Section", 0, null);

        var result = await svc.UpdatePageAsync(wiki.Id, page.Id, "New Title", "New content", null, null, null);

        result.Should().NotBeNull();
        result!.Title.Should().Be("New Title");
        result.Content.Should().Be("New content");
    }

    [Fact]
    public async Task UpdatePageAsync_NonExistentPage_ReturnsNull()
    {
        var svc = CreateService();
        var wiki = await svc.CreateWikiAsync("Wiki", "col-001", null);

        var result = await svc.UpdatePageAsync(wiki.Id, Guid.NewGuid(), "T", "C", null, null, null);

        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdatePageAsync_WithRelatedPageIds_UpdatesRelations()
    {
        var svc = CreateService();
        var wiki = await svc.CreateWikiAsync("Wiki", "col-001", null);
        var page1 = await svc.AddPageAsync(wiki.Id, "Page 1", "Content", "Section", 0, null);
        var page2 = await svc.AddPageAsync(wiki.Id, "Page 2", "Content", "Section", 1, null);

        var result = await svc.UpdatePageAsync(wiki.Id, page1.Id, null, null, null, null, new[] { page2.Id });

        result.Should().NotBeNull();
    }

    // ── DeletePageAsync tests ─────────────────────────────────────────────────

    [Fact]
    public async Task DeletePageAsync_ExistingPage_ReturnsTrueAndRemovesPage()
    {
        var svc = CreateService();
        var wiki = await svc.CreateWikiAsync("Wiki", "col-001", null);
        var page = await svc.AddPageAsync(wiki.Id, "Page", "Content", "Section", 0, null);

        var deleted = await svc.DeletePageAsync(wiki.Id, page.Id);

        deleted.Should().BeTrue();
    }

    [Fact]
    public async Task DeletePageAsync_NonExistentPage_ReturnsFalse()
    {
        var svc = CreateService();
        var wiki = await svc.CreateWikiAsync("Wiki", "col-001", null);

        var deleted = await svc.DeletePageAsync(wiki.Id, Guid.NewGuid());

        deleted.Should().BeFalse();
    }

    // ── UpdateRelatedPagesAsync tests ─────────────────────────────────────────

    [Fact]
    public async Task UpdateRelatedPagesAsync_ValidIds_SetsRelations()
    {
        var svc = CreateService();
        var wiki = await svc.CreateWikiAsync("Wiki", "col-001", null);
        var page1 = await svc.AddPageAsync(wiki.Id, "Page 1", "Content", "Section", 0, null);
        var page2 = await svc.AddPageAsync(wiki.Id, "Page 2", "Content", "Section", 1, null);

        // Should not throw
        await svc.UpdateRelatedPagesAsync(wiki.Id, page1.Id, new[] { page2.Id });
    }
}
