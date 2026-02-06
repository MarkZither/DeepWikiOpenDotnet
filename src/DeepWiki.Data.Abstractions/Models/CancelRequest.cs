using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace DeepWiki.Data.Abstractions.Models;

/// <summary>
/// Request to cancel an in-flight prompt generation.
/// </summary>
public class CancelRequest
{
    /// <summary>
    /// Session identifier.
    /// </summary>
    [JsonPropertyName("sessionId")]
    [Required(ErrorMessage = "SessionId is required")]
    public required string SessionId { get; init; }

    /// <summary>
    /// Prompt identifier to cancel.
    /// </summary>
    [JsonPropertyName("promptId")]
    [Required(ErrorMessage = "PromptId is required")]
    public required string PromptId { get; init; }
}
