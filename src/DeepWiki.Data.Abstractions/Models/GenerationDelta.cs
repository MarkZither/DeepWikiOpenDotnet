using System.Text.Json.Serialization;

namespace DeepWiki.Data.Abstractions.Models;

/// <summary>
/// Represents a single streaming token delta event emitted during generation.
/// </summary>
public class GenerationDelta
{
    /// <summary>
    /// Parent prompt identifier.
    /// </summary>
    [JsonPropertyName("promptId")]
    public required string PromptId { get; init; }

    /// <summary>
    /// Event type: "token", "done", or "error".
    /// </summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>
    /// Monotonic sequence number (0-based, no gaps).
    /// </summary>
    [JsonPropertyName("seq")]
    public int Seq { get; set; }

    /// <summary>
    /// Token delta text (nullable for done/error events).
    /// </summary>
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    /// <summary>
    /// Role: "assistant", "system", or "user".
    /// </summary>
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    /// <summary>
    /// Optional provider-specific metadata or error details.
    /// For error events, must include { "code": "...", "message": "..." }.
    /// </summary>
    [JsonPropertyName("metadata")]
    public object? Metadata { get; init; }
}
