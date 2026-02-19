using System;
using System.Collections.Generic;
using System.Linq;
using deepwiki_open_dotnet.Web.Models;

namespace deepwiki_open_dotnet.Web.Services;

public class ChatStateService
{
    private readonly List<ChatMessageModel> _messages = new();
    private readonly List<DocumentCollectionModel> _selectedCollectionModels = new();

    public IReadOnlyList<ChatMessageModel> Messages => _messages;

    public bool IsGenerating { get; set; }

    public HashSet<string> SelectedCollectionIds { get; } = new();

    /// <summary>
    /// Full collection models for the current selection (populated when selecting via DocumentCollectionModel overload).
    /// </summary>
    public IReadOnlyList<DocumentCollectionModel> SelectedCollectionModels => _selectedCollectionModels;

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

    /// <summary>
    /// Sets the selected collections by ID only (names not available, ScopeLabel shows count).
    /// </summary>
    public void SetSelectedCollections(IEnumerable<string> ids)
    {
        _selectedCollectionModels.Clear();
        SelectedCollectionIds.Clear();
        foreach (var id in ids.Where(i => !string.IsNullOrWhiteSpace(i)))
            SelectedCollectionIds.Add(id!);

        StateChanged?.Invoke();
    }

    /// <summary>
    /// Sets the selected collections from full models (ScopeLabel shows collection names).
    /// </summary>
    public void SetSelectedCollections(IEnumerable<DocumentCollectionModel> collections)
    {
        var list = collections.ToList();
        _selectedCollectionModels.Clear();
        _selectedCollectionModels.AddRange(list);

        SelectedCollectionIds.Clear();
        foreach (var col in list)
            SelectedCollectionIds.Add(col.Id);

        StateChanged?.Invoke();
    }

    /// <summary>
    /// Human-readable description of the current retrieval scope.
    /// Shows collection names when selected via model overload, "N collections" when selected via ID,
    /// or "All Documents" when nothing is selected.
    /// </summary>
    public string ScopeLabel
    {
        get
        {
            if (_selectedCollectionModels.Count > 0)
                return string.Join(", ", _selectedCollectionModels.Select(c => c.Name));
            if (SelectedCollectionIds.Count > 0)
                return $"{SelectedCollectionIds.Count} collections";
            return "All Documents";
        }
    }
}
