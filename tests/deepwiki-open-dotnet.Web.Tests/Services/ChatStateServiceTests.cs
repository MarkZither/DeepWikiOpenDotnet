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

    // T032 – US2: default scope indicator logic
    [Fact]
    public void ScopeLabel_Returns_AllDocuments_When_No_Collections_Selected()
    {
        var svc = new ChatStateService();

        Assert.Equal("All Documents", svc.ScopeLabel);
    }

    [Fact]
    public void ScopeLabel_Returns_CollectionCount_When_Collections_Selected()
    {
        var svc = new ChatStateService();
        svc.SetSelectedCollections(new[] { "c1", "c2", "c3" });

        Assert.Equal("3 collections", svc.ScopeLabel);
    }

    [Fact]
    public void ScopeLabel_Returns_AllDocuments_After_Collections_Cleared()
    {
        var svc = new ChatStateService();
        svc.SetSelectedCollections(new[] { "c1" });
        svc.SetSelectedCollections(System.Array.Empty<string>());

        Assert.Equal("All Documents", svc.ScopeLabel);
    }

    // T042 – US3: collection selection via DocumentCollectionModel overload
    [Fact]
    public void SetSelectedCollections_Models_Populates_SelectedCollectionIds()
    {
        var svc = new ChatStateService();

        svc.SetSelectedCollections(new[]
        {
            new deepwiki_open_dotnet.Web.Models.DocumentCollectionModel { Id = "id-1", Name = "Repo One", DocumentCount = 10 },
            new deepwiki_open_dotnet.Web.Models.DocumentCollectionModel { Id = "id-2", Name = "Repo Two", DocumentCount = 5 }
        });

        Assert.Contains("id-1", svc.SelectedCollectionIds);
        Assert.Contains("id-2", svc.SelectedCollectionIds);
    }

    [Fact]
    public void SetSelectedCollections_Models_Fires_StateChanged()
    {
        var svc = new ChatStateService();
        var fired = false;
        svc.StateChanged += () => fired = true;

        svc.SetSelectedCollections(new[]
        {
            new deepwiki_open_dotnet.Web.Models.DocumentCollectionModel { Id = "id-1", Name = "Repo One", DocumentCount = 2 }
        });

        Assert.True(fired);
    }

    [Fact]
    public void SetSelectedCollections_Models_ScopeLabel_Shows_Names()
    {
        var svc = new ChatStateService();

        svc.SetSelectedCollections(new[]
        {
            new deepwiki_open_dotnet.Web.Models.DocumentCollectionModel { Id = "id-1", Name = "Alpha", DocumentCount = 3 },
            new deepwiki_open_dotnet.Web.Models.DocumentCollectionModel { Id = "id-2", Name = "Beta",  DocumentCount = 1 }
        });

        // ScopeLabel should show names, not the generic count
        Assert.Contains("Alpha", svc.ScopeLabel);
        Assert.Contains("Beta", svc.ScopeLabel);
    }

    [Fact]
    public void SetSelectedCollections_EmptyModels_Clears_Selection_And_Returns_AllDocuments()
    {
        var svc = new ChatStateService();
        svc.SetSelectedCollections(new[]
        {
            new deepwiki_open_dotnet.Web.Models.DocumentCollectionModel { Id = "id-1", Name = "Repo One", DocumentCount = 2 }
        });

        svc.SetSelectedCollections(System.Array.Empty<deepwiki_open_dotnet.Web.Models.DocumentCollectionModel>());

        Assert.Empty(svc.SelectedCollectionIds);
        Assert.Equal("All Documents", svc.ScopeLabel);
    }

    [Fact]
    public void SetSelectedCollections_Models_Replaces_Previous_Selection()
    {
        var svc = new ChatStateService();
        svc.SetSelectedCollections(new[]
        {
            new deepwiki_open_dotnet.Web.Models.DocumentCollectionModel { Id = "old-1", Name = "Old", DocumentCount = 1 }
        });

        svc.SetSelectedCollections(new[]
        {
            new deepwiki_open_dotnet.Web.Models.DocumentCollectionModel { Id = "new-1", Name = "New", DocumentCount = 2 }
        });

        Assert.DoesNotContain("old-1", svc.SelectedCollectionIds);
        Assert.Contains("new-1", svc.SelectedCollectionIds);
    }

    /// <summary>
    /// T065 / SC-002: Adding 50+ messages and performing state operations should complete well within 1 second
    /// and show no degradation — the message list is a simple List&lt;T&gt; and does not copy on every add.
    /// </summary>
    [Fact]
    public void Performance_50Plus_Messages_No_Degradation()
    {
        const int MessageCount = 60; // exceeds SC-002 threshold of 50
        var svc = new ChatStateService();
        var stateChangeCount = 0;
        svc.StateChanged += () => stateChangeCount++;

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Add 60 alternating user/assistant messages
        for (int i = 0; i < MessageCount; i++)
        {
            var role = i % 2 == 0 ? MessageRole.User : MessageRole.Assistant;
            svc.AddMessage(new ChatMessageModel { Role = role, Text = $"Message {i}" });
        }

        // Simulate token streaming — 500 appends to the last message
        for (int i = 0; i < 500; i++)
        {
            svc.UpdateLastMessage(m => m.Text += "token ");
        }

        sw.Stop();

        // All messages must be present
        Assert.Equal(MessageCount, svc.Messages.Count);

        // The last message text was accumulated via UpdateLastMessage
        Assert.Contains("token", svc.Messages[^1].Text);

        // StateChanged fired for each AddMessage + UpdateLastMessage call
        Assert.Equal(MessageCount + 500, stateChangeCount);

        // Performance: all operations must complete well under 500 ms (generous upper bound for CI)
        Assert.True(sw.ElapsedMilliseconds < 500,
            $"Performance test exceeded 500 ms threshold (took {sw.ElapsedMilliseconds} ms). " +
            "Possible regression in ChatStateService message management.");
    }
}
