namespace DeepWiki.Rag.Core.Snapshots;

/// <summary>
/// No-op snapshot recorder used when <c>Wiki:Snapshots:Enabled</c> is <see langword="false"/> (default).
/// </summary>
public sealed class NullWikiSnapshotRecorder : IWikiSnapshotRecorder
{
    public Task RecordAsync(WikiSnapshotEntry entry, CancellationToken ct = default) => Task.CompletedTask;
}
