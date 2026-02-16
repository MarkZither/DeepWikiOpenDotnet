using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using deepwiki_open_dotnet.Web.Components.Pages;
using deepwiki_open_dotnet.Web.Services;
using Xunit;

namespace DeepWiki.Web.Tests.Components;

public class ChatTests
{
    [Fact]
    public void Chat_Renders_Input_And_MessageList()
    {
        using var ctx = new BunitContext();
        ctx.Services.AddMudServices();
        ctx.Services.AddSingleton(new Markdig.MarkdownPipelineBuilder().Build());
        ctx.Services.AddSingleton<ChatStateService>();
        ctx.Services.AddSingleton<deepwiki_open_dotnet.Web.Services.NdJsonStreamParser>();
        ctx.Services.AddSingleton(new deepwiki_open_dotnet.Web.Services.ChatApiClient(new System.Net.Http.HttpClient { BaseAddress = new Uri("https+http://apiservice") }));

        var cut = ctx.Render<Chat>();

        // Expect an input field and a message list (not implemented yet - test should fail)
        cut.Find("#chat-input");
        cut.Find(".message-list");
    }
}
