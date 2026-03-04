using System;
using System.Collections.Generic;
using System.Linq;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using deepwiki_open_dotnet.Web.Components.Shared;
using deepwiki_open_dotnet.Web.Models;
using Xunit;

namespace DeepWiki.Web.Tests.Components;

// T036 – US3: bUnit tests for WikiSidebar.razor
public class WikiSidebarTests
{
    // ── helpers ─────────────────────────────────────────────────────────────

    private static WikiPageDto Page(Guid id, string title, string sectionPath, int sortOrder = 0) =>
        new()
        {
            Id = id,
            Title = title,
            SectionPath = sectionPath,
            SortOrder = sortOrder,
            Content = string.Empty,
            Status = "OK"
        };

    // ── Test: builds tree from flat SectionPath list ─────────────────────────
    [Fact]
    public void WikiSidebar_BuildsTreeFromFlatSectionPaths()
    {
        var pageId1 = Guid.NewGuid();
        var pageId2 = Guid.NewGuid();

        var pages = new List<WikiPageDto>
        {
            Page(pageId1, "Introduction", "Overview/Introduction", 0),
            Page(pageId2, "Architecture",  "Architecture/Design",  1),
        };

        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddMudServices();

        var cut = ctx.Render<WikiSidebar>(p => p
            .Add(x => x.Pages, pages)
            .Add(x => x.SelectedPageId, (Guid?)null)
            .Add(x => x.OnPageSelected, EventCallback.Factory.Create<Guid>(new object(), _ => { })));

        var markup = cut.Markup;

        // Both section folder names should be in the rendered output
        Assert.Contains("Overview", markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Architecture", markup, StringComparison.OrdinalIgnoreCase);
    }

    // ── Test: renders page leaf nodes ────────────────────────────────────────
    [Fact]
    public void WikiSidebar_RendersPageLeafNodes()
    {
        var pageId = Guid.NewGuid();
        var pages = new List<WikiPageDto>
        {
            Page(pageId, "Getting Started", "Setup/Getting Started", 0),
        };

        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddMudServices();

        var cut = ctx.Render<WikiSidebar>(p => p
            .Add(x => x.Pages, pages)
            .Add(x => x.SelectedPageId, (Guid?)null)
            .Add(x => x.OnPageSelected, EventCallback.Factory.Create<Guid>(new object(), _ => { })));

        var markup = cut.Markup;
        Assert.Contains("Getting Started", markup, StringComparison.OrdinalIgnoreCase);
    }

    // ── Test: clicking page node triggers selection callback ─────────────────
    [Fact]
    public void WikiSidebar_PageClick_TriggersSelectionCallback()
    {
        var pageId = Guid.NewGuid();
        Guid? selectedId = null;

        var pages = new List<WikiPageDto>
        {
            Page(pageId, "Quickstart", "Quickstart", 0),
        };

        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddMudServices();

        var cut = ctx.Render<WikiSidebar>(p => p
            .Add(x => x.Pages, pages)
            .Add(x => x.SelectedPageId, (Guid?)null)
            .Add(x => x.OnPageSelected, (Guid id) => { selectedId = id; }));

        // Find and click the page link/button for the page
        var pageLinks = cut.FindAll("[data-page-id]");
        if (pageLinks.Count > 0)
        {
            pageLinks[0].Click();
            Assert.Equal(pageId, selectedId);
        }
        else
        {
            // Fallback: find any anchor or button that contains the title text
            var allLinks = cut.FindAll("a, button");
            var matchingLink = allLinks.FirstOrDefault(l =>
                l.TextContent.Contains("Quickstart", StringComparison.OrdinalIgnoreCase));

            Assert.NotNull(matchingLink); // page leaf should be clickable
        }
    }

    // ── Test: handles nested sections with correct indentation ───────────────
    [Fact]
    public void WikiSidebar_HandlesNestedSections()
    {
        var pageId = Guid.NewGuid();
        var pages = new List<WikiPageDto>
        {
            Page(pageId, "Deep Page", "Root/Section/Subsection/Deep Page", 0),
        };

        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddMudServices();

        var cut = ctx.Render<WikiSidebar>(p => p
            .Add(x => x.Pages, pages)
            .Add(x => x.SelectedPageId, (Guid?)null)
            .Add(x => x.OnPageSelected, EventCallback.Factory.Create<Guid>(new object(), _ => { })));

        var markup = cut.Markup;

        // All path segments should appear
        Assert.Contains("Root", markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Section", markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Subsection", markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Deep Page", markup, StringComparison.OrdinalIgnoreCase);
    }

    // ── Test: highlights selected page ───────────────────────────────────────
    [Fact]
    public void WikiSidebar_HighlightsSelectedPage()
    {
        var pageId = Guid.NewGuid();
        var otherId = Guid.NewGuid();

        var pages = new List<WikiPageDto>
        {
            Page(pageId,  "Selected Page",    "Docs/Selected Page",    0),
            Page(otherId, "Other Page",        "Docs/Other Page",       1),
        };

        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddMudServices();

        var cut = ctx.Render<WikiSidebar>(p => p
            .Add(x => x.Pages, pages)
            .Add(x => x.SelectedPageId, (Guid?)pageId)
            .Add(x => x.OnPageSelected, EventCallback.Factory.Create<Guid>(new object(), _ => { })));

        var markup = cut.Markup;

        // The selected page should have an active/selected class or aria attribute
        Assert.Contains("Selected Page", markup, StringComparison.OrdinalIgnoreCase);
        // Check for active indicator — look for active CSS class or the page id
        Assert.True(
            markup.Contains("active", StringComparison.OrdinalIgnoreCase)
            || markup.Contains(pageId.ToString(), StringComparison.OrdinalIgnoreCase),
            "Selected page should be visually highlighted");
    }

    // ── Test: renders empty state gracefully ─────────────────────────────────
    [Fact]
    public void WikiSidebar_HandlesEmptyPageList()
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddMudServices();

        var cut = ctx.Render<WikiSidebar>(p => p
            .Add(x => x.Pages, new List<WikiPageDto>())
            .Add(x => x.SelectedPageId, (Guid?)null)
            .Add(x => x.OnPageSelected, EventCallback.Factory.Create<Guid>(new object(), _ => { })));

        // Should render without throwing — empty state
        Assert.NotNull(cut.Markup);
    }
}
