using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using deepwiki_open_dotnet.Web.Components.Shared;
using deepwiki_open_dotnet.Web.Models;
using deepwiki_open_dotnet.Web.Services;
using DeepWiki.Web.Tests.Fixtures;
using Xunit;

namespace DeepWiki.Web.Tests.Components;

// T041 – US3: bUnit tests for DocumentScopeSelector
public class DocumentScopeSelectorTests
{
    private static ChatApiClient BuildApiClient(string collectionsJson)
    {
        var handler = new FakeHttpHandler((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(collectionsJson, Encoding.UTF8, "application/json")
        }));
        return new ChatApiClient(new HttpClient(handler) { BaseAddress = new Uri("https+http://apiservice") }, Microsoft.Extensions.Logging.Abstractions.NullLogger<deepwiki_open_dotnet.Web.Services.ChatApiClient>.Instance);
    }

    /// <summary>
    /// Creates a context and renders MudPopoverProvider first (required for MudSelect),
    /// then renders the DocumentScopeSelector component.
    /// </summary>
    private static IRenderedComponent<DocumentScopeSelector> RenderWithPopover(BunitContext ctx)
    {
        // Render MudPopoverProvider as a standalone component first (no ChildContent wrapping needed)
        ctx.Render(b =>
        {
            b.OpenComponent<MudPopoverProvider>(0);
            b.CloseComponent();
        });
        return ctx.Render<DocumentScopeSelector>();
    }

    [Fact]
    public async Task DocumentScopeSelector_Renders_Wrapper_Div()
    {
        var json = """{"collections":[],"total_count":0}""";

        await using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddMudServices();
        ctx.Services.AddSingleton<ChatStateService>();
        ctx.Services.AddSingleton(BuildApiClient(json));

        var cut = RenderWithPopover(ctx);

        // After render, the component should contain the wrapper div
        Assert.NotNull(cut.Find(".document-scope-selector"));
    }

    [Fact]
    public async Task DocumentScopeSelector_Renders_MudSelect_Input()
    {
        var json = """
            {
              "collections": [
                {"id": "c1", "name": "Alpha Repo", "document_count": 3},
                {"id": "c2", "name": "Beta Repo",  "document_count": 7}
              ],
              "total_count": 2
            }
            """;

        await using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddMudServices();
        ctx.Services.AddSingleton<ChatStateService>();
        ctx.Services.AddSingleton(BuildApiClient(json));

        var cut = RenderWithPopover(ctx);

        // Wait for OnInitializedAsync to complete and check collections loaded
        cut.WaitForState(() => cut.Instance.LoadedCollections.Count == 2, TimeSpan.FromSeconds(2));

        // The MudSelect should be rendered
        Assert.NotNull(cut.Find(".mud-input-control"));
        Assert.Equal(2, cut.Instance.LoadedCollections.Count);
        Assert.Equal("Alpha Repo", cut.Instance.LoadedCollections[0].Name);
    }

    [Fact]
    public async Task DocumentScopeSelector_Empty_Collections_LoadedCollections_Is_Empty()
    {
        var json = """{"collections":[],"total_count":0}""";

        await using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddMudServices();
        ctx.Services.AddSingleton<ChatStateService>();
        ctx.Services.AddSingleton(BuildApiClient(json));

        var cut = RenderWithPopover(ctx);

        // Wait for load
        cut.WaitForState(() => cut.Find(".document-scope-selector") is not null, TimeSpan.FromSeconds(2));

        // No collections available
        Assert.Empty(cut.Instance.LoadedCollections);
    }

    [Fact]
    public async Task DocumentScopeSelector_SelectCollections_Updates_ChatStateService()
    {
        var json = """
            {
              "collections": [
                {"id": "col-a", "name": "Repo A", "document_count": 1}
              ],
              "total_count": 1
            }
            """;

        await using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddMudServices();
        var state = new ChatStateService();
        ctx.Services.AddSingleton(state);
        ctx.Services.AddSingleton(BuildApiClient(json));

        var cut = RenderWithPopover(ctx);

        // Wait for collections to load
        cut.WaitForState(() => cut.Instance.LoadedCollections.Count >= 1, TimeSpan.FromSeconds(2));

        // Use the public API to programmatically select a collection
        var toSelect = new[] { new DocumentCollectionModel { Id = "col-a", Name = "Repo A", DocumentCount = 1 } };
        cut.Instance.SelectCollections(toSelect);

        Assert.Contains("col-a", state.SelectedCollectionIds);
        Assert.Equal("Repo A", state.ScopeLabel);
    }
}
