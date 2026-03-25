using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DeepWiki.Data.Abstractions.Entities;
using DeepWiki.Rag.Core.Services;
using FluentAssertions;
using Xunit;

namespace DeepWiki.Rag.Core.Tests.Services;

public class WikiExportServiceTests
{
    private readonly WikiExportService _sut = new();

    // ── Helpers ────────────────────────────────────────────────────────────

    private static WikiEntity MakeWiki(string name = "Test Wiki", string? description = null)
        => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            CollectionId = "col-001",
            Status = WikiStatus.Complete,
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            UpdatedAt = DateTime.UtcNow
        };

    private static WikiPageEntity MakePage(Guid wikiId, string title, string content,
        string sectionPath = "Introduction", int sortOrder = 0)
        => new()
        {
            Id = Guid.NewGuid(),
            WikiId = wikiId,
            Title = title,
            Content = content,
            SectionPath = sectionPath,
            SortOrder = sortOrder,
            Status = PageStatus.OK,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

    private static async Task<string> ExportMarkdownAsync(
        WikiExportService sut,
        WikiEntity wiki,
        IReadOnlyList<WikiPageEntity> pages,
        IDictionary<Guid, IReadOnlyList<WikiPageEntity>>? relatedPages = null)
    {
        using var ms = new MemoryStream();
        await sut.ExportAsMarkdownAsync(wiki, pages, relatedPages ?? new Dictionary<Guid, IReadOnlyList<WikiPageEntity>>(), ms);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static async Task<string> ExportJsonAsync(
        WikiExportService sut,
        WikiEntity wiki,
        IReadOnlyList<WikiPageEntity> pages,
        IDictionary<Guid, IReadOnlyList<WikiPageEntity>>? relatedPages = null)
    {
        using var ms = new MemoryStream();
        await sut.ExportAsJsonAsync(wiki, pages, relatedPages ?? new Dictionary<Guid, IReadOnlyList<WikiPageEntity>>(), ms);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    // ── ExportAsMarkdownAsync ──────────────────────────────────────────────

    [Fact]
    public async Task ExportAsMarkdownAsync_ProducesWikiHeading()
    {
        var wiki = MakeWiki("My Great Wiki", "A fine description");
        var output = await ExportMarkdownAsync(_sut, wiki, []);

        output.Should().StartWith("# My Great Wiki");
        output.Should().Contain("A fine description");
    }

    [Fact]
    public async Task ExportAsMarkdownAsync_ZeroPages_EmitsNoPagesBanner()
    {
        var wiki = MakeWiki();
        var output = await ExportMarkdownAsync(_sut, wiki, []);

        output.Should().Contain("> No pages have been generated for this wiki.");
        output.Should().NotContain("## Table of Contents");
    }

    [Fact]
    public async Task ExportAsMarkdownAsync_ProducesToCWithAnchorLinks()
    {
        var wiki = MakeWiki();
        var page1 = MakePage(wiki.Id, "Getting Started", "Intro content", sortOrder: 0);
        var page2 = MakePage(wiki.Id, "Advanced Usage", "Advanced content", sortOrder: 1);

        var output = await ExportMarkdownAsync(_sut, wiki, [page1, page2]);

        output.Should().Contain("## Table of Contents");
        // Anchor links use lowercase-hyphenated form
        output.Should().Contain("[Getting Started](#getting-started)");
        output.Should().Contain("[Advanced Usage](#advanced-usage)");
    }

    [Fact]
    public async Task ExportAsMarkdownAsync_ProducesH2HeadingsPerPage()
    {
        var wiki = MakeWiki();
        var page = MakePage(wiki.Id, "Overview", "Some overview", sortOrder: 0);

        var output = await ExportMarkdownAsync(_sut, wiki, [page]);

        output.Should().Contain("## Overview");
        output.Should().Contain("Some overview");
    }

    [Fact]
    public async Task ExportAsMarkdownAsync_IncludesRelatedPagesSection()
    {
        var wiki = MakeWiki();
        var page1 = MakePage(wiki.Id, "Page One", "Content one", sortOrder: 0);
        var page2 = MakePage(wiki.Id, "Page Two", "Content two", sortOrder: 1);

        var relatedPages = new Dictionary<Guid, IReadOnlyList<WikiPageEntity>>
        {
            [page1.Id] = [page2]
        };

        var output = await ExportMarkdownAsync(_sut, wiki, [page1, page2], relatedPages);

        output.Should().Contain("### Related Pages");
        output.Should().Contain("[Page Two](#page-two)");
    }

    [Fact]
    public async Task ExportAsMarkdownAsync_DeletedRelatedPage_ShowsPageRemovedLabel()
    {
        var wiki = MakeWiki();
        var page = MakePage(wiki.Id, "Main Page", "Content", sortOrder: 0);

        // Represent a deleted page as an entity with empty Guid
        var deletedPage = new WikiPageEntity { Id = Guid.Empty, Title = string.Empty };
        var relatedPages = new Dictionary<Guid, IReadOnlyList<WikiPageEntity>>
        {
            [page.Id] = [deletedPage]
        };

        var output = await ExportMarkdownAsync(_sut, wiki, [page], relatedPages);

        output.Should().Contain("*(page removed)*");
    }

    [Fact]
    public async Task ExportAsMarkdownAsync_PagesOrderedBySortOrder()
    {
        var wiki = MakeWiki();
        var page1 = MakePage(wiki.Id, "Second", "B", sortOrder: 1);
        var page2 = MakePage(wiki.Id, "First", "A", sortOrder: 0);

        // Pass in reverse order — should still render sorted
        var output = await ExportMarkdownAsync(_sut, wiki, [page1, page2]);

        var firstIdx = output.IndexOf("## First", StringComparison.Ordinal);
        var secondIdx = output.IndexOf("## Second", StringComparison.Ordinal);
        firstIdx.Should().BeLessThan(secondIdx);
    }

    // ── ExportAsJsonAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task ExportAsJsonAsync_ProducesValidJson()
    {
        var wiki = MakeWiki("JSON Wiki", "desc");
        var page = MakePage(wiki.Id, "Intro", "Hello world", "Intro", 0);
        var json = await ExportJsonAsync(_sut, wiki, [page]);

        var doc = JsonDocument.Parse(json);
        doc.Should().NotBeNull();
    }

    [Fact]
    public async Task ExportAsJsonAsync_MetadataObjectContainsRequiredFields()
    {
        var wiki = MakeWiki("JSON Wiki", "Some description");
        var json = await ExportJsonAsync(_sut, wiki, []);

        var doc = JsonDocument.Parse(json);
        var meta = doc.RootElement.GetProperty("metadata");

        meta.GetProperty("name").GetString().Should().Be("JSON Wiki");
        meta.GetProperty("description").GetString().Should().Be("Some description");
        meta.GetProperty("pageCount").GetInt32().Should().Be(0);
        meta.TryGetProperty("exportDate", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ExportAsJsonAsync_ZeroPages_ProducesEmptyPagesArray()
    {
        var wiki = MakeWiki();
        var json = await ExportJsonAsync(_sut, wiki, []);

        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("pages").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ExportAsJsonAsync_PagesArrayContainsTitleContentSectionSortOrder()
    {
        var wiki = MakeWiki();
        var page = MakePage(wiki.Id, "Intro", "Hello world", "Overview", 5);

        var json = await ExportJsonAsync(_sut, wiki, [page]);

        var doc = JsonDocument.Parse(json);
        var pages = doc.RootElement.GetProperty("pages");
        pages.GetArrayLength().Should().Be(1);

        var p = pages[0];
        p.GetProperty("title").GetString().Should().Be("Intro");
        p.GetProperty("content").GetString().Should().Be("Hello world");
        p.GetProperty("sectionPath").GetString().Should().Be("Overview");
        p.GetProperty("sortOrder").GetInt32().Should().Be(5);
    }

    [Fact]
    public async Task ExportAsJsonAsync_RelatedPagesIncludedInPageObject()
    {
        var wiki = MakeWiki();
        var page1 = MakePage(wiki.Id, "Page One", "C1", sortOrder: 0);
        var page2 = MakePage(wiki.Id, "Page Two", "C2", sortOrder: 1);

        var relatedPages = new Dictionary<Guid, IReadOnlyList<WikiPageEntity>>
        {
            [page1.Id] = [page2]
        };

        var json = await ExportJsonAsync(_sut, wiki, [page1, page2], relatedPages);
        var doc = JsonDocument.Parse(json);
        var pages = doc.RootElement.GetProperty("pages");

        // Find page1
        var p1 = pages.EnumerateArray().First(p => p.GetProperty("title").GetString() == "Page One");
        var related = p1.GetProperty("relatedPages");
        related.GetArrayLength().Should().Be(1);
        related[0].GetProperty("title").GetString().Should().Be("Page Two");
    }

    [Fact]
    public async Task ExportAsJsonAsync_DeletedRelatedPage_ShowsPageRemovedTitle()
    {
        var wiki = MakeWiki();
        var page = MakePage(wiki.Id, "Main", "C", sortOrder: 0);
        var deletedPage = new WikiPageEntity { Id = Guid.Empty, Title = string.Empty };

        var relatedPages = new Dictionary<Guid, IReadOnlyList<WikiPageEntity>>
        {
            [page.Id] = [deletedPage]
        };

        var json = await ExportJsonAsync(_sut, wiki, [page], relatedPages);
        var doc = JsonDocument.Parse(json);
        var pages = doc.RootElement.GetProperty("pages");
        var related = pages[0].GetProperty("relatedPages");

        related.GetArrayLength().Should().Be(1);
        related[0].GetProperty("title").GetString().Should().Be("(page removed)");
        related[0].GetProperty("id").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
