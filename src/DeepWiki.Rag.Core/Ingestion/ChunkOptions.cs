namespace DeepWiki.Rag.Core.Ingestion;

/// <summary>
/// Configuration options for sliding-window text chunking during document ingestion.
/// Bound from the <c>Embedding:Chunking</c> configuration section.
/// </summary>
/// <remarks>
/// Defaults are tuned for <c>nomic-embed-text</c>'s 8 192-token context window:
/// <list type="bullet">
///   <item><c>ChunkSize = 512</c> — small enough for retrieval specificity</item>
///   <item><c>ChunkOverlap = 128</c> — ~25 % overlap retains cross-boundary context</item>
///   <item><c>MaxChunksPerFile = 200</c> — caps memory/storage per file</item>
/// </list>
/// </remarks>
public record ChunkOptions
{
    /// <summary>
    /// Maximum tokens per chunk. Defaults to 512.
    /// </summary>
    public int ChunkSize { get; init; } = 512;

    /// <summary>
    /// Number of overlapping tokens between consecutive chunks. Defaults to 128.
    /// </summary>
    public int ChunkOverlap { get; init; } = 128;

    /// <summary>
    /// Maximum number of chunks stored per source file. Defaults to 200.
    /// Files exceeding this limit are capped and a warning is logged.
    /// </summary>
    public int MaxChunksPerFile { get; init; } = 200;
}
