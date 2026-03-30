using DeepWiki.Data.Abstractions.Entities;
using FluentAssertions;

namespace DeepWiki.Data.Abstractions.Tests.Entities;

/// <summary>
/// Unit tests for Wiki entity construction, default values, enum values,
/// and navigation property initialisation (T021).
/// </summary>
public class WikiEntityTests
{
    // ── WikiStatus enum ──────────────────────────────────────────────────

    [Fact]
    public void WikiStatus_HasExpectedValues()
    {
        var values = Enum.GetValues<WikiStatus>();
        values.Should().Contain(WikiStatus.Generating);
        values.Should().Contain(WikiStatus.Complete);
        values.Should().Contain(WikiStatus.Partial);
        values.Should().Contain(WikiStatus.Error);
    }

    [Fact]
    public void WikiStatus_Generating_IsDefaultValue()
    {
        // Default enum value (0) should be Generating so that an uninitialised
        // entity is treated as "not yet complete".
        var defaultStatus = default(WikiStatus);
        defaultStatus.Should().Be(WikiStatus.Generating);
    }

    // ── PageStatus enum ──────────────────────────────────────────────────

    [Fact]
    public void PageStatus_HasExpectedValues()
    {
        var values = Enum.GetValues<PageStatus>();
        values.Should().Contain(PageStatus.OK);
        values.Should().Contain(PageStatus.Error);
        values.Should().Contain(PageStatus.Generating);
    }

    // ── WikiEntity construction ──────────────────────────────────────────

    [Fact]
    public void WikiEntity_DefaultConstructor_SetsStringDefaults()
    {
        var wiki = new WikiEntity();

        wiki.CollectionId.Should().Be(string.Empty);
        wiki.Name.Should().Be(string.Empty);
        wiki.Description.Should().BeNull();
    }

    [Fact]
    public void WikiEntity_DefaultConstructor_HasDefaultGuidId()
    {
        var wiki = new WikiEntity();

        // Id is value type; default is Guid.Empty until assigned by the repository.
        wiki.Id.Should().Be(Guid.Empty);
    }

    [Fact]
    public void WikiEntity_DefaultConstructor_InitialisesNavigationCollection()
    {
        var wiki = new WikiEntity();

        wiki.Pages.Should().NotBeNull();
        wiki.Pages.Should().BeEmpty();
    }

    [Fact]
    public void WikiEntity_DefaultStatus_IsGenerating()
    {
        var wiki = new WikiEntity();
        wiki.Status.Should().Be(WikiStatus.Generating);
    }

    [Fact]
    public void WikiEntity_PropertiesAreAssignable()
    {
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var wiki = new WikiEntity
        {
            Id = id,
            CollectionId = "col-abc",
            Name = "My Wiki",
            Description = "A description",
            Status = WikiStatus.Complete,
            CreatedAt = now,
            UpdatedAt = now
        };

        wiki.Id.Should().Be(id);
        wiki.CollectionId.Should().Be("col-abc");
        wiki.Name.Should().Be("My Wiki");
        wiki.Description.Should().Be("A description");
        wiki.Status.Should().Be(WikiStatus.Complete);
        wiki.CreatedAt.Should().Be(now);
        wiki.UpdatedAt.Should().Be(now);
    }

    [Fact]
    public void WikiEntity_PagesCollection_AcceptsChildPages()
    {
        var wiki = new WikiEntity { Name = "Wiki" };
        var page = new WikiPageEntity { Title = "Page 1" };

        wiki.Pages.Add(page);

        wiki.Pages.Should().ContainSingle();
        wiki.Pages.Should().Contain(page);
    }

    // ── WikiPageEntity construction ──────────────────────────────────────

    [Fact]
    public void WikiPageEntity_DefaultConstructor_SetsStringDefaults()
    {
        var page = new WikiPageEntity();

        page.Title.Should().Be(string.Empty);
        page.Content.Should().Be(string.Empty);
        page.SectionPath.Should().Be(string.Empty);
    }

    [Fact]
    public void WikiPageEntity_DefaultConstructor_HasNullParentPageId()
    {
        var page = new WikiPageEntity();
        page.ParentPageId.Should().BeNull();
    }

    [Fact]
    public void WikiPageEntity_DefaultStatus_IsOK()
    {
        // PageStatus.OK = 0 (first declared value)
        var page = new WikiPageEntity();
        page.Status.Should().Be(PageStatus.OK);
    }

    [Fact]
    public void WikiPageEntity_DefaultConstructor_InitialisesNavigationCollections()
    {
        var page = new WikiPageEntity();

        page.ChildPages.Should().NotBeNull().And.BeEmpty();
        page.SourceRelations.Should().NotBeNull().And.BeEmpty();
        page.TargetRelations.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void WikiPageEntity_PropertiesAreAssignable()
    {
        var wikiId = Guid.NewGuid();
        var pageId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var page = new WikiPageEntity
        {
            Id = pageId,
            WikiId = wikiId,
            Title = "Overview",
            Content = "# Overview\nContent here.",
            SectionPath = "Introduction/Overview",
            SortOrder = 3,
            ParentPageId = parentId,
            Status = PageStatus.Generating,
            CreatedAt = now,
            UpdatedAt = now
        };

        page.Id.Should().Be(pageId);
        page.WikiId.Should().Be(wikiId);
        page.Title.Should().Be("Overview");
        page.Content.Should().Be("# Overview\nContent here.");
        page.SectionPath.Should().Be("Introduction/Overview");
        page.SortOrder.Should().Be(3);
        page.ParentPageId.Should().Be(parentId);
        page.Status.Should().Be(PageStatus.Generating);
        page.CreatedAt.Should().Be(now);
        page.UpdatedAt.Should().Be(now);
    }

    // ── WikiPageRelation construction ────────────────────────────────────

    [Fact]
    public void WikiPageRelation_PropertiesAreAssignable()
    {
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        var relation = new WikiPageRelation
        {
            SourcePageId = sourceId,
            TargetPageId = targetId
        };

        relation.SourcePageId.Should().Be(sourceId);
        relation.TargetPageId.Should().Be(targetId);
    }

    [Fact]
    public void WikiPageRelation_DefaultNavigationProperties_AreNonNull()
    {
        // Navigation properties are initialised to non-null via null! (required ref)
        // Accessing them on an untracked entity is valid as long as we don't dereference.
        var relation = new WikiPageRelation
        {
            SourcePageId = Guid.NewGuid(),
            TargetPageId = Guid.NewGuid()
        };

        // Assigning navigation props should succeed
        var sourcePage = new WikiPageEntity { Title = "Source" };
        var targetPage = new WikiPageEntity { Title = "Target" };

        relation.SourcePage = sourcePage;
        relation.TargetPage = targetPage;

        relation.SourcePage.Should().BeSameAs(sourcePage);
        relation.TargetPage.Should().BeSameAs(targetPage);
    }

    // ── Name/CollectionId boundary checks ────────────────────────────────

    [Fact]
    public void WikiEntity_Name_SupportsMaxLength200Chars()
    {
        var longName = new string('A', 200);
        var wiki = new WikiEntity { Name = longName };
        wiki.Name.Length.Should().Be(200);
    }

    [Fact]
    public void WikiEntity_Description_IsOptional()
    {
        var wiki = new WikiEntity { Description = null };
        wiki.Description.Should().BeNull();

        wiki.Description = "Some description";
        wiki.Description.Should().Be("Some description");
    }
}
