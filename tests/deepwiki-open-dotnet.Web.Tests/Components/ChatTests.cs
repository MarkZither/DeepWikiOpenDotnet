using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using deepwiki_open_dotnet.Web.Components.Pages;
using deepwiki_open_dotnet.Web.Services;
using DeepWiki.Web.Tests.Fixtures;
using Xunit;

namespace DeepWiki.Web.Tests.Components;

public class ChatTests
{
    [Fact]
    public async Task Chat_Renders_Input_And_MessageList()
    {
        await using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddMudServices();
        ctx.Services.AddSingleton(new Markdig.MarkdownPipelineBuilder().Build());
        ctx.Services.AddSingleton<ChatStateService>();
        ctx.Services.AddSingleton<NdJsonStreamParser>();

        // Session creation returns a valid session response
        var sessionId = Guid.NewGuid();
        var handler = new FakeHttpHandler((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $"{{\"sessionId\":\"{sessionId}\"}}",
                Encoding.UTF8, "application/json")
        }));
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://apiservice") };
        ctx.Services.AddSingleton(new ChatApiClient(http));

        var cut = ctx.Render<Chat>();

        // Expect an input field and a message list
        cut.Find("#chat-input");
        cut.Find(".message-list");
    }
}
