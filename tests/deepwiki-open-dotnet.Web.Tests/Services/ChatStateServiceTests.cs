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
}
