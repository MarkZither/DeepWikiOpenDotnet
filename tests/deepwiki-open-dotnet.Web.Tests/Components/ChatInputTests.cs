using Bunit;
using Microsoft.Extensions.DependencyInjection;
using deepwiki_open_dotnet.Web.Components.Shared;
using deepwiki_open_dotnet.Web.Services;
using Xunit;

namespace DeepWiki.Web.Tests.Components;

public class ChatInputTests
{
    [Fact]
    public void ChatInput_Should_Have_Input_And_Send_Button()
    {
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<ChatStateService>();

        var cut = ctx.Render<ChatInput>();

        // Expect input and button elements
        var input = cut.Find("#chat-input");
        var btn = cut.Find("#send-btn");

        Assert.NotNull(input);
        Assert.NotNull(btn);
    }

    [Fact]
    public void ChatInput_Should_Disable_When_Generating()
    {
        using var ctx = new BunitContext();
        var state = new ChatStateService { IsGenerating = true };
        ctx.Services.AddSingleton(state);

        var cut = ctx.Render<ChatInput>();

        // Test expects the send button to be disabled while generating (stub doesn't implement this -> FAILS)
        var btn = cut.Find("#send-btn");
        Assert.True(btn.HasAttribute("disabled"));
    }

    [Fact]
    public void ChatInput_Prevents_Empty_Submission()
    {
        using var ctx = new BunitContext();
        var state = new ChatStateService();
        ctx.Services.AddSingleton(state);

        var cut = ctx.Render<ChatInput>();

        // Click send with empty input - state should not receive a new message (stub not wired)
        var btn = cut.Find("#send-btn");
        btn.Click();

        Assert.Empty(state.Messages);
    }
}
