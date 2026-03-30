namespace DeepWiki.Rag.Core.Models;

/// <summary>
/// Configuration options for wiki generation pipeline.
/// Bound from "Wiki:Generation" configuration section.
/// </summary>
public class WikiGenerationOptions
{
    /// <summary>
    /// Generation mode: "sequential" or "parallel".
    /// Sequential is the default (safe for rate-limited providers).
    /// </summary>
    public string Mode { get; set; } = "sequential";

    /// <summary>
    /// Maximum number of pages to generate concurrently in parallel mode.
    /// Ignored in sequential mode.
    /// </summary>
    public int MaxParallelPages { get; set; } = 3;

    /// <summary>
    /// Maximum number of retries when TOC generation produces unparseable JSON.
    /// </summary>
    public int MaxTocRetries { get; set; } = 2;

    /// <summary>
    /// Maximum token budget per page generation call.
    /// Limits context window usage for both local (Ollama) and remote providers.
    /// </summary>
    public int PageTokenLimit { get; set; } = 4000;
}
