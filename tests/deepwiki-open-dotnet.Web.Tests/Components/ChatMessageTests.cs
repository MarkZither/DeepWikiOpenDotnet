using System.Collections.Generic;
using System.Threading.Tasks;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using deepwiki_open_dotnet.Web.Components.Shared;
using deepwiki_open_dotnet.Web.Models;
using Xunit;

namespace DeepWiki.Web.Tests.Components;

// T031 – US2: bUnit tests for ChatMessage with source citations
public class ChatMessageTests
{
    [Fact]
    public async Task ChatMessage_Displays_Source_Citations_With_Clickable_Links()
    {
        await using var ctx = new BunitContext();
        ctx.JSInterop.SetupVoid("chat.renderMathInElement", _ => true);
        ctx.Services.AddMudServices();
        ctx.Services.AddSingleton(new Markdig.MarkdownPipelineBuilder().Build());

        var message = new ChatMessageModel
        {
            Role = MessageRole.Assistant,
            Text = "Here is a grounded response.",
            Sources =
            {
                new SourceCitation { Title = "Doc A", Url = "https://example.com/a", Score = 0.9f },
                new SourceCitation { Title = "Doc B", Url = "https://example.com/b", Score = 0.7f }
            }
        };

        var cut = ctx.Render<ChatMessage>(p => p.Add(c => c.Message, message));

        var sources = cut.Find(".source-citations");
        Assert.NotNull(sources);

        var links = cut.FindAll(".source-citations a");
        Assert.Equal(2, links.Count);
        Assert.Equal("https://example.com/a", links[0].GetAttribute("href"));
        Assert.Equal("https://example.com/b", links[1].GetAttribute("href"));
    }

    [Fact]
    public void ChatMessage_No_Sources_Section_When_Empty()
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.SetupVoid("chat.renderMathInElement", _ => true);
        ctx.Services.AddSingleton(new Markdig.MarkdownPipelineBuilder().Build());

        var message = new ChatMessageModel
        {
            Role = MessageRole.User,
            Text = "Hello there"
        };

        var cut = ctx.Render<ChatMessage>(p => p.Add(c => c.Message, message));

        var sourceSections = cut.FindAll(".source-citations");
        Assert.Empty(sourceSections);
    }

    [Fact]
    public async Task ChatMessage_Shows_Grounded_Badge_When_Sources_Present()
    {
        await using var ctx = new BunitContext();
        ctx.JSInterop.SetupVoid("chat.renderMathInElement", _ => true);
        ctx.Services.AddMudServices();
        ctx.Services.AddSingleton(new Markdig.MarkdownPipelineBuilder().Build());

        var message = new ChatMessageModel
        {
            Role = MessageRole.Assistant,
            Text = "Grounded response.",
            Sources = { new SourceCitation { Title = "Ref Doc", Url = "https://example.com/ref", Score = 0.85f } }
        };

        var cut = ctx.Render<ChatMessage>(p => p.Add(c => c.Message, message));

        // Verify the grounded badge is present when sources exist
        cut.Find(".grounded-badge");
    }

    [Fact]
    public void ChatMessage_No_Grounded_Badge_Without_Sources()
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.SetupVoid("chat.renderMathInElement", _ => true);
        ctx.Services.AddSingleton(new Markdig.MarkdownPipelineBuilder().Build());

        var message = new ChatMessageModel
        {
            Role = MessageRole.Assistant,
            Text = "Plain response with no citations."
        };

        var cut = ctx.Render<ChatMessage>(p => p.Add(c => c.Message, message));

        var badges = cut.FindAll(".grounded-badge");
        Assert.Empty(badges);
    }

    [Fact]
    public async Task ChatMessage_Source_Citations_Show_Relevance_Score()
    {
        await using var ctx = new BunitContext();
        ctx.JSInterop.SetupVoid("chat.renderMathInElement", _ => true);
        ctx.Services.AddMudServices();
        ctx.Services.AddSingleton(new Markdig.MarkdownPipelineBuilder().Build());

        var message = new ChatMessageModel
        {
            Role = MessageRole.Assistant,
            Text = "Answer with score.",
            Sources = { new SourceCitation { Title = "Scored Doc", Url = "https://example.com/s", Score = 0.95f } }
        };

        var cut = ctx.Render<ChatMessage>(p => p.Add(c => c.Message, message));

        var html = cut.Find(".source-citations").InnerHtml;
        Assert.Contains("0.95", html);
    }
}
