namespace DeepWiki.Rag.Core.Snapshots;

/// <summary>
/// Records LLM interaction snapshots for auditing and offline test fixture generation.
/// Register the no-op <see cref="NullWikiSnapshotRecorder"/> when snapshotting is disabled,
/// and <see cref="WikiSnapshotRecorder"/> when enabled.
/// </summary>
public interface IWikiSnapshotRecorder
{
    /// <summary>Persist a single snapshot entry.</summary>
    Task RecordAsync(WikiSnapshotEntry entry, CancellationToken ct = default);
}
