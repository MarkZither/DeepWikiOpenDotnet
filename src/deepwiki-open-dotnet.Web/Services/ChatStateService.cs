using System;
using System.Collections.Generic;
using System.Linq;
using deepwiki_open_dotnet.Web.Models;

namespace deepwiki_open_dotnet.Web.Services;

public class ChatStateService
{
    private readonly List<ChatMessageModel> _messages = new();

    public IReadOnlyList<ChatMessageModel> Messages => _messages;

    public bool IsGenerating { get; set; }

    public HashSet<string> SelectedCollectionIds { get; } = new();

    public event Action? StateChanged;

    public void AddMessage(ChatMessageModel message)
    {
        _messages.Add(message);
        StateChanged?.Invoke();
    }

    public void UpdateLastMessage(Action<ChatMessageModel> update)
    {
        if (_messages.Count == 0)
            return;

        update(_messages[^1]);
        StateChanged?.Invoke();
    }

    public void ClearMessages()
    {
        _messages.Clear();
        StateChanged?.Invoke();
    }

    public void SetSelectedCollections(IEnumerable<string> ids)
    {
        SelectedCollectionIds.Clear();
        foreach (var id in ids.Where(i => !string.IsNullOrWhiteSpace(i)))
            SelectedCollectionIds.Add(id!);

        StateChanged?.Invoke();
    }
}
