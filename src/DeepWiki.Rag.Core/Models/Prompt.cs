namespace DeepWiki.Rag.Core.Models;

/// <summary>
/// Represents a single prompt submission within a session.
/// </summary>
public class Prompt
{
    /// <summary>
    /// Unique prompt identifier (GUID).
    /// </summary>
    public required string PromptId { get; init; }

    /// <summary>
    /// Parent session identifier.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// User prompt text.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Optional idempotency key for retry-safe requests.
    /// Duplicate keys within same session return cached response.
    /// </summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>
    /// Current prompt status.
    /// </summary>
    public PromptStatus Status { get; set; }

    /// <summary>
    /// Prompt creation timestamp (UTC).
    /// </summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>
    /// Total tokens generated (updated when generation completes).
    /// </summary>
    public int TokenCount { get; set; }
}
