namespace DeepWiki.ApiService.Configuration;

/// <summary>
/// Configuration options for the embedding provider.
/// </summary>
public sealed class EmbeddingOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "Embedding";

    /// <summary>
    /// Maximum time in seconds to wait for an embedding response before cancelling.
    /// Increase for slow local models such as Ollama on CPU.
    /// Defaults to 120 seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 120;
}
