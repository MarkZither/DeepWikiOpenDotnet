using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace DeepWiki.Data.Abstractions.Models;

/// <summary>
/// Request to submit a prompt within a session for generation.
/// </summary>
public class PromptRequest
{
    /// <summary>
    /// Target session ID.
    /// </summary>
    [JsonPropertyName("sessionId")]
    [Required(ErrorMessage = "SessionId is required")]
    public required string SessionId { get; init; }

    /// <summary>
    /// User prompt text.
    /// </summary>
    [JsonPropertyName("prompt")]
    [Required(ErrorMessage = "Prompt is required")]
    [MinLength(1, ErrorMessage = "Prompt cannot be empty")]
    public required string Prompt { get; init; }

    /// <summary>
    /// Optional idempotency key for retry-safe requests.
    /// </summary>
    [JsonPropertyName("idempotencyKey")]
    public string? IdempotencyKey { get; init; }

    /// <summary>
    /// Number of documents to retrieve for RAG context (default 5, range 1-20).
    /// </summary>
    [JsonPropertyName("topK")]
    [Range(1, 20, ErrorMessage = "TopK must be between 1 and 20")]
    public int TopK { get; init; } = 5;

    /// <summary>
    /// Optional retrieval filters (e.g., repoUrl, filePath).
    /// </summary>
    [JsonPropertyName("filters")]
    public Dictionary<string, string>? Filters { get; init; }
}
