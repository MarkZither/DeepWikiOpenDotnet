using System.Collections.Concurrent;

namespace DeepWiki.Rag.Core.Services;

/// <summary>
/// Tracks in-flight prompt CancellationTokenSource instances so the application
/// can cancel all active prompts during graceful shutdown.
/// </summary>
public sealed class PromptCancellationRegistry
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _map = new();

    public void Register(string promptId, CancellationTokenSource cts)
    {
        if (string.IsNullOrEmpty(promptId) || cts == null) return;
        _map[promptId] = cts;
    }

    public void Unregister(string promptId)
    {
        if (string.IsNullOrEmpty(promptId)) return;
        _map.TryRemove(promptId, out _);
    }

    public void CancelAll()
    {
        foreach (var kv in _map.ToArray())
        {
            try { kv.Value.Cancel(); } catch { }
        }
    }

    public IReadOnlyCollection<string> ActivePromptIds() => _map.Keys.ToList().AsReadOnly();
}
