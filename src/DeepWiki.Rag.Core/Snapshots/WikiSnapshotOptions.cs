namespace DeepWiki.Rag.Core.Snapshots;

/// <summary>Options for LLM snapshot recording (bound from <c>Wiki:Snapshots</c> config section).</summary>
public sealed class WikiSnapshotOptions
{
    /// <summary>When <see langword="false"/> (default), the <see cref="NullWikiSnapshotRecorder"/> is used and no files are written.</summary>
    public bool Enabled { get; set; }

    /// <summary>Directory where snapshot JSON files are written. Relative paths are resolved against the current working directory.</summary>
    public string OutputPath { get; set; } = "./llm-snapshots/wiki/";
}
