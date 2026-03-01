using DeepWiki.Data.Abstractions.Entities;
using DeepWiki.Data.SqlServer.DbContexts;
using DeepWiki.Data.SqlServer.Repositories;
using DeepWiki.Data.SqlServer.Tests.Fixtures;
using Xunit;

namespace DeepWiki.Data.SqlServer.Tests;

/// <summary>
/// Testcontainers integration tests for <see cref="SqlServerWikiRepository"/>.
/// Verifies 100% behaviour parity with <c>PostgresWikiRepositoryTests</c> (T021a).
/// Uses the check-then-insert-or-update UpsertPageAsync strategy specific to SQL Server.
/// Task T021b — must pass before Phase 3 user-story implementation begins.
/// </summary>
[Trait("Category", "Integration")]
public class SqlServerWikiRepositoryTests : IAsyncLifetime
{
    private readonly SqlServerFixture _fixture;
    private SqlServerVectorDbContext? _context;
    private SqlServerWikiRepository? _repository;

    private SqlServerWikiRepository Repository
    {
        get
        {
            Assert.NotNull(_repository);
            return _repository;
        }
    }

    public SqlServerWikiRepositoryTests()
    {
        _fixture = new SqlServerFixture();
    }

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _context = _fixture.CreateDbContext();
        _repository = new SqlServerWikiRepository(_context);
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

    // ── T021b-1: CreateWikiAsync ──────────────────────────────────────────

    [Fact]
    public async Task CreateWikiAsync_ShouldPersistAllEntityFields()
    {
        // Arrange
        var wiki = BuildWiki("Create Test Wiki SS", "col-ss-create");

        // Act
        var created = await Repository.CreateWikiAsync(wiki);

        // Assert
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("Create Test Wiki SS", created.Name);
        Assert.Equal("col-ss-create", created.CollectionId);
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

    // ── T021b-2: GetWikiByIdAsync — full graph ────────────────────────────

    [Fact]
    public async Task GetWikiByIdAsync_ShouldReturnFullGraphWithPagesAndRelations()
    {
        // Arrange
        var wiki = await Repository.CreateWikiAsync(BuildWiki("Graph Wiki SS", "col-ss-graph"));
        var page1 = await Repository.AddPageAsync(BuildPage(wiki.Id, "Page Alpha SS", 0));
        var page2 = await Repository.AddPageAsync(BuildPage(wiki.Id, "Page Beta SS", 1));
        await Repository.SetRelatedPagesAsync(page1.Id, [page2.Id]);

        // Act
        var result = await Repository.GetWikiByIdAsync(wiki.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(wiki.Id, result.Id);
        Assert.Equal("Graph Wiki SS", result.Name);
        Assert.Equal(2, result.Pages.Count);

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

    // ── T021b-3: DeleteWikiAsync — cascade ────────────────────────────────

    [Fact]
    public async Task DeleteWikiAsync_ShouldCascadeRemovePagesAndRelations()
    {
        // Arrange
        var wiki = await Repository.CreateWikiAsync(BuildWiki("Delete Wiki SS", "col-ss-delete"));
        var page1 = await Repository.AddPageAsync(BuildPage(wiki.Id, "Del Page 1 SS", 0));
        var page2 = await Repository.AddPageAsync(BuildPage(wiki.Id, "Del Page 2 SS", 1));
        await Repository.SetRelatedPagesAsync(page1.Id, [page2.Id]);

        Assert.Equal(2, await Repository.GetPageCountAsync(wiki.Id));

        // Act
        await Repository.DeleteWikiAsync(wiki.Id);

        // Assert
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

    // ── T021b-4: UpsertPageAsync — check-then-insert-or-update idempotency ─

    [Fact]
    public async Task UpsertPageAsync_ShouldBeIdempotent_DoubleCallProducesSingleRow()
    {
        // Arrange — pre-assigned ID (simulates orchestrator calling upsert twice)
        var wiki = await Repository.CreateWikiAsync(BuildWiki("Upsert Wiki SS", "col-ss-upsert"));
        var pageId = Guid.NewGuid();

        var page = new WikiPageEntity
        {
            Id = pageId,
            WikiId = wiki.Id,
            Title = "Upsert Page SS",
            Content = "Initial content",
            SectionPath = "Section/Sub",
            SortOrder = 0,
            Status = PageStatus.Generating
        };

        // First call — insert path
        var first = await Repository.UpsertPageAsync(page);

        Assert.Equal(pageId, first.Id);
        Assert.Equal("Initial content", first.Content);

        // Second call — update path (same ID, different content)
        first.Content = "Updated content";
        first.Status = PageStatus.OK;
        var second = await Repository.UpsertPageAsync(first);

        // Single row asserted via GetPageCountAsync
        var count = await Repository.GetPageCountAsync(wiki.Id);
        Assert.Equal(1, count);
        Assert.Equal(pageId, second.Id);
        Assert.Equal("Updated content", second.Content);
        Assert.Equal(PageStatus.OK, second.Status);
    }

    // ── T021b-5: ExistsGeneratingAsync ───────────────────────────────────

    [Fact]
    public async Task ExistsGeneratingAsync_ShouldReturnTrue_OnlyDuringGeneratingStatus()
    {
        // Unique names to avoid cross-test contamination if fixture is ever shared
        var collectionId = $"col-ss-gen-{Guid.NewGuid():N}";
        const string wikiName = "SS Generating Wiki";

        var wiki = await Repository.CreateWikiAsync(new WikiEntity
        {
            Name = wikiName,
            CollectionId = collectionId,
            Status = WikiStatus.Complete
        });

        // Not generating
        var before = await Repository.ExistsGeneratingAsync(collectionId, wikiName);
        Assert.False(before);

        // Set to Generating
        await Repository.UpdateWikiStatusAsync(wiki.Id, WikiStatus.Generating);
        var during = await Repository.ExistsGeneratingAsync(collectionId, wikiName);
        Assert.True(during);

        // Set back to Complete
        await Repository.UpdateWikiStatusAsync(wiki.Id, WikiStatus.Complete);
        var after = await Repository.ExistsGeneratingAsync(collectionId, wikiName);
        Assert.False(after);
    }

    [Fact]
    public async Task ExistsGeneratingAsync_ShouldReturnFalse_ForNonExistentCollection()
    {
        var result = await Repository.ExistsGeneratingAsync("nonexistent-ss", "nonexistent-wiki-ss");
        Assert.False(result);
    }
}
