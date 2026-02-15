using System.Text.Json.Serialization;

namespace DeepWiki.Data.Abstractions.Models;

/// <summary>
/// Request to create a new generation session.
/// </summary>
public class SessionRequest
{
    /// <summary>
    /// Optional organization/repository identifier (reserved for future use).
    /// </summary>
    [JsonPropertyName("owner")]
    public string? Owner { get; init; }

    /// <summary>
    /// Optional session metadata/context.
    /// </summary>
    [JsonPropertyName("context")]
    public Dictionary<string, string>? Context { get; init; }
}
