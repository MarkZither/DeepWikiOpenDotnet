using System;
using deepwiki_open_dotnet.Web.Models;
using deepwiki_open_dotnet.Web.Services;
using Xunit;

namespace DeepWiki.Web.Tests.Services;

public class ChatStateServiceTests
{
    [Fact]
    public void AddMessage_Should_AddMessage_And_Raise_StateChanged()
    {
        var svc = new ChatStateService();
        var fired = false;
        svc.StateChanged += () => fired = true;

        svc.AddMessage(new ChatMessageModel { Role = MessageRole.User, Text = "hello" });

        Assert.True(fired);
        Assert.Single(svc.Messages);
        Assert.Equal("hello", svc.Messages[0].Text);
    }

    [Fact]
    public void ClearMessages_Should_Remove_All_Messages_But_Preserve_Collections()
    {
        var svc = new ChatStateService();
        svc.AddMessage(new ChatMessageModel { Role = MessageRole.User, Text = "one" });
        svc.AddMessage(new ChatMessageModel { Role = MessageRole.Assistant, Text = "two" });
        svc.SetSelectedCollections(new[] { "c1" });

        svc.ClearMessages();

        Assert.Empty(svc.Messages);
        Assert.Contains("c1", svc.SelectedCollectionIds);
    }

    [Fact]
    public void SetSelectedCollections_Should_Update_Set_And_Raise_Event()
    {
        var svc = new ChatStateService();
        var fired = false;
        svc.StateChanged += () => fired = true;

        svc.SetSelectedCollections(new[] { "a", "b" });

        Assert.True(fired);
        Assert.Equal(2, svc.SelectedCollectionIds.Count);
        Assert.Contains("a", svc.SelectedCollectionIds);
    }
}
