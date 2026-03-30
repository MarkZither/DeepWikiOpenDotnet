using DeepWiki.Data.Abstractions.Entities;
using DeepWiki.Data.Postgres.DbContexts;
using DeepWiki.Data.Postgres.Repositories;
using DeepWiki.Data.Postgres.Tests.Fixtures;
using Xunit;

namespace DeepWiki.Data.Postgres.Tests;

/// <summary>
/// Testcontainers integration tests for <see cref="PostgresWikiRepository"/>.
/// Verifies CRUD, cascades, idempotency, and concurrency guards against a real Postgres container.
/// Task T021a — must pass before Phase 3 user-story implementation begins.
/// </summary>
[Trait("Category", "Integration")]
public class PostgresWikiRepositoryTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private PostgresVectorDbContext? _context;
    private PostgresWikiRepository? _repository;

    private PostgresWikiRepository Repository
    {
        get
        {
            Assert.NotNull(_repository);
            return _repository;
        }
    }

    public PostgresWikiRepositoryTests()
    {
        _fixture = new PostgresFixture();
    }

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _context = _fixture.CreateDbContext();
        _repository = new PostgresWikiRepository(_context);
    }

    public async Task DisposeAsync()
    {
        if (_context != null)
            await _context.DisposeAsync();

        await _fixture.DisposeAsync();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static WikiEntity BuildWiki(string name = "Test Wiki", string collectionId = "col-001") => new()
    {
        Name = name,
        CollectionId = collectionId,
        Description = "A test wiki",
        Status = WikiStatus.Complete
    };

    private static WikiPageEntity BuildPage(Guid wikiId, string title = "Test Page", int sortOrder = 0) => new()
    {
        WikiId = wikiId,
        Title = title,
        Content = $"Content for {title}",
        SectionPath = "Introduction",
        SortOrder = sortOrder,
        Status = PageStatus.OK
    };

    // ── T021a-1: CreateWikiAsync ──────────────────────────────────────────

    [Fact]
    public async Task CreateWikiAsync_ShouldPersistAllEntityFields()
    {
        // Arrange
        var wiki = BuildWiki("Create Test Wiki", "col-create");

        // Act
        var created = await Repository.CreateWikiAsync(wiki);

        // Assert
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("Create Test Wiki", created.Name);
        Assert.Equal("col-create", created.CollectionId);
        Assert.Equal("A test wiki", created.Description);
        Assert.Equal(WikiStatus.Complete, created.Status);
        Assert.True(created.CreatedAt > DateTime.MinValue);
        Assert.True(created.UpdatedAt > DateTime.MinValue);
    }

    [Fact]
    public async Task CreateWikiAsync_ThrowsArgumentNull_WhenWikiIsNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Repository.CreateWikiAsync(null!));
    }

    // ── T021a-2: GetWikiByIdAsync — full graph ────────────────────────────

    [Fact]
    public async Task GetWikiByIdAsync_ShouldReturnFullGraphWithPagesAndRelations()
    {
        // Arrange — wiki with two pages and a relation between them
        var wiki = await Repository.CreateWikiAsync(BuildWiki("Graph Wiki", "col-graph"));
        var page1 = await Repository.AddPageAsync(BuildPage(wiki.Id, "Page Alpha", 0));
        var page2 = await Repository.AddPageAsync(BuildPage(wiki.Id, "Page Beta", 1));
        await Repository.SetRelatedPagesAsync(page1.Id, [page2.Id]);

        // Act
        var result = await Repository.GetWikiByIdAsync(wiki.Id);

        // Assert — top-level wiki fields
        Assert.NotNull(result);
        Assert.Equal(wiki.Id, result.Id);
        Assert.Equal("Graph Wiki", result.Name);

        // Pages loaded
        Assert.Equal(2, result.Pages.Count);

        // Relation loaded on page1 → page2
        var p1 = result.Pages.First(p => p.Id == page1.Id);
        Assert.Single(p1.SourceRelations);
        Assert.Equal(page2.Id, p1.SourceRelations.First().TargetPageId);
    }

    [Fact]
    public async Task GetWikiByIdAsync_ShouldReturnNull_ForNonExistentId()
    {
        var result = await Repository.GetWikiByIdAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    // ── T021a-3: DeleteWikiAsync — cascade ────────────────────────────────

    [Fact]
    public async Task DeleteWikiAsync_ShouldCascadeRemovePagesAndRelations()
    {
        // Arrange — wiki with pages and a cross-page relation
        var wiki = await Repository.CreateWikiAsync(BuildWiki("Delete Wiki", "col-delete"));
        var page1 = await Repository.AddPageAsync(BuildPage(wiki.Id, "Del Page 1", 0));
        var page2 = await Repository.AddPageAsync(BuildPage(wiki.Id, "Del Page 2", 1));
        await Repository.SetRelatedPagesAsync(page1.Id, [page2.Id]);

        // Pre-condition: pages exist
        Assert.Equal(2, await Repository.GetPageCountAsync(wiki.Id));

        // Act
        await Repository.DeleteWikiAsync(wiki.Id);

        // Assert — wiki and pages are gone
        var deletedWiki = await Repository.GetWikiByIdAsync(wiki.Id);
        Assert.Null(deletedWiki);

        var remainingPages = await Repository.GetPageCountAsync(wiki.Id);
        Assert.Equal(0, remainingPages);
    }

    [Fact]
    public async Task DeleteWikiAsync_ShouldBeNoOp_ForNonExistentId()
    {
        // Should not throw
        await Repository.DeleteWikiAsync(Guid.NewGuid());
    }

    // ── T021a-4: UpsertPageAsync — idempotency ────────────────────────────

    [Fact]
    public async Task UpsertPageAsync_ShouldBeIdempotent_DoubleCallProducesSingleRow()
    {
        // Arrange
        var wiki = await Repository.CreateWikiAsync(BuildWiki("Upsert Wiki", "col-upsert"));
        var pageId = Guid.NewGuid();

        var page = new WikiPageEntity
        {
            Id = pageId,
            WikiId = wiki.Id,
            Title = "Upsert Page",
            Content = "Initial content",
            SectionPath = "Section/Sub",
            SortOrder = 0,
            Status = PageStatus.Generating
        };

        // Act — first call: insert path
        var first = await Repository.UpsertPageAsync(page);

        Assert.Equal(pageId, first.Id);
        Assert.Equal("Initial content", first.Content);

        // Mutate and call again: update path
        first.Content = "Updated content";
        first.Status = PageStatus.OK;
        var second = await Repository.UpsertPageAsync(first);

        // Assert — single row, updated content
        var count = await Repository.GetPageCountAsync(wiki.Id);
        Assert.Equal(1, count);
        Assert.Equal(pageId, second.Id);
        Assert.Equal("Updated content", second.Content);
        Assert.Equal(PageStatus.OK, second.Status);
    }

    // ── T021a-5: ExistsGeneratingAsync ───────────────────────────────────

    [Fact]
    public async Task ExistsGeneratingAsync_ShouldReturnTrue_OnlyDuringGeneratingStatus()
    {
        // Arrange — start with Complete status
        var collectionId = $"col-gen-{Guid.NewGuid():N}";
        const string wikiName = "Generating Wiki";

        var wiki = await Repository.CreateWikiAsync(new WikiEntity
        {
            Name = wikiName,
            CollectionId = collectionId,
            Status = WikiStatus.Complete
        });

        // Not generating yet
        var beforeGenerate = await Repository.ExistsGeneratingAsync(collectionId, wikiName);
        Assert.False(beforeGenerate);

        // Flip to Generating
        await Repository.UpdateWikiStatusAsync(wiki.Id, WikiStatus.Generating);
        var duringGenerate = await Repository.ExistsGeneratingAsync(collectionId, wikiName);
        Assert.True(duringGenerate);

        // Flip back to Complete
        await Repository.UpdateWikiStatusAsync(wiki.Id, WikiStatus.Complete);
        var afterGenerate = await Repository.ExistsGeneratingAsync(collectionId, wikiName);
        Assert.False(afterGenerate);
    }

    [Fact]
    public async Task ExistsGeneratingAsync_ShouldReturnFalse_ForNonExistentCollection()
    {
        var result = await Repository.ExistsGeneratingAsync("nonexistent-collection", "nonexistent-wiki");
        Assert.False(result);
    }
}
